package vn.networkadmin.protocol.screenshare;

import java.io.DataInputStream;
import java.io.DataOutputStream;
import java.io.EOFException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/** Implements the same big-endian binary frame format as NetworkAdmin.Shared. */
public final class ScreenFrameProtocol {
    public static final int MAX_FRAME_BYTES = 12 * 1024 * 1024;
    public static final int MAX_SCREEN_DIMENSION = 7680;
    public static final int HEADER_BYTES = Integer.BYTES + Long.BYTES + 1 + Integer.BYTES * 4;

    private ScreenFrameProtocol() { }

    public static void writeFrame(OutputStream stream, ScreenFramePacket frame) throws IOException {
        byte[] jpeg = frame.jpeg();
        validate(frame, jpeg.length);
        if (!isJpeg(jpeg)) throw new IOException("Frame payload is not a complete JPEG image.");

        var output = new DataOutputStream(stream);
        output.writeInt(jpeg.length);
        output.writeLong(frame.sequence());
        output.writeByte(frame.fullFrame() ? 1 : 0);
        output.writeInt(frame.x());
        output.writeInt(frame.y());
        output.writeInt(frame.width());
        output.writeInt(frame.height());
        output.write(jpeg);
        output.flush();
    }

    /** Returns null only when the stream ends cleanly between frame headers. */
    public static ScreenFramePacket readFrame(InputStream stream) throws IOException {
        var input = new DataInputStream(stream);
        int first = input.read();
        if (first < 0) return null;

        int length = (first << 24) | (input.readUnsignedByte() << 16)
                | (input.readUnsignedByte() << 8) | input.readUnsignedByte();
        long sequence = input.readLong();
        int kind = input.readUnsignedByte();
        int x = input.readInt();
        int y = input.readInt();
        int width = input.readInt();
        int height = input.readInt();
        if (length <= 0 || length > MAX_FRAME_BYTES || sequence < 0 || kind > 1
                || x < 0 || y < 0 || width <= 0 || height <= 0
                || width > MAX_SCREEN_DIMENSION || height > MAX_SCREEN_DIMENSION
                || (kind == 1 && (x != 0 || y != 0))) {
            throw new IOException("Screen frame header is invalid.");
        }

        byte[] jpeg = new byte[length];
        try {
            input.readFully(jpeg);
        } catch (EOFException exception) {
            throw new EOFException("Connection ended in the middle of a screen frame.");
        }
        if (!isJpeg(jpeg)) throw new IOException("Frame payload is not a complete JPEG image.");
        return new ScreenFramePacket(sequence, kind == 1, x, y, width, height, jpeg);
    }

    private static void validate(ScreenFramePacket frame, int length) throws IOException {
        if (length <= 0 || length > MAX_FRAME_BYTES || frame.sequence() < 0
                || frame.x() < 0 || frame.y() < 0 || frame.width() <= 0 || frame.height() <= 0
                || frame.width() > MAX_SCREEN_DIMENSION || frame.height() > MAX_SCREEN_DIMENSION
                || (frame.fullFrame() && (frame.x() != 0 || frame.y() != 0))) {
            throw new IOException("Screen frame is invalid.");
        }
    }

    private static boolean isJpeg(byte[] jpeg) {
        return jpeg.length >= 4 && (jpeg[0] & 0xFF) == 0xFF && (jpeg[1] & 0xFF) == 0xD8
                && (jpeg[jpeg.length - 2] & 0xFF) == 0xFF && (jpeg[jpeg.length - 1] & 0xFF) == 0xD9;
    }
}
