package vn.networkadmin.client.infrastructure.remoteadministration;

import vn.networkadmin.client.application.remoteadministration.RemoteAdministrationPort;

import java.io.IOException;
import java.util.Locale;
import java.util.concurrent.TimeUnit;

/** Windows workstation commands. It runs in the logged-on Client process/session. */
public final class WindowsRemoteAdministrationAdapter implements RemoteAdministrationPort {
    @Override public void lockScreen() throws Exception {
        requireWindows();
        runAndCheck("rundll32.exe", "user32.dll,LockWorkStation");
    }

    @Override public void shutdown() throws Exception {
        requireWindows();
        runAndCheck("shutdown.exe", "/s", "/t", "0", "/c", "Shutdown initiated by the authorized NetworkAdmin server.");
    }

    @Override public void restart() throws Exception {
        requireWindows();
        runAndCheck("shutdown.exe", "/r", "/t", "0", "/c", "Restart initiated by the authorized NetworkAdmin server.");
    }

    private static void runAndCheck(String... command) throws IOException, InterruptedException {
        Process process = new ProcessBuilder(command).redirectErrorStream(true).start();
        if (!process.waitFor(5, TimeUnit.SECONDS)) {
            process.destroyForcibly();
            throw new IOException("Windows did not confirm the operation within five seconds.");
        }
        if (process.exitValue() != 0) {
            String output = new String(process.getInputStream().readAllBytes(), java.nio.charset.StandardCharsets.UTF_8).trim();
            throw new IOException(output.isBlank() ? "Windows rejected the operation. Check the Client account's local policy." : output);
        }
    }

    private static void requireWindows() {
        if (!System.getProperty("os.name", "").toLowerCase(Locale.ROOT).contains("windows"))
            throw new UnsupportedOperationException("Remote lock, shutdown, and restart are currently supported on Windows Clients only.");
    }
}
