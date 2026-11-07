package vn.networkadmin.client.runtime;

import vn.networkadmin.network.application.NetworkScanner;
import vn.networkadmin.network.application.ScanCancellation;
import vn.networkadmin.network.domain.NetworkDevice;
import vn.networkadmin.network.domain.NetworkInterfaceInfo;
import vn.networkadmin.network.domain.PingResult;
import vn.networkadmin.network.domain.ScanSubnet;
import vn.networkadmin.network.ports.NetworkInventoryPort;
import vn.networkadmin.network.ports.PingPort;
import vn.networkadmin.protocol.control.JsonLineProtocol;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.ports.ClientConnectionEvents;
import vn.networkadmin.transport.ports.ClientConnectionSupervisorPort;
import vn.networkadmin.transport.ports.ClientControlChannel;
import vn.networkadmin.transport.domain.ClientIdentity;
import vn.networkadmin.client.ports.screenshare.ClientScreenShareMessageHandler;
import vn.networkadmin.client.ports.ClientFeatureMessageHandler;

import java.net.InetAddress;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.atomic.AtomicReference;

/** Client control-plane application service. Connects once per session, publishes inventory and handles network commands. */
public final class ClientAgentRuntime implements AutoCloseable, ClientConnectionEvents {
    private final NetworkInventoryPort inventory;
    private final PingPort ping;
    private final NetworkScanner scanner;
    private final ExecutorService commandWorkers;
    private final String clientId, clientName;
    private final AtomicReference<ScanTask> activeScan = new AtomicReference<>();
    private final Semaphore pingSlots = new Semaphore(4);
    private final ClientConnectionSupervisorPort supervisor;
    private final ScheduledExecutorService publisherScheduler;
    private final ClientScreenShareMessageHandler screenShareMessages;
    private final ClientFeatureMessageHandler featureMessages;
    private volatile Thread runner;
    private volatile ClientControlChannel current;
    private volatile ScheduledFuture<?> networkInfoPublisher;

    public ClientAgentRuntime(String clientId, String clientName,
                              NetworkInventoryPort inventory, PingPort ping, NetworkScanner scanner,
                              ExecutorService commandWorkers, ScheduledExecutorService publisherScheduler,
                              ClientConnectionSupervisorPort supervisor) {
        this(clientId, clientName, inventory, ping, scanner, commandWorkers, publisherScheduler, supervisor,
                ClientScreenShareMessageHandler.NO_OP, ClientFeatureMessageHandler.NO_OP);
    }

    public ClientAgentRuntime(String clientId, String clientName,
                              NetworkInventoryPort inventory, PingPort ping, NetworkScanner scanner,
                              ExecutorService commandWorkers, ScheduledExecutorService publisherScheduler,
                              ClientConnectionSupervisorPort supervisor,
                              ClientScreenShareMessageHandler screenShareMessages) {
        this(clientId, clientName, inventory, ping, scanner, commandWorkers, publisherScheduler, supervisor,
                screenShareMessages, ClientFeatureMessageHandler.NO_OP);
    }

    public ClientAgentRuntime(String clientId, String clientName,
                              NetworkInventoryPort inventory, PingPort ping, NetworkScanner scanner,
                              ExecutorService commandWorkers, ScheduledExecutorService publisherScheduler,
                              ClientConnectionSupervisorPort supervisor,
                              ClientScreenShareMessageHandler screenShareMessages,
                              ClientFeatureMessageHandler featureMessages) {
        this.clientId = Objects.requireNonNull(clientId); this.clientName = Objects.requireNonNull(clientName);
        this.inventory = Objects.requireNonNull(inventory); this.ping = Objects.requireNonNull(ping);
        this.scanner = Objects.requireNonNull(scanner); this.commandWorkers = Objects.requireNonNull(commandWorkers);
        this.publisherScheduler = Objects.requireNonNull(publisherScheduler);
        this.supervisor = Objects.requireNonNull(supervisor);
        this.screenShareMessages = Objects.requireNonNull(screenShareMessages);
        this.featureMessages = Objects.requireNonNull(featureMessages);
    }

    public static String localClientId() {
        String machine = System.getenv().get("COMPUTERNAME");
        if (machine == null || machine.isBlank()) {
            try { machine = InetAddress.getLocalHost().getHostName(); } catch (Exception ex) { machine = "unknown"; }
        }
        String domain = System.getenv().getOrDefault("USERDOMAIN", "");
        return ClientIdentity.stableId(machine, domain, System.getProperty("user.name", "unknown"));
    }

    public void start() {
        if (runner != null) throw new IllegalStateException("Client runtime already started.");
        runner = Thread.ofPlatform().daemon(true).name("network-admin-client-runtime").start(() -> supervisor.runUntilInterrupted(this));
    }

    public void runUntilStopped() { supervisor.runUntilInterrupted(this); }

    @Override public void connected(ClientControlChannel connection) {
        screenShareMessages.connected(connection);
        featureMessages.connected(connection);
        current = connection;
        Map<String, Object> initial = networkInfoPayload();
        try { connection.send(MessageType.NETWORK_INFO, initial); }
        catch (java.io.IOException ex) { connection.close(); throw new IllegalStateException("Could not send initial network inventory.", ex); }
        ScheduledFuture<?> old = networkInfoPublisher;
        if (old != null) old.cancel(true);
        java.util.concurrent.atomic.AtomicReference<Map<String, Object>> last = new java.util.concurrent.atomic.AtomicReference<>(initial);
        networkInfoPublisher = publisherScheduler.scheduleWithFixedDelay(() -> {
            try {
                Map<String, Object> changed = networkInfoPayload();
                if (!last.get().equals(changed)) { connection.send(MessageType.NETWORK_INFO, changed); last.set(changed); }
            } catch (Exception ex) { connection.close(); }
        }, 15, 15, TimeUnit.SECONDS);
    }

    @Override public void disconnected(ClientControlChannel connection) {
        screenShareMessages.disconnected(connection);
        featureMessages.disconnected(connection);
        ScanTask scan = activeScan.get();
        if (scan != null && scan.channel == connection) scan.cancellation.cancel();
        if (current == connection) current = null;
        ScheduledFuture<?> publisher = networkInfoPublisher;
        networkInfoPublisher = null;
        if (publisher != null) publisher.cancel(true);
    }

    @Override public void messageReceived(ClientControlChannel source, MessageEnvelope message) {
        if (message.type() == MessageType.SCREEN_SHARE_REQUEST
                || message.type() == MessageType.SCREEN_CHANNEL_READY
                || message.type() == MessageType.SCREEN_SHARE_STOP) {
            screenShareMessages.handle(source, message);
            return;
        }
        if (message.type() == MessageType.CONTROL_REQUEST || message.type() == MessageType.CONTROL_INPUT_EVENT
                || message.type() == MessageType.CONTROL_STOP || message.type() == MessageType.REMOTE_CHAT_MESSAGE) {
            ClientControlChannel active = currentConnection(source);
            featureMessages.handle(active, message);
            return;
        }
        if (message.type() == MessageType.REMOTE_ADMINISTRATION_CANCEL) {
            cancelScan(source, message);
            return;
        }
        if (message.type() == MessageType.PING_REQUEST) {
            ClientControlChannel connection = currentConnection(source);
            commandWorkers.execute(() -> runPing(connection, message));
        } else if (message.type() == MessageType.SCAN_REQUEST) {
            ClientControlChannel connection = currentConnection(source);
            ScanTask previous = activeScan.get();
            if (previous != null && !previous.channel.isConnected()) activeScan.compareAndSet(previous, null);
            ScanTask task = new ScanTask(connection, message.requestId(), new ScanCancellation());
            if (!activeScan.compareAndSet(null, task)) {
                sendScanResult(connection, message.requestId(), false, "", "A scan is already running on this client.", List.of());
                return;
            }
            commandWorkers.execute(() -> runScan(connection, message, task));
        }
    }

    private ClientControlChannel currentConnection(ClientControlChannel source) {
        ClientControlChannel connection = current;
        if (connection == null || connection != source || !connection.isConnected()) throw new IllegalStateException("Client is disconnected.");
        return connection;
    }

    @Override public void failed(Exception error) {
        System.getLogger(getClass().getName()).log(System.Logger.Level.WARNING, "Client control connection failed.", error);
        screenShareMessages.failed(error);
    }

    private void runPing(ClientControlChannel connection, MessageEnvelope message) {
        boolean acquired = false;
        try {
            pingSlots.acquire(); acquired = true;
            Map<?, ?> payload = objectPayload(message);
            String target = payload.get("target") instanceof String s ? s.trim() : "";
            PingResult result = target.isBlank()
                    ? new PingResult(target, false, 0, "Invalid ping target", 0, 0, null, List.of())
                    : ping.pingSeries(target, 1000, 4);
            connection.send(MessageType.PING_RESULT, message.requestId(), pingPayload(result));
        } catch (Exception ex) { sendPingFailure(connection, message.requestId(), ex); }
        finally { if (acquired) pingSlots.release(); }
    }

    private void runScan(ClientControlChannel connection, MessageEnvelope message, ScanTask task) {
        String subnet = "";
        try {
            Map<?, ?> payload = objectPayload(message);
            subnet = payload.get("subnet") instanceof String s ? s.trim() : "";
            int timeout = payload.get("timeoutMs") instanceof Number n ? n.intValue() : ScanSubnet.DEFAULT_TIMEOUT_MS;
            if (subnet.isBlank()) subnet = inventory.interfaces().stream().filter(i -> !i.virtual())
                    .map(NetworkInterfaceInfo::subnet).filter(s -> !s.isBlank()).findFirst()
                    .orElseThrow(() -> new IllegalStateException("No local IPv4 subnet is available."));
            ScanSubnet normalized = ScanSubnet.parse(subnet).orElseThrow(() -> new IllegalArgumentException(ScanSubnet.HELP));
            boolean locallyManaged = inventory.interfaces().stream().filter(info -> !info.virtual())
                    .map(NetworkInterfaceInfo::subnet).map(ScanSubnet::parse).flatMap(java.util.Optional::stream)
                    .anyMatch(local -> local.contains(normalized));
            if (!locallyManaged) throw new IllegalArgumentException("Scan range must be inside a non-virtual Client network interface subnet.");
            String scanSubnet = normalized.toString();
            List<NetworkDevice> devices = scanner.scan(scanSubnet, timeout, progress -> {
                if (progress.scannedHosts() % 10 == 0 || progress.scannedHosts() == progress.totalHosts())
                    sendScanProgress(connection, message.requestId(), scanSubnet, progress);
            }, task.cancellation);
            sendScanResult(connection, message.requestId(), true, normalized.toString(), "", devices);
        } catch (java.util.concurrent.CancellationException ex) {
            sendScanResult(connection, message.requestId(), false, subnet, "Scan canceled.", List.of());
        } catch (InterruptedException ex) {
            Thread.currentThread().interrupt(); sendScanResult(connection, message.requestId(), false, subnet, "Scan canceled.", List.of());
        } catch (Exception ex) { sendScanResult(connection, message.requestId(), false, subnet, "Scan failed: " + ex.getMessage(), List.of()); }
        finally { activeScan.compareAndSet(task, null); }
    }

    private void cancelScan(ClientControlChannel source, MessageEnvelope message) {
        ScanTask task = activeScan.get();
        if (task == null || task.channel != source || !source.isConnected() || !task.requestId.equals(message.requestId())
                || !(message.data() instanceof Map<?, ?> payload)
                || !source.sessionId().equals(payload.get("sessionId"))
                || !task.requestId.equals(payload.get("targetRequestId"))) return;
        task.cancellation.cancel();
    }

    private void sendScanProgress(ClientControlChannel connection, String requestId, String subnet, NetworkScanner.Progress progress) {
        try { connection.send(MessageType.SCAN_RESULT, requestId, Map.of("progress", true, "success", true,
                "subnet", subnet, "scannedHosts", progress.scannedHosts(), "totalHosts", progress.totalHosts(),
                "onlineHosts", progress.onlineHosts(), "currentIpAddress", progress.currentIpAddress(), "devices", List.of())); }
        catch (java.io.IOException error) { connection.close(); }
    }

    private void sendPingFailure(ClientControlChannel connection, String requestId, Exception ex) {
        try { connection.send(MessageType.PING_RESULT, requestId, pingPayload(new PingResult("", false, 0,
                "Ping failed: " + ex.getMessage(), 0, 0, null, List.of()))); }
        catch (java.io.IOException ignored) { connection.close(); }
    }

    private void sendScanResult(ClientControlChannel connection, String requestId, boolean success,
                                String subnet, String error, List<NetworkDevice> devices) {
        if (connection == null) return;
        List<Map<String, Object>> mapped = devices.stream().map(ClientAgentRuntime::devicePayload).toList();
        try { connection.send(MessageType.SCAN_RESULT, requestId, Map.of("progress", false, "success", success, "subnet", subnet,
                "errorMessage", error, "devices", mapped)); }
        catch (java.io.IOException ex) { connection.close(); }
    }

    private Map<String, Object> networkInfoPayload() {
        List<Map<String, Object>> interfaces = inventory.interfaces().stream().map(info -> {
            Map<String, Object> mapped = new LinkedHashMap<>();
            mapped.put("name", info.name()); mapped.put("description", info.description());
            mapped.put("ipAddress", info.ipAddress()); mapped.put("macAddress", info.macAddress());
            mapped.put("gateway", info.gateway()); mapped.put("subnet", info.subnet());
            return mapped;
        }).toList();
        String hostname;
        try { hostname = InetAddress.getLocalHost().getHostName(); } catch (Exception ex) { hostname = clientName; }
        return Map.of("hostname", hostname, "osName", System.getProperty("os.name", "Unknown"),
                "osVersion", System.getProperty("os.version", "Unknown"),
                "architecture", System.getProperty("os.arch", "Unknown"), "interfaces", interfaces);
    }

    private static Map<String, Object> pingPayload(PingResult result) {
        List<Map<String, Object>> attempts = result.attempts().stream().map(a -> {
            Map<String, Object> value = new LinkedHashMap<>(); value.put("number", a.number()); value.put("success", a.success());
            value.put("responseTimeMs", a.responseTimeMs()); value.put("status", a.status()); return value;
        }).toList();
        Map<String, Object> mapped = new LinkedHashMap<>();
        mapped.put("host", result.host()); mapped.put("success", result.success()); mapped.put("roundtripTimeMs", result.roundtripTimeMs());
        mapped.put("statusMessage", result.statusMessage()); mapped.put("sent", result.sent()); mapped.put("received", result.received());
        mapped.put("lost", result.lost()); mapped.put("packetLossPercent", result.packetLossPercent());
        mapped.put("averageResponseTimeMs", result.averageResponseTimeMs()); mapped.put("attempts", attempts);
        return mapped;
    }

    private static Map<String, Object> devicePayload(NetworkDevice device) {
        Map<String, Object> mapped = new LinkedHashMap<>(); mapped.put("ipAddress", device.ipAddress());
        mapped.put("macAddress", device.macAddress()); mapped.put("isOnline", device.online());
        mapped.put("responseTimeMs", device.responseTimeMs()); mapped.put("lastSeen", null); return mapped;
    }

    private static Map<?, ?> objectPayload(MessageEnvelope message) {
        if (!(message.data() instanceof Map<?, ?> payload)) throw new IllegalArgumentException("Command payload must be an object.");
        return payload;
    }

    @Override public void close() {
        supervisor.close();
        Thread activeRunner = runner; if (activeRunner != null) activeRunner.interrupt();
        ScheduledFuture<?> publisher = networkInfoPublisher; networkInfoPublisher = null;
        if (publisher != null) publisher.cancel(true);
        ScanTask scan = activeScan.getAndSet(null); if (scan != null) scan.cancellation.cancel();
        current = null;
    }

    private record ScanTask(ClientControlChannel channel, String requestId, ScanCancellation cancellation) { }
}
