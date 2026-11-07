package vn.networkadmin.client.application.remotecontrol;

import vn.networkadmin.protocol.control.ControlInputEvent;

/** Performs a previously validated input event on the local desktop. */
public interface RemoteInputPort {
    boolean available();
    void apply(ControlInputEvent event);
    void releaseAll();
}
