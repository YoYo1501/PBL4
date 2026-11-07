package vn.networkadmin.screenshare.server.domain;

import java.nio.IntBuffer;
import java.util.Objects;

/** Immutable complete desktop image used by the viewer after applying deltas. */
public final class ScreenCanvas {
    private final int width;
    private final int height;
    private final int[] argb;

    public ScreenCanvas(int width, int height, int[] argb) {
        if (width <= 0 || height <= 0 || (long) width * height > Integer.MAX_VALUE) {
            throw new IllegalArgumentException("Canvas dimensions are invalid.");
        }
        Objects.requireNonNull(argb, "argb");
        if (argb.length != width * height) throw new IllegalArgumentException("Canvas pixel count is invalid.");
        this.width = width;
        this.height = height;
        this.argb = argb.clone();
    }

    public int width() { return width; }
    public int height() { return height; }
    public IntBuffer pixels() { return IntBuffer.wrap(argb).asReadOnlyBuffer(); }

    public ScreenCanvas withPatch(int x, int y, DecodedJpeg patch) {
        Objects.requireNonNull(patch, "patch");
        if (x < 0 || y < 0 || x + patch.width() > width || y + patch.height() > height) {
            throw new IllegalArgumentException("Screen delta is outside the current canvas.");
        }

        int[] updated = argb.clone();
        IntBuffer patchPixels = patch.pixels();
        for (int row = 0; row < patch.height(); row++) {
            int source = row * patch.width();
            int target = (y + row) * width + x;
            for (int column = 0; column < patch.width(); column++) {
                updated[target + column] = patchPixels.get(source + column);
            }
        }
        return new ScreenCanvas(width, height, updated, true);
    }

    private ScreenCanvas(int width, int height, int[] argb, boolean takeOwnership) {
        this.width = width;
        this.height = height;
        this.argb = takeOwnership ? argb : argb.clone();
    }
}
