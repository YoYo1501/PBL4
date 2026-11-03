using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace NetworkAdmin.Server.Security;

internal static class ServerTlsCertificateProvider
{
    private const string SubjectName = "CN=NetworkAdminTool LAN Server";

    public static X509Certificate2 LoadOrCreate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates.OfType<X509Certificate2>()
            .Where(c => c.Subject == SubjectName && c.HasPrivateKey && c.NotAfter > DateTime.UtcNow)
            .OrderByDescending(c => c.NotBefore).FirstOrDefault();
        if (existing is not null) return new X509Certificate2(existing);

        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(SubjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var usages = new OidCollection { new("1.3.6.1.5.5.7.3.1") };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(3));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var pfx = created.Export(X509ContentType.Pfx, password);
        using var persistedCertificate = X509CertificateLoader.LoadPkcs12(pfx, password,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
        CryptographicOperations.ZeroMemory(pfx);
        store.Add(persistedCertificate);
        var persisted = store.Certificates.OfType<X509Certificate2>()
            .FirstOrDefault(c => c.Thumbprint == persistedCertificate.Thumbprint && c.HasPrivateKey);
        return persisted is null
            ? throw new InvalidOperationException("Could not persist the NetworkAdminTool TLS certificate in Current User\\My.")
            : new X509Certificate2(persisted);
    }
}
