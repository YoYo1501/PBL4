package vn.networkadmin.client.application.remoteadministration;

import vn.networkadmin.client.ports.ClientFeatureMessageHandler;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.ports.ClientControlChannel;

import java.io.IOException;
import java.util.Map;
import java.util.Objects;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;

/** Applies the trusted Admin's allow-listed power commands; shutdown/restart remain cancellable until execution. */
public final class ClientRemoteAdministrationService implements ClientFeatureMessageHandler {
    private final String clientId;
    private final RemoteAdministrationClientView view;
    private final RemoteAdministrationPort commands;
    private final ScheduledExecutorService scheduler;
    private final ConcurrentHashMap<String, Pending> pending = new ConcurrentHashMap<>();

    public ClientRemoteAdministrationService(String clientId, RemoteAdministrationClientView view,
                                             RemoteAdministrationPort commands,
                                             ScheduledExecutorService scheduler) {
        this.clientId = Objects.requireNonNull(clientId);
        this.view = Objects.requireNonNull(view);
        this.commands = Objects.requireNonNull(commands);
        this.scheduler = Objects.requireNonNull(scheduler);
    }

    @Override public void handle(ClientControlChannel channel, MessageEnvelope message) {
        if (message.type() == MessageType.REMOTE_ADMINISTRATION_CANCEL) {
            cancel(channel, message);
        } else if (message.type() == MessageType.REMOTE_ADMINISTRATION_COMMAND) {
            receive(channel, message);
        }
    }

    @Override public void disconnected(ClientControlChannel channel) {
        pending.forEach((id, operation) -> {
            if (operation.channel == channel && pending.remove(id, operation)) {
                operation.future.cancel(false);
                view.clearCountdown("Admin connection closed; pending operation canceled.");
            }
        });
    }

    private void receive(ClientControlChannel channel, MessageEnvelope message) {
        if (!channel.isConnected() || message.requestId() == null || message.requestId().isBlank()
                || !(message.data() instanceof Map<?, ?> payload)) return;
        Object commandValue = payload.get("command");
        if (!(commandValue instanceof Number number)) return;
        int command = number.intValue();
        if (command == 4) {
            if (pending.containsKey(message.requestId())) return;
            try {
                commands.lockScreen();
                sendResult(channel, message.requestId(), command, 3, "Workstation locked.", "");
                view.clearCountdown("Workstation locked by Admin.");
            } catch (Exception error) {
                sendResult(channel, message.requestId(), command, 4, "", safeMessage(error));
                view.clearCountdown("Lock failed: " + safeMessage(error));
            }
            return;
        }
        if (command != 2 && command != 3) return;
        int seconds = payload.get("countdownSeconds") instanceof Number n ? n.intValue() : 30;
        if (seconds < 5 || seconds > 60 || pending.containsKey(message.requestId())) return;
        String label = command == 2 ? "Shutdown" : "Restart";
        Pending operation = new Pending(channel, command);
        Pending prior = pending.putIfAbsent(message.requestId(), operation);
        if (prior != null) return;
        view.showCountdown(label, seconds, () -> cancelLocal(message.requestId(), operation));
        operation.future = scheduler.schedule(() -> execute(message.requestId(), operation), seconds, TimeUnit.SECONDS);
        sendResult(channel, message.requestId(), command, 1,
                label + " scheduled in " + seconds + " seconds; Client can cancel before it executes.", "");
    }

    private void execute(String requestId, Pending operation) {
        if (!pending.remove(requestId, operation) || !operation.channel.isConnected()) return;
        try {
            if (operation.command == 2) commands.shutdown(); else commands.restart();
            sendResult(operation.channel, requestId, operation.command, 3, "Command executed.", "");
            view.clearCountdown(operation.command == 2 ? "Shutdown initiated." : "Restart initiated.");
        } catch (Exception error) {
            sendResult(operation.channel, requestId, operation.command, 4, "", safeMessage(error));
            view.clearCountdown("Remote operation failed: " + safeMessage(error));
        }
    }

    private void cancel(ClientControlChannel channel, MessageEnvelope message) {
        Pending operation = pending.get(message.requestId());
        if (operation != null && operation.channel == channel) cancelLocal(message.requestId(), operation);
    }

    private void cancelLocal(String id, Pending operation) {
        if (!pending.remove(id, operation)) return;
        ScheduledFuture<?> future = operation.future;
        if (future != null) future.cancel(false);
        view.clearCountdown("Remote operation canceled.");
        sendResult(operation.channel, id, operation.command, 2, "Canceled before execution.", "");
    }

    private void sendResult(ClientControlChannel channel, String requestId, int command, int status,
                            String result, String error) {
        try {
            channel.send(MessageType.REMOTE_ADMINISTRATION_RESULT, requestId, Map.of(
                    "command", command, "status", status, "clientId", clientId,
                    "result", result, "errorMessage", error, "timestampUtc", java.time.Instant.now().toString()));
        } catch (IOException ignored) { channel.close(); }
    }

    private static String safeMessage(Exception error) {
        return error.getMessage() == null ? error.getClass().getSimpleName() : error.getMessage();
    }

    private static final class Pending {
        private final ClientControlChannel channel;
        private final int command;
        private volatile ScheduledFuture<?> future;
        private Pending(ClientControlChannel channel, int command) { this.channel = channel; this.command = command; }
    }
}
