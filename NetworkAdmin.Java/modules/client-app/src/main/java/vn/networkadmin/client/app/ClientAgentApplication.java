package vn.networkadmin.client.app;

import vn.networkadmin.client.config.ClientDependencyConfig;
import vn.networkadmin.client.runtime.ClientAgentRuntime;
import vn.networkadmin.client.presentation.screenshare.ClientScreenShareWindow;

import java.net.InetAddress;
import java.util.concurrent.CountDownLatch;

/** Client process entry point; dependency construction is delegated to ClientDependencyConfig. */
public final class ClientAgentApplication {
    private ClientAgentApplication() { }

    public static void main(String[] args) throws InterruptedException {
        String host = setting(args, "host", "NETWORKADMIN_SERVER_HOST", "127.0.0.1");
        int port;
        try { port = Integer.parseInt(setting(args, "port", "NETWORKADMIN_SERVER_PORT", "5000")); }
        catch (NumberFormatException error) { throw new IllegalArgumentException("Server port must be a number.", error); }
        String fingerprint = setting(args, "fingerprint", "NETWORKADMIN_TLS_SERVER_SHA256", null);
        if (fingerprint == null || fingerprint.isBlank())
            throw new IllegalArgumentException("Set NETWORKADMIN_TLS_SERVER_SHA256 to the Admin certificate SHA-256 fingerprint.");
        String clientName = hostname();
        ClientScreenShareWindow screenShareWindow = new ClientScreenShareWindow(host, port);
        ClientDependencyConfig dependencies = new ClientDependencyConfig(host, port, fingerprint,
                ClientAgentRuntime.localClientId(), clientName, screenShareWindow, screenShareWindow, screenShareWindow,
                screenShareWindow::chatReceived, screenShareWindow::setChatSender);
        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            dependencies.close();
            screenShareWindow.close();
        }, "network-admin-client-shutdown"));
        dependencies.runtime().start();
        System.out.printf("NetworkAdmin Client started; connecting to %s:%d. Press Ctrl+C to stop.%n", host, port);
        new CountDownLatch(1).await();
    }

    private static String setting(String[] args, String name, String environment, String fallback) {
        String prefix = "--" + name + "=";
        for (String argument : args) if (argument.startsWith(prefix)) return argument.substring(prefix.length());
        String value = System.getenv(environment);
        return value == null || value.isBlank() ? fallback : value;
    }

    private static String hostname() {
        try { return InetAddress.getLocalHost().getHostName(); }
        catch (Exception error) { return System.getenv().getOrDefault("COMPUTERNAME", "NetworkAdmin Client"); }
    }
}
