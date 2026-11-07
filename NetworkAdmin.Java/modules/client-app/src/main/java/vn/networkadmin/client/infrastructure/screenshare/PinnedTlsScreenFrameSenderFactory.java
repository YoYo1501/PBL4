package vn.networkadmin.client.infrastructure.screenshare;

import vn.networkadmin.client.application.screenshare.ScreenFrameSender;
import vn.networkadmin.client.application.screenshare.ScreenFrameSenderFactory;

import java.io.IOException;

public final class PinnedTlsScreenFrameSenderFactory implements ScreenFrameSenderFactory {
    private final String fingerprint;

    public PinnedTlsScreenFrameSenderFactory(String fingerprint) { this.fingerprint = fingerprint; }

    @Override public ScreenFrameSender connect(String host, int port, String requestId, String clientId,
                                               String sessionId, String token) throws IOException {
        return new TlsScreenFrameChannel(host, port, fingerprint, requestId, clientId, sessionId, token);
    }
}
