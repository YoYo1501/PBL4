package vn.networkadmin.client.presentation.screenshare;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.Assumptions;
import vn.networkadmin.client.infrastructure.monitoring.ClientTelemetrySampler;

import javax.swing.JLabel;
import javax.swing.SwingUtilities;
import java.awt.GraphicsEnvironment;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.time.Instant;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import static org.junit.jupiter.api.Assertions.assertEquals;

class ClientScreenShareWindowEdtTest {
    @Test
    void telemetryInvocationFromWorkerPublishesUiOnEdt() throws Exception {
        Assumptions.assumeTrue(GraphicsEnvironment.isHeadless());
        var window = new ClientScreenShareWindow();
        var telemetry = new ClientTelemetrySampler.TelemetrySnapshot(
                10, 20, 30, 12.5, 3.25, 1_000, 500,
                "machine", "os", "cpu", "screen", "ip", "mac", Instant.now());
        Method apply = ClientScreenShareWindow.class.getDeclaredMethod(
                "applyTelemetry", ClientTelemetrySampler.TelemetrySnapshot.class);
        apply.setAccessible(true);
        ExecutorService worker = Executors.newSingleThreadExecutor();
        try {
            worker.submit(() -> {
                try { apply.invoke(window, telemetry); }
                catch (ReflectiveOperationException error) { throw new AssertionError(error); }
            }).get();
            SwingUtilities.invokeAndWait(() -> { });

            Field field = ClientScreenShareWindow.class.getDeclaredField("downloadValue");
            field.setAccessible(true);
            assertEquals("12.50 Mbps", ((JLabel) field.get(window)).getText());
        } finally {
            worker.shutdownNow();
            window.close();
        }
    }
}
