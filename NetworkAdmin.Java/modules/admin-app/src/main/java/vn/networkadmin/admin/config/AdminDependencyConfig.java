package vn.networkadmin.admin.config;

import vn.networkadmin.admin.runtime.AdminControlRuntime;
import vn.networkadmin.admin.application.AdminFeatureMessages;
import vn.networkadmin.admin.application.chat.AdminChatService;
import vn.networkadmin.admin.application.chat.AdminChatUseCases;
import vn.networkadmin.admin.application.remotecontrol.AdminRemoteControlService;
import vn.networkadmin.admin.application.remotecontrol.AdminRemoteControlUseCases;
import vn.networkadmin.admin.presentation.chat.AdminChatPresenter;
import vn.networkadmin.admin.presentation.dashboard.AdminDashboardPresenter;
import vn.networkadmin.admin.application.screenshare.AdminScreenShareCoordinator;
import vn.networkadmin.admin.application.screenshare.AdminScreenShareUseCases;
import vn.networkadmin.admin.application.screenshare.ScreenFrameDataEvents;
import vn.networkadmin.admin.infrastructure.screenshare.ScreenFrameDataServer;
import vn.networkadmin.admin.infrastructure.screenshare.LocalScreenFrameDataEvents;
import vn.networkadmin.screenshare.server.application.ApplyScreenFrame;
import vn.networkadmin.screenshare.server.infrastructure.ImageIoJpegDecoder;
import vn.networkadmin.transport.application.ControlServerOptions;
import vn.networkadmin.transport.infrastructure.ClientSessionRegistry;
import vn.networkadmin.transport.infrastructure.TcpControlServer;

import javax.net.ssl.SSLContext;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;

/** Admin composition root: wires the control adapter, session store, use cases and presenter. */
public final class AdminDependencyConfig implements AutoCloseable {
    private final ExecutorService presentationWorkers = Executors.newFixedThreadPool(4, task -> {
        Thread thread = new Thread(task, "admin-presentation-worker"); thread.setDaemon(true); return thread;
    });
    private final ExecutorService connectionWorkers = Executors.newVirtualThreadPerTaskExecutor();
    private final ScheduledExecutorService heartbeatMonitor = Executors.newSingleThreadScheduledExecutor(task -> {
        Thread thread = new Thread(task, "admin-heartbeat-monitor"); thread.setDaemon(true); return thread;
    });
    private final ScheduledExecutorService screenShareScheduler = Executors.newSingleThreadScheduledExecutor(task -> {
        Thread thread = new Thread(task, "admin-screen-share-timeouts"); thread.setDaemon(true); return thread;
    });
    private final ClientSessionRegistry sessions;
    private final TcpControlServer controlServer;
    private final AdminControlRuntime runtime;
    private final AdminDashboardPresenter presenter;
    private final AdminScreenShareCoordinator screenShare;
    private final AdminRemoteControlService remoteControl;
    private final AdminChatService chat;
    private final AdminChatPresenter chatPresenter;

    public AdminDependencyConfig(SSLContext tlsContext, ControlServerOptions options,
                                 String screenHost, int screenPort) throws java.io.IOException {
        this.sessions = new ClientSessionRegistry();
        this.controlServer = new TcpControlServer(tlsContext, options, sessions, connectionWorkers, heartbeatMonitor);
        ScreenFrameDataEvents dataEvents = new LocalScreenFrameDataEvents();
        var dataServer = new ScreenFrameDataServer(tlsContext, options.bindAddress(), screenPort, dataEvents);
        int activeScreenPort = dataServer.start();
        this.screenShare = new AdminScreenShareCoordinator(sessions, controlServer, dataServer,
                new ApplyScreenFrame(new ImageIoJpegDecoder()), screenShareScheduler, screenHost, activeScreenPort,
                java.time.Duration.ofSeconds(30), dataEvents);
        this.remoteControl = new AdminRemoteControlService(sessions, controlServer, screenShareScheduler);
        this.chat = new AdminChatService(sessions, controlServer);
        this.runtime = new AdminControlRuntime(controlServer, sessions, controlServer, screenShare,
                new AdminFeatureMessages(remoteControl, chat));
        this.presenter = new AdminDashboardPresenter(runtime, presentationWorkers);
        this.chatPresenter = new AdminChatPresenter(chat, presentationWorkers);
    }

    public AdminControlRuntime runtime() { return runtime; }
    public AdminDashboardPresenter presenter() { return presenter; }
    public AdminScreenShareUseCases screenShare() { return screenShare; }
    public AdminRemoteControlUseCases remoteControl() { return remoteControl; }
    public AdminChatUseCases chat() { return chat; }
    public AdminChatPresenter chatPresenter() { return chatPresenter; }

    @Override public void close() {
        screenShare.close();
        remoteControl.close();
        presenter.close();
        presentationWorkers.shutdownNow();
        screenShareScheduler.shutdownNow();
    }
}
