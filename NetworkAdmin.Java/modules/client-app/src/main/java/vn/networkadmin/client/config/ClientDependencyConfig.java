package vn.networkadmin.client.config;

import vn.networkadmin.client.runtime.ClientAgentRuntime;
import vn.networkadmin.client.application.screenshare.ClientScreenShareService;
import vn.networkadmin.client.application.screenshare.ScreenShareClientView;
import vn.networkadmin.client.application.ClientFeatureMessages;
import vn.networkadmin.client.application.chat.ClientChatService;
import vn.networkadmin.client.application.chat.ClientChatUseCases;
import vn.networkadmin.client.application.remotecontrol.ClientRemoteControlService;
import vn.networkadmin.client.application.remotecontrol.RemoteControlClientView;
import vn.networkadmin.client.infrastructure.remotecontrol.AwtRemoteInputAdapter;
import vn.networkadmin.client.infrastructure.screenshare.PinnedTlsScreenFrameSenderFactory;
import vn.networkadmin.network.application.NetworkScanner;
import vn.networkadmin.network.infrastructure.JavaPingAdapter;
import vn.networkadmin.network.infrastructure.SystemNetworkInventoryAdapter;
import vn.networkadmin.transport.application.ClientConnectionSupervisor;
import vn.networkadmin.transport.infrastructure.ClientControlConnection;
import vn.networkadmin.transport.infrastructure.JvmSystemStatsSampler;
import vn.networkadmin.screenshare.application.EncodeScreenFrame;
import vn.networkadmin.screenshare.infrastructure.AwtScreenCaptureAdapter;
import vn.networkadmin.screenshare.infrastructure.AtomicLatestFrameStore;
import vn.networkadmin.screenshare.infrastructure.ImageIoJpegEncoder;

import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;

/** Client composition root: creates infrastructure adapters and injects application dependencies. */
public final class ClientDependencyConfig implements AutoCloseable {
    private final ExecutorService scanWorkers = Executors.newFixedThreadPool(32, task -> {
        Thread thread = new Thread(task, "client-network-scan"); thread.setDaemon(true); return thread;
    });
    private final ExecutorService commandWorkers = Executors.newVirtualThreadPerTaskExecutor();
    private final ScheduledExecutorService heartbeatScheduler = Executors.newSingleThreadScheduledExecutor(task -> {
        Thread thread = new Thread(task, "client-control-heartbeat"); thread.setDaemon(true); return thread;
    });
    private final ScheduledExecutorService publisherScheduler = Executors.newSingleThreadScheduledExecutor(task -> {
        Thread thread = new Thread(task, "client-network-info-publisher"); thread.setDaemon(true); return thread;
    });
    private final ScheduledExecutorService screenScheduler = Executors.newScheduledThreadPool(2, task -> {
        Thread thread = new Thread(task, "client-screen-share"); thread.setDaemon(true); return thread;
    });
    private final ExecutorService screenWorkers = Executors.newFixedThreadPool(2, task -> {
        Thread thread = new Thread(task, "client-screen-share-worker"); thread.setDaemon(true); return thread;
    });
    private final ClientAgentRuntime runtime;
    private final ClientScreenShareService screenShare;
    private final ClientRemoteControlService remoteControl;
    private final ClientChatService chat;
    private final ExecutorService inputWorkers;

    public ClientDependencyConfig(String host, int port, String fingerprint, String clientId, String clientName,
                                  ScreenShareClientView screenShareView, RemoteControlClientView remoteControlView,
                                  java.util.function.Consumer<ClientChatUseCases.ChatMessage> chatReceiver,
                                  java.util.function.Consumer<java.util.function.Consumer<String>> chatSenderSink) {
        var inventory = new SystemNetworkInventoryAdapter();
        var ping = new JavaPingAdapter();
        var scanner = new NetworkScanner(ping, inventory, scanWorkers);
        var connections = new ClientConnectionSupervisor(
                () -> new ClientControlConnection(host, port, fingerprint, clientId, clientName, List.of(), 10_000),
                new JvmSystemStatsSampler(), heartbeatScheduler);
        this.screenShare = new ClientScreenShareService(clientId, clientName, fingerprint, screenShareView,
                new AwtScreenCaptureAdapter(), new AtomicLatestFrameStore(),
                new EncodeScreenFrame(new ImageIoJpegEncoder(), 0.72f),
                new PinnedTlsScreenFrameSenderFactory(fingerprint), screenScheduler, screenWorkers);
        this.inputWorkers = new java.util.concurrent.ThreadPoolExecutor(1, 1, 0L, java.util.concurrent.TimeUnit.MILLISECONDS,
                new java.util.concurrent.ArrayBlockingQueue<>(256), task -> {
                    Thread thread = new Thread(task, "client-remote-input"); thread.setDaemon(true); return thread;
                }, new java.util.concurrent.ThreadPoolExecutor.AbortPolicy());
        this.remoteControl = new ClientRemoteControlService(clientId, remoteControlView,
                new AwtRemoteInputAdapter(), inputWorkers);
        this.chat = new ClientChatService(clientId, clientName, chatReceiver);
        chatSenderSink.accept(chat::send);
        this.runtime = new ClientAgentRuntime(clientId, clientName, inventory, ping, scanner,
                commandWorkers, publisherScheduler, connections, screenShare,
                new ClientFeatureMessages(remoteControl, chat));
    }

    public ClientAgentRuntime runtime() { return runtime; }

    @Override public void close() {
        runtime.close();
        screenShare.close();
        screenWorkers.shutdownNow();
        inputWorkers.shutdownNow();
        heartbeatScheduler.shutdownNow();
        publisherScheduler.shutdownNow();
        commandWorkers.shutdownNow();
        scanWorkers.shutdownNow();
    }

}
