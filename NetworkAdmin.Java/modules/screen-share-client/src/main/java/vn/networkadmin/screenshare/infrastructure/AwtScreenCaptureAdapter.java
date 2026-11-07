package vn.networkadmin.screenshare.infrastructure;

import java.awt.GraphicsEnvironment;
import java.awt.Rectangle;
import java.awt.Robot;
import java.awt.Toolkit;
import java.awt.image.BufferedImage;
import vn.networkadmin.screenshare.domain.CapturedFrame;
import vn.networkadmin.screenshare.ports.ScreenCapturePort;

/** Windows desktop capture adapter based on the same AWT Robot API used by RemoteX. */
public final class AwtScreenCaptureAdapter implements ScreenCapturePort {
    @Override
    public CapturedFrame capture() {
        if (GraphicsEnvironment.isHeadless()) {
            throw new IllegalStateException("Screen capture requires a graphical desktop session.");
        }

        Rectangle bounds = new Rectangle(Toolkit.getDefaultToolkit().getScreenSize());
        if (bounds.width <= 0 || bounds.height <= 0) {
            throw new IllegalStateException("Primary display dimensions are invalid.");
        }

        try {
            BufferedImage image = new Robot().createScreenCapture(bounds);
            int[] pixels = image.getRGB(0, 0, image.getWidth(), image.getHeight(), null, 0, image.getWidth());
            return CapturedFrame.takeOwnership(image.getWidth(), image.getHeight(), pixels);
        } catch (java.awt.AWTException exception) {
            throw new IllegalStateException("Unable to initialize desktop capture.", exception);
        }
    }
}
