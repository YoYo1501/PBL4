# NetworkAdmin Java migration

Both the Admin/Server and Client applications are being migrated to Java. The existing .NET solution stays intact as a reference and fallback until the Java applications reach feature parity.

See [MIGRATION_STATUS.md](MIGRATION_STATUS.md) for the complete feature-by-feature port inventory and current status.

## Current migration slices

- `modules/screen-share-protocol`: bounded screen frame packets, binary framing compatible with `NetworkAdmin.Shared.Protocol.ScreenFrameProtocol`, and the consent-bound data-channel handshake.
- `modules/screen-share-client`: AWT capture adapter, independent latest-frame loop, and full/delta JPEG encoder using RemoteX's 16x16 block and 35% area rules.
- `modules/screen-share-server`: JPEG decode and full/delta composition for a screen viewer.
- `modules/network-core`: CIDR validation/host ranges, interface inventory, ping series, ARP lookup, and bounded LAN discovery. It ports the .NET scanner constraints (IPv4 `/23`–`/30`, 510 hosts maximum, 32 concurrent probes, warm-up then retry, ping-or-ARP discovery) behind injected ports.
- `modules/control-protocol`: numeric message IDs, UTF-8 JSON-lines envelope framing, payload/frame validation, Hello/HelloAck DTOs, capabilities, and bounded duplicate-message ledger compatible with the .NET control protocol.
- `modules/control-transport`: TLS 1.2/1.3 with pinned SHA-256 server certificates, PKCS#12 server identity loading, Hello/HelloAck listener, one-session-per-client registry, heartbeat timeout cleanup and Client reconnect supervisor.
- `modules/client-runtime`: Client composition/runtime for publishing network inventory and CPU/RAM heartbeat, plus handling session-scoped Ping and Scan requests.
- `modules/client-app`: runnable Client entry point and composition root; delegates business/runtime behavior to `client-runtime` and existing transport/network modules.
- `modules/admin-runtime`: Admin-side session snapshots and Ping/Scan command dispatch with request/response correlation bound to the originating session.

The modules use `domain`, `application`, `ports`, and `infrastructure` packages. Use cases receive adapters through constructors. The capture loop uses a replaceable latest-frame store so a slow sender can skip stale captures instead of building a queue.

The transport and network flows are composed into Client/Admin runtime services. `modules/admin-app` contains the runnable Java Swing Admin dashboard, Chat dialog, and Screen Viewer. `modules/client-app` provides a runnable Client agent with explicit consent prompts, Stop Sharing and Revoke Control actions, and a chat panel. Screen frames use a separate TLS data socket (default port 5001); control, telemetry, Ping, Scan, remote input, and Chat use the existing JSON-lines TLS socket (default port 5000). Remote input requires a separate Client consent prompt and is revoked on Client action, Admin stop, disconnect, or session change. LAN scans are restricted to the Client local interface subnet and support cancellation. The .NET solution remains intact as a migration reference; no C# source has been removed.

The current Java desktop flow includes basic Client management, online/offline session snapshots, CPU/RAM telemetry, Ping, bounded LAN scan, screen viewing, consented remote input, and session-bound Chat. Security event collection, AI inference/alerts, persistent chat history, production packaging, and live multi-machine validation remain follow-up work.

## Server TLS identity

The transport loads an operator-provisioned PKCS#12 identity through `Pkcs12ServerTlsContext.load(path, password)`. This avoids embedding a keystore password or generating a certificate with an unmanaged secret. On Windows PowerShell, set the password in the environment and generate/export the certificate with the JDK tools:

```powershell
$env:NETWORKADMIN_TLS_KEYSTORE_PASSWORD = Read-Host "PKCS#12 password"
keytool -genkeypair -alias networkadmin -keyalg RSA -keysize 3072 -sigalg SHA256withRSA -validity 1095 -dname "CN=NetworkAdminTool LAN Server" -storetype PKCS12 -keystore .\server.p12 -storepass:env NETWORKADMIN_TLS_KEYSTORE_PASSWORD -keypass:env NETWORKADMIN_TLS_KEYSTORE_PASSWORD
keytool -exportcert -alias networkadmin -keystore .\server.p12 -storepass:env NETWORKADMIN_TLS_KEYSTORE_PASSWORD -file .\server.cer
Get-FileHash .\server.cer -Algorithm SHA256
```

Configure the server with the PKCS#12 file/password and configure each Client with the displayed 64-digit fingerprint. Restrict access to the keystore file and password to the server account. Java and .NET can then pin the same certificate.

## Build and test

The Java modules target Java 21 or newer. From this directory, use the Gradle Wrapper:

```powershell
.\gradlew.bat build
.\gradlew.bat test
```

## Run the Admin dashboard

For a local VS Code test, `.vscode/launch.json` reads `NetworkAdmin.Java/.env.local` for both Java launch configurations. A local test keystore and matching certificate are stored as ignored files (`local-test.p12`, `local-test.cer`); `.env.example` documents the expected values. These self-signed credentials are for development only. Keep the `.p12` and `.env.local` private and do not commit them.

Provision a PKCS#12 identity as described above, then configure the keystore path and password in the Admin process environment. The password is read from the environment and is not accepted as a command-line argument:

```powershell
$env:NETWORKADMIN_TLS_KEYSTORE_PATH = (Resolve-Path .\server.p12).Path
$env:NETWORKADMIN_TLS_KEYSTORE_PASSWORD = Read-Host "PKCS#12 password"
.\gradlew.bat :modules:admin-app:run
```

By default, Admin listens on `0.0.0.0:5000`. Set `NETWORKADMIN_SERVER_HOST` and `NETWORKADMIN_SERVER_PORT` to change the bind address. Set `NETWORKADMIN_HEARTBEAT_TIMEOUT_SECONDS` (10–300, default 30) to change stale-client detection. Configure the Java Client to pin the certificate fingerprint printed by `Get-FileHash` and connect to this host/port.

## Run the Client agent

Set `NETWORKADMIN_TLS_SERVER_SHA256` to the Admin certificate SHA-256 fingerprint and, if needed, set `NETWORKADMIN_SERVER_HOST` and `NETWORKADMIN_SERVER_PORT`. Then run:

```powershell
.\gradlew.bat :modules:client-app:run
```

For a same-machine check, use `127.0.0.1` for `NETWORKADMIN_SERVER_HOST`, start Admin first, then run Client. Select the connected Client and click **View Screen**; sharing begins only after clicking **Accept** on Client. Either **Stop Viewing** in the Admin viewer or **Stop Sharing** on Client ends the stream.

For two LAN machines, bind Admin control to `0.0.0.0`, set `NETWORKADMIN_SCREEN_HOST` to the Admin machine's reachable LAN IPv4 address, and configure that same address as the Client's `NETWORKADMIN_SERVER_HOST`. Permit inbound TCP ports 5000 and 5001 on the Admin machine/firewall. Client verifies the Admin certificate fingerprint on both TLS channels. A disconnect ends consent; reconnect requires a new request and a new Client acceptance.

To compile production sources directly with the JDK, run:

```powershell
./build-screen-share.ps1
```

