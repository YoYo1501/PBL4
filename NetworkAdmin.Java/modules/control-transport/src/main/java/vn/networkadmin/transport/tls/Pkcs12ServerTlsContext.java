package vn.networkadmin.transport.tls;

import javax.net.ssl.KeyManagerFactory;
import javax.net.ssl.SSLContext;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.KeyStore;
import java.security.SecureRandom;

/** Loads a server identity from an operator-provisioned PKCS#12 keystore. */
public final class Pkcs12ServerTlsContext {
    private Pkcs12ServerTlsContext() { }

    public static SSLContext load(Path keyStorePath, char[] password) throws IOException {
        if (keyStorePath == null || password == null || password.length == 0)
            throw new IllegalArgumentException("A PKCS#12 path and non-empty password are required.");
        try (InputStream input = Files.newInputStream(keyStorePath)) {
            KeyStore keyStore = KeyStore.getInstance("PKCS12");
            keyStore.load(input, password);
            KeyManagerFactory keys = KeyManagerFactory.getInstance(KeyManagerFactory.getDefaultAlgorithm());
            keys.init(keyStore, password);
            SSLContext context = SSLContext.getInstance("TLS");
            context.init(keys.getKeyManagers(), null, new SecureRandom());
            return context;
        } catch (Exception ex) { throw new IOException("Could not load the configured PKCS#12 server identity.", ex); }
    }
}
