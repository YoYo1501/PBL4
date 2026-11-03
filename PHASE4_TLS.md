# Phase 4: TLS for LAN control and screen channels

The Server creates one self-signed TLS certificate on first launch and stores it in the current Windows user's `Current User\\Personal` certificate store. It reuses that certificate on later launches. The Server UI exposes its SHA-256 fingerprint in Settings/Endpoint; console mode prints it at startup.

The Client must pin that exact 64-digit SHA-256 fingerprint. The same Server certificate is used on TCP 5000 (agent/control protocol) and TCP 5001 (screen frames). TLS 1.2 or newer is required. No certificate is issued per Client.

## Run on a LAN

1. Start the Server. In its Settings page, copy `TLS SHA256` (or copy it from the console output).
2. Start the Client with the Server's Wi-Fi IPv4 address and copied pin:

   ```powershell
   dotnet run --project NetworkAdmin.Client -- --host 192.168.1.25 --port 5000 --tls-sha256 <64-digit-fingerprint>
   ```

   Replace the example address and fingerprint with the values for the Server. For Visual Studio, add `--tls-sha256 <fingerprint>` to the Client profile's `commandLineArgs`.

3. If using `NETWORKADMIN_TLS_SERVER_SHA256`, set it on each Client to the same fingerprint. A missing or malformed pin makes the normal Client fail closed.

The fingerprint is public information and may be copied over a trusted channel. Do not accept a different fingerprint unexpectedly; compare it with the Server's locally displayed value. If the Server certificate is removed from the current user's certificate store, the next launch creates a new certificate and all Client pins must be updated.

TLS authenticates the Server to each Client and protects confidentiality and integrity of both TCP channels. Control still requires the separate Client consent and is bound to the active Client/Session/Screen/Control request IDs. This demo does not add client certificates or a per-device pairing secret; Client agent identity remains a self-reported stable ClientId. Input events are accepted only from the Server authenticated by the pinned certificate, only during the consented session, and are released when control stops or the connection ends.

## Test configuration

In-memory test fixtures may omit `TlsCertificate` and continue to use loopback plaintext. The executable Server loads a persistent certificate, and the normal executable Client requires a fingerprint. These test-only constructors are not used by `ServerOptions.Load` or `ClientOptions.Load`.
