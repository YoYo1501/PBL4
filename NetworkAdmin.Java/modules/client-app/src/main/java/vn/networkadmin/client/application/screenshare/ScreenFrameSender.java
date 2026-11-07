package vn.networkadmin.client.application.screenshare;

import vn.networkadmin.protocol.screenshare.ScreenFramePacket;
import java.io.IOException;

public interface ScreenFrameSender extends AutoCloseable {
    void send(ScreenFramePacket frame) throws IOException;
    @Override void close();
}
