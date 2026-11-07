package vn.networkadmin.screenshare.domain;

import java.nio.IntBuffer;
import java.util.Objects;

/** Immutable snapshot of one display frame in ARGB format. */
public final class CapturedFrame {
    private final int width;
    private final int height;
    private final int[] argb;

    public CapturedFrame(int width, int height, int[] argb) {
        this(width, height, argb, false);
    }

    /** Transfers ownership of pixels to the frame to avoid another full-screen copy. */
    public static CapturedFrame takeOwnership(int width, int height, int[] argb) {
        return new CapturedFrame(width, height, argb, true);
    }

    private CapturedFrame(int width, int height, int[] argb, boolean takeOwnership) {
        if (width <= 0 || height <= 0 || (long) width * height > Integer.MAX_VALUE) {
            throw new IllegalArgumentException("Frame dimensions are invalid.");
        }
        Objects.requireNonNull(argb, "argb");
        if (argb.length != width * height) {
            throw new IllegalArgumentException("Pixel count does not match frame dimensions.");
        }
        this.width = width;
        this.height = height;
        this.argb = takeOwnership ? argb : argb.clone();
    }

    public int width() { return width; }
    public int height() { return height; }

    /** Read-only view avoids copying the full pixel array during frame comparison. */
    public IntBuffer pixels() {
        return IntBuffer.wrap(argb).asReadOnlyBuffer();
    }
}
