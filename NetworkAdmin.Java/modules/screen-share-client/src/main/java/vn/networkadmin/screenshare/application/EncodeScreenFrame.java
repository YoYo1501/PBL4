package vn.networkadmin.screenshare.application;

import java.nio.IntBuffer;
import java.util.Objects;
import java.util.Optional;
import vn.networkadmin.screenshare.domain.CapturedFrame;
import vn.networkadmin.screenshare.ports.JpegEncoderPort;
import vn.networkadmin.protocol.screenshare.FrameRectangle;
import vn.networkadmin.protocol.screenshare.ScreenFramePacket;

/** Applies RemoteX's 16-pixel block comparison and full-frame threshold policy. */
public final class EncodeScreenFrame {
    private static final int BLOCK_SIZE = 16;
    private static final double FULL_FRAME_THRESHOLD = 0.35;

    private final JpegEncoderPort jpegEncoder;
    private final float jpegQuality;

    public EncodeScreenFrame(JpegEncoderPort jpegEncoder, float jpegQuality) {
        this.jpegEncoder = Objects.requireNonNull(jpegEncoder, "jpegEncoder");
        if (jpegQuality < 0.1f || jpegQuality > 1.0f) {
            throw new IllegalArgumentException("JPEG quality must be in the range 0.1 to 1.0.");
        }
        this.jpegQuality = jpegQuality;
    }

    /** Returns empty when the captured frame has no changed pixels. */
    public Optional<ScreenFramePacket> execute(CapturedFrame current, CapturedFrame previous, long sequence) {
        Objects.requireNonNull(current, "current");
        if (sequence < 0) throw new IllegalArgumentException("Sequence cannot be negative.");

        if (previous == null || previous.width() != current.width() || previous.height() != current.height()) {
            return Optional.of(fullFrame(current, sequence));
        }

        FrameRectangle changed = findChangedBounds(previous, current);
        if (changed == null) return Optional.empty();

        double changedAreaRatio = (double) changed.area() / ((long) current.width() * current.height());
        if (changedAreaRatio > FULL_FRAME_THRESHOLD) return Optional.of(fullFrame(current, sequence));

        return Optional.of(new ScreenFramePacket(sequence, false, changed.x(), changed.y(),
                changed.width(), changed.height(), jpegEncoder.encode(current, changed, jpegQuality)));
    }

    private ScreenFramePacket fullFrame(CapturedFrame frame, long sequence) {
        var bounds = new FrameRectangle(0, 0, frame.width(), frame.height());
        return new ScreenFramePacket(sequence, true, 0, 0, frame.width(), frame.height(),
                jpegEncoder.encode(frame, bounds, jpegQuality));
    }

    private static FrameRectangle findChangedBounds(CapturedFrame oldFrame, CapturedFrame newFrame) {
        IntBuffer oldPixels = oldFrame.pixels();
        IntBuffer newPixels = newFrame.pixels();
        int minX = newFrame.width();
        int minY = newFrame.height();
        int maxX = -1;
        int maxY = -1;

        for (int blockY = 0; blockY < newFrame.height(); blockY += BLOCK_SIZE) {
            int endY = Math.min(blockY + BLOCK_SIZE, newFrame.height());
            for (int blockX = 0; blockX < newFrame.width(); blockX += BLOCK_SIZE) {
                int endX = Math.min(blockX + BLOCK_SIZE, newFrame.width());
                if (!blockChanged(oldPixels, newPixels, newFrame.width(), blockX, blockY, endX, endY)) continue;
                minX = Math.min(minX, blockX);
                minY = Math.min(minY, blockY);
                maxX = Math.max(maxX, endX);
                maxY = Math.max(maxY, endY);
            }
        }

        return maxX < 0 ? null : new FrameRectangle(minX, minY, maxX - minX, maxY - minY);
    }

    private static boolean blockChanged(IntBuffer oldPixels, IntBuffer newPixels, int frameWidth,
                                        int x, int y, int endX, int endY) {
        for (int row = y; row < endY; row++) {
            int offset = row * frameWidth;
            for (int column = x; column < endX; column++) {
                int index = offset + column;
                if (oldPixels.get(index) != newPixels.get(index)) return true;
            }
        }
        return false;
    }
}
