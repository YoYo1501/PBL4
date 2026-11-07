package vn.networkadmin.protocol.screenshare;

/** Screen region in pixels; right and bottom edges are exclusive. */
public record FrameRectangle(int x, int y, int width, int height) {
    public FrameRectangle {
        if (x < 0 || y < 0 || width <= 0 || height <= 0) {
            throw new IllegalArgumentException("Frame rectangle is invalid.");
        }
    }

    public long area() {
        return (long) width * height;
    }
}
