package vn.networkadmin.client.application.remotecontrol;

import java.util.concurrent.CompletionStage;

public interface RemoteControlClientView {
    CompletionStage<Boolean> requestControlConsent(String adminName);
    void controlStarted(Runnable revokeAction);
    void controlStopped(String reason);
}
