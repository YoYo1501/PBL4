package vn.networkadmin.screenshare.infrastructure;

import java.awt.image.BufferedImage;
import java.awt.image.DataBufferInt;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.IntBuffer;
import java.util.Objects;
import javax.imageio.IIOImage;
import javax.imageio.ImageIO;
import javax.imageio.ImageWriteParam;
import javax.imageio.ImageWriter;
import javax.imageio.stream.MemoryCacheImageOutputStream;
import vn.networkadmin.screenshare.domain.CapturedFrame;
import vn.networkadmin.screenshare.ports.JpegEncoderPort;
import vn.networkadmin.protocol.screenshare.FrameRectangle;

public final class ImageIoJpegEncoder implements JpegEncoderPort {
    @Override
    public byte[] encode(CapturedFrame frame, FrameRectangle region, float quality) {
        Objects.requireNonNull(frame, "frame");
        Objects.requireNonNull(region, "region");
        if (region.x() + region.width() > frame.width() || region.y() + region.height() > frame.height()) {
            throw new IllegalArgumentException("JPEG region exceeds captured frame bounds.");
        }
        if (quality < 0.1f || quality > 1.0f) {
            throw new IllegalArgumentException("JPEG quality must be in the range 0.1 to 1.0.");
        }

        BufferedImage image = new BufferedImage(region.width(), region.height(), BufferedImage.TYPE_INT_RGB);
        int[] rgb = ((DataBufferInt) image.getRaster().getDataBuffer()).getData();
        IntBuffer pixels = frame.pixels();
        for (int row = 0; row < region.height(); row++) {
            int sourceOffset = (region.y() + row) * frame.width() + region.x();
            int targetOffset = row * region.width();
            for (int column = 0; column < region.width(); column++) {
                rgb[targetOffset + column] = pixels.get(sourceOffset + column) & 0x00FFFFFF;
            }
        }
        ImageWriter writer = ImageIO.getImageWritersByFormatName("jpeg").next();
        try (var bytes = new ByteArrayOutputStream();
             var output = new MemoryCacheImageOutputStream(bytes)) {
            writer.setOutput(output);
            ImageWriteParam parameters = writer.getDefaultWriteParam();
            parameters.setCompressionMode(ImageWriteParam.MODE_EXPLICIT);
            parameters.setCompressionQuality(quality);
            writer.write(null, new IIOImage(image, null, null), parameters);
            output.flush();
            return bytes.toByteArray();
        } catch (IOException exception) {
            throw new IllegalStateException("Unable to encode captured frame as JPEG.", exception);
        } finally {
            writer.dispose();
        }
    }
}
