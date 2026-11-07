package vn.networkadmin.screenshare.server.infrastructure;

import java.awt.image.BufferedImage;
import java.io.ByteArrayInputStream;
import java.io.IOException;
import javax.imageio.ImageIO;
import vn.networkadmin.screenshare.server.domain.DecodedJpeg;
import vn.networkadmin.screenshare.server.ports.JpegDecoderPort;

public final class ImageIoJpegDecoder implements JpegDecoderPort {
    public static final int MAX_WIDTH = 3840;
    public static final int MAX_HEIGHT = 2160;
    private static final long MAX_PIXELS = (long) MAX_WIDTH * MAX_HEIGHT;

    @Override
    public DecodedJpeg decode(byte[] jpeg) {
        try {
            BufferedImage image = ImageIO.read(new ByteArrayInputStream(jpeg));
            if (image == null) throw new IllegalArgumentException("Frame payload is not a supported JPEG image.");
            if (image.getWidth() <= 0 || image.getHeight() <= 0 || image.getWidth() > MAX_WIDTH
                    || image.getHeight() > MAX_HEIGHT || (long) image.getWidth() * image.getHeight() > MAX_PIXELS) {
                throw new IllegalArgumentException("Decoded frame exceeds the viewer's resolution limit.");
            }
            int[] pixels = image.getRGB(0, 0, image.getWidth(), image.getHeight(), null, 0, image.getWidth());
            return new DecodedJpeg(image.getWidth(), image.getHeight(), pixels);
        } catch (IOException exception) {
            throw new IllegalArgumentException("Unable to decode screen JPEG frame.", exception);
        }
    }
}
