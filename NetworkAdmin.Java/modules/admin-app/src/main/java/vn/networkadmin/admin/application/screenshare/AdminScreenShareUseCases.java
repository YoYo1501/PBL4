package vn.networkadmin.admin.application.screenshare;

import vn.networkadmin.screenshare.server.domain.ScreenCanvas;

/** Admin-facing screen-share actions and read-only session state. */
public interface AdminScreenShareUseCases extends AutoCloseable {
    void requestViewing(String clientId, String sessionId) throws Exception;
    void stopViewing(String sessionId);
    ScreenShareSnapshot snapshot(String sessionId);
    @Override void close();

    record ScreenShareSnapshot(String requestId, String status, String detail, ScreenCanvas canvas) { }
}
