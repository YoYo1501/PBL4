package vn.networkadmin.admin.application.remotecontrol;

import vn.networkadmin.protocol.control.ControlInputEvent;

public interface AdminRemoteControlUseCases extends AutoCloseable {
    void requestControl(String clientId, String sessionId) throws Exception;
    void stopControl(String sessionId);
    void sendInput(String sessionId, ControlInputEvent event) throws Exception;
    ControlState state(String sessionId);
    @Override void close();

    record ControlState(String requestId, String status, String detail) { }
}
