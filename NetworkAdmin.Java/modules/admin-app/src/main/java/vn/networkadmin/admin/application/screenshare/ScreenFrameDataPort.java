package vn.networkadmin.admin.application.screenshare;

import java.io.IOException;

/** Admin-owned boundary for the authenticated binary screen channel. */
public interface ScreenFrameDataPort extends AutoCloseable {
    int start() throws IOException;
    int port();
    void authorize(String requestId, String clientId, String sessionId, String token);
    void revoke(String requestId);
    @Override void close();
    record Identity(String requestId, String clientId, String sessionId) { }
}
