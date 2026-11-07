package vn.networkadmin.client.application.screenshare;

import java.util.concurrent.CompletionStage;

/** UI boundary; headless implementations must resolve consent as rejected. */
public interface ScreenShareClientView {
    CompletionStage<Boolean> requestConsent(String adminName);
    void sharingStarted(Runnable stopAction);
    void sharingStopped(String reason);
    void connectionChanged(boolean connected);
    default void connectionFailed(String reason) { }
    default void clientIdentity(String name, String clientId) { }
    default void sessionChanged(String sessionId) { }
}
