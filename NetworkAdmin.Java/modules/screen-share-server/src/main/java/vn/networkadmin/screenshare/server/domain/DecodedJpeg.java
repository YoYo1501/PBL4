package vn.networkadmin.screenshare.server.domain;

import java.nio.IntBuffer;
import java.util.Objects;

/** Decoded JPEG pixels in ARGB format. */
public final class DecodedJpeg {
    private final int width;
    private final int height;
    private final int[] argb;

    public DecodedJpeg(int width, int height, int[] argb) {
        if (width <= 0 || height <= 0 || (long) width * height > Integer.MAX_VALUE) {
            throw new IllegalArgumentException("Decoded image dimensions are invalid.");
        }
        Objects.requireNonNull(argb, "argb");
        if (argb.length != width * height) throw new IllegalArgumentException("Decoded pixel count is invalid.");
        this.width = width;
        this.height = height;
        this.argb = argb.clone();
    }

    public int width() { return width; }
    public int height() { return height; }
    public IntBuffer pixels() { return IntBuffer.wrap(argb).asReadOnlyBuffer(); }
}
