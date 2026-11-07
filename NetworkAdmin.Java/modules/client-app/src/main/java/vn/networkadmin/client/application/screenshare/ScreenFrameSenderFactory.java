package vn.networkadmin.client.application.screenshare;

import java.io.IOException;

public interface ScreenFrameSenderFactory {
    ScreenFrameSender connect(String host, int port, String requestId, String clientId,
                              String sessionId, String token) throws IOException;
}
