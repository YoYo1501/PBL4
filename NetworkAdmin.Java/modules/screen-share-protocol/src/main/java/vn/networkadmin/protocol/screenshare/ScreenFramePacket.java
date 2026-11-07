package vn.networkadmin.protocol.screenshare;

import java.util.Objects;

/** Binary screen-frame message shared by the Java applications and C# migration reference. */
public record ScreenFramePacket(long sequence, boolean fullFrame, int x, int y,
                                int width, int height, byte[] jpeg) {
    public ScreenFramePacket {
        if (sequence < 0 || x < 0 || y < 0 || width <= 0 || height <= 0) {
            throw new IllegalArgumentException("Screen frame metadata is invalid.");
        }
        if (fullFrame && (x != 0 || y != 0)) {
            throw new IllegalArgumentException("A full frame must start at the display origin.");
        }
        Objects.requireNonNull(jpeg, "jpeg");
        if (jpeg.length == 0) {
            throw new IllegalArgumentException("JPEG data cannot be empty.");
        }
        jpeg = jpeg.clone();
    }

    @Override
    public byte[] jpeg() {
        return jpeg.clone();
    }
}
