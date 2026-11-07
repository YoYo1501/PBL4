package vn.networkadmin.transport.tls;

import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLSocket;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;
import java.io.IOException;
import java.net.InetSocketAddress;
import java.security.SecureRandom;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;
import java.util.Arrays;

/** TLS 1.2/1.3 client context whose server trust is anchored to the configured SHA-256 certificate pin. */
public final class PinnedTlsClientSocketFactory {
    private final SSLContext context;
    private final String fingerprint;

    public PinnedTlsClientSocketFactory(String fingerprint) throws IOException {
        this.fingerprint = TlsCertificatePin.normalize(fingerprint);
        if (this.fingerprint == null) throw new IllegalArgumentException("Server SHA-256 fingerprint must contain 64 hexadecimal digits.");
        try {
            X509TrustManager trust = new X509TrustManager() {
                @Override public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
                    throw new CertificateException("Client certificate authentication is not configured.");
                }
                @Override public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
                    if (chain == null || chain.length == 0 || !TlsCertificatePin.matches(chain[0], PinnedTlsClientSocketFactory.this.fingerprint))
                        throw new CertificateException("Server certificate SHA-256 pin does not match.");
                }
                @Override public X509Certificate[] getAcceptedIssuers() { return new X509Certificate[0]; }
            };
            context = SSLContext.getInstance("TLS");
            context.init(null, new TrustManager[]{trust}, new SecureRandom());
        } catch (Exception ex) { throw new IOException("Could not initialize pinned TLS.", ex); }
    }

    public SSLSocket connect(String host, int port, int connectTimeoutMillis) throws IOException {
        SSLSocket socket = (SSLSocket) context.getSocketFactory().createSocket();
        try {
            socket.connect(new InetSocketAddress(host, port), connectTimeoutMillis);
            socket.setEnabledProtocols(Arrays.stream(socket.getSupportedProtocols())
                    .filter(p -> p.equals("TLSv1.3") || p.equals("TLSv1.2")).toArray(String[]::new));
            socket.setSoTimeout(connectTimeoutMillis);
            socket.startHandshake();
            X509Certificate peer = (X509Certificate) socket.getSession().getPeerCertificates()[0];
            if (!TlsCertificatePin.matches(peer, fingerprint)) throw new IOException("Server certificate SHA-256 pin does not match.");
            return socket;
        } catch (IOException | RuntimeException ex) { try { socket.close(); } catch (IOException ignored) { } throw ex; }
    }
}
