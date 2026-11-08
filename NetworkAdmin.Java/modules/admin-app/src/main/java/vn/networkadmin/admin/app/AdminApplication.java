package vn.networkadmin.admin.app;

import vn.networkadmin.admin.config.AdminDependencyConfig;
import vn.networkadmin.admin.presentation.dashboard.AdminDashboardFrame;
import vn.networkadmin.admin.runtime.AdminControlRuntime;
import vn.networkadmin.transport.application.ControlServerOptions;
import vn.networkadmin.transport.tls.Pkcs12ServerTlsContext;

import javax.net.ssl.SSLContext;
import javax.swing.JOptionPane;
import javax.swing.SwingUtilities;
import java.awt.GraphicsEnvironment;
import java.net.InetAddress;
import java.nio.file.Path;
import java.time.Duration;
import java.util.Arrays;

/** Composition root for the Java Admin desktop application. */
public final class AdminApplication {
    private AdminApplication() { }

    public static void main(String[] args) {
        try {
            Configuration config = Configuration.from(args);
            char[] password = config.keyStorePassword().toCharArray();
            SSLContext tls;
            try { tls = Pkcs12ServerTlsContext.load(config.keyStorePath(), password); }
            finally { Arrays.fill(password, '\0'); }

            ControlServerOptions options = new ControlServerOptions(
                    InetAddress.getByName(config.host()), config.port(), config.maxConnections(),
                    Duration.ofSeconds(10), Duration.ofSeconds(config.heartbeatTimeoutSeconds()));
            String screenHost = config.screenHost() == null || config.screenHost().isBlank()
                    ? (config.host().equals("0.0.0.0") ? InetAddress.getLocalHost().getHostAddress() : config.host())
                    : config.screenHost();
            AdminDependencyConfig dependencies = new AdminDependencyConfig(tls, options, screenHost, config.screenPort());
            AdminControlRuntime runtime = dependencies.runtime();
            String displayHost = config.host().equals("0.0.0.0") ? "All network interfaces" : config.host();
            String address = displayHost + ": " + config.port() + " | TLS";

            Thread listener = new Thread(() -> {
                try { runtime.run(); }
                catch (Exception error) {
                    System.getLogger(AdminApplication.class.getName()).log(System.Logger.Level.ERROR,
                            "Admin control server stopped.", error);
                    dependencies.close();
                    if (!GraphicsEnvironment.isHeadless()) SwingUtilities.invokeLater(() ->
                            JOptionPane.showMessageDialog(null, "Control server stopped: " + rootMessage(error),
                                    "Network Administration", JOptionPane.ERROR_MESSAGE));
                }
            }, "admin-control-listener");
            listener.setDaemon(true);
            listener.start();

            SwingUtilities.invokeLater(() -> {
                AdminDashboardFrame frame = new AdminDashboardFrame(dependencies.presenter(), dependencies.screenShare(),
                        dependencies.remoteControl(), dependencies.chatPresenter(), address,
                        System.getenv("NETWORKADMIN_TLS_SERVER_SHA256"), dependencies::close);
                frame.setVisible(true);
            });
        } catch (Exception error) {
            String message = rootMessage(error);
            System.err.println("Could not start Network Administration Admin: " + message);
            if (!GraphicsEnvironment.isHeadless()) SwingUtilities.invokeLater(() ->
                    JOptionPane.showMessageDialog(null, message, "Network Administration startup error",
                            JOptionPane.ERROR_MESSAGE));
            System.exit(1);
        }
    }

    private static String rootMessage(Throwable error) {
        Throwable current = error;
        while (current.getCause() != null) current = current.getCause();
        return current.getMessage() == null ? current.getClass().getSimpleName() : current.getMessage();
    }

    private record Configuration(String host, int port, int maxConnections, int heartbeatTimeoutSeconds,
                                 int screenPort, String screenHost, Path keyStorePath, String keyStorePassword) {
        private static Configuration from(String[] args) {
            String host = setting(args, "host", "NETWORKADMIN_SERVER_HOST", "0.0.0.0");
            int port = integerSetting(args, "port", "NETWORKADMIN_SERVER_PORT", 5000);
            int maxConnections = integerSetting(args, "max-connections", "NETWORKADMIN_MAX_CONNECTIONS", 128);
            int heartbeat = integerSetting(args, "heartbeat-timeout", "NETWORKADMIN_HEARTBEAT_TIMEOUT_SECONDS", 30);
            int screenPort = integerSetting(args, "screen-port", "NETWORKADMIN_SCREEN_PORT", 5001);
            String screenHost = setting(args, "screen-host", "NETWORKADMIN_SCREEN_HOST", null);
            String path = setting(args, "keystore", "NETWORKADMIN_TLS_KEYSTORE_PATH", null);
            String password = System.getenv("NETWORKADMIN_TLS_KEYSTORE_PASSWORD");
            if (path == null || path.isBlank()) throw new IllegalArgumentException(
                    "Set NETWORKADMIN_TLS_KEYSTORE_PATH to a PKCS#12 keystore file.");
            if (password == null || password.isBlank()) throw new IllegalArgumentException(
                    "Set NETWORKADMIN_TLS_KEYSTORE_PASSWORD in the environment.");
            if (port < 1 || port > 65535) throw new IllegalArgumentException("Port must be between 1 and 65535.");
            if (heartbeat < 10 || heartbeat > 300) throw new IllegalArgumentException("Heartbeat timeout must be between 10 and 300 seconds.");
            if (screenPort < 1 || screenPort > 65535 || screenPort == port)
                throw new IllegalArgumentException("Screen data port must be a valid port different from the control port.");
            return new Configuration(host, port, maxConnections, heartbeat, screenPort, screenHost, Path.of(path), password);
        }

        private static String setting(String[] args, String name, String environment, String fallback) {
            String argument = "--" + name + "=";
            for (String value : args) if (value.startsWith(argument)) return value.substring(argument.length());
            String configured = System.getenv(environment);
            return configured == null || configured.isBlank() ? fallback : configured;
        }

        private static int integerSetting(String[] args, String name, String environment, int fallback) {
            String value = setting(args, name, environment, Integer.toString(fallback));
            try { return Integer.parseInt(value); }
            catch (NumberFormatException error) { throw new IllegalArgumentException("Invalid integer for --" + name + ": " + value); }
        }
    }
}
