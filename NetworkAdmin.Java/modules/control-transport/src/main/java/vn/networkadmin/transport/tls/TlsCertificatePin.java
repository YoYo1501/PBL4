package vn.networkadmin.transport.tls;

import java.security.MessageDigest;
import java.security.cert.CertificateEncodingException;
import java.security.cert.X509Certificate;
import java.util.HexFormat;

public final class TlsCertificatePin {
    private TlsCertificatePin() { }

    public static String normalize(String fingerprint) {
        if (fingerprint == null || fingerprint.isBlank()) return null;
        StringBuilder normalized = new StringBuilder(64);
        for (int i = 0; i < fingerprint.length(); i++) {
            char c = fingerprint.charAt(i);
            if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')) normalized.append(Character.toUpperCase(c));
            else if (c != ':' && c != '-' && !Character.isWhitespace(c)) return null;
        }
        return normalized.length() == 64 ? normalized.toString() : null;
    }

    public static boolean matches(X509Certificate certificate, String expectedFingerprint) {
        String expected = normalize(expectedFingerprint);
        if (certificate == null || expected == null) return false;
        try {
            byte[] expectedBytes = HexFormat.of().parseHex(expected);
            byte[] actual = MessageDigest.getInstance("SHA-256").digest(certificate.getEncoded());
            return MessageDigest.isEqual(actual, expectedBytes);
        } catch (Exception ex) { return false; }
    }
}
