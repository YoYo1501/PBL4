package vn.networkadmin.screenshare.server.application;

import java.util.Objects;
import vn.networkadmin.protocol.screenshare.ScreenFramePacket;
import vn.networkadmin.screenshare.server.domain.DecodedJpeg;
import vn.networkadmin.screenshare.server.domain.ScreenCanvas;
import vn.networkadmin.screenshare.server.ports.JpegDecoderPort;

/** Decodes full and delta frames and maintains a complete viewer canvas. */
public final class ApplyScreenFrame {
    private final JpegDecoderPort decoder;

    public ApplyScreenFrame(JpegDecoderPort decoder) {
        this.decoder = Objects.requireNonNull(decoder, "decoder");
    }

    public ScreenCanvas execute(ScreenFramePacket packet, ScreenCanvas current) {
        Objects.requireNonNull(packet, "packet");
        DecodedJpeg decoded = decoder.decode(packet.jpeg());
        if (decoded.width() != packet.width() || decoded.height() != packet.height()) {
            throw new IllegalArgumentException("Decoded JPEG dimensions do not match frame metadata.");
        }

        if (packet.fullFrame()) {
            if (packet.x() != 0 || packet.y() != 0) {
                throw new IllegalArgumentException("Full frame must start at the display origin.");
            }
            return new ScreenCanvas(decoded.width(), decoded.height(), toArray(decoded));
        }

        if (current == null) throw new IllegalStateException("Received a delta frame before the initial full frame.");
        return current.withPatch(packet.x(), packet.y(), decoded);
    }

    private static int[] toArray(DecodedJpeg image) {
        int[] pixels = new int[image.width() * image.height()];
        image.pixels().get(pixels);
        return pixels;
    }
}
