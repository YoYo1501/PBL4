# .NET to Java migration status

The Java tree is the destination. The .NET solution remains available as a behavioral reference until Java feature parity is reviewed. A feature is not considered migrated merely because a matching library exists: it must be connected to a runnable Java Client/Admin flow.

| Area | Existing .NET behavior | Java destination | Status |
|---|---|---|---|
| Shared screen frame protocol | Full/delta JPEG, bounded framing | `modules/screen-share-protocol` | Integrated with request-bound TLS data channel; existing binary frame format and control message IDs retained |
| Client screen capture/encode | AWT capture, RemoteX delta algorithm | `modules/screen-share-client`, `modules/client-app` | Integrated with explicit Client consent, bounded latest-frame capture/send loop, pinned TLS data channel, and revoke/stop handling |
| Admin frame decode/composition | Ordered full/delta canvas | `modules/screen-share-server`, `modules/admin-app` | Integrated with consent/session coordinator, TLS data listener, first-frame state, and Swing Screen Viewer |
| LAN scan and ping | CIDR `/23`–`/30`, ARP + ping, four-probe ping | `modules/network-core`, `modules/client-runtime`, `modules/admin-runtime`, `modules/admin-app` | Ported and connected to session-bound Ping/Scan request-response flow; dashboard actions are wired; live LAN verification pending |
| Network interface inventory | Interfaces, IPv4, MAC, gateway, subnet | `modules/network-core`, `modules/client-runtime`, `modules/admin-runtime` | Client publishes snapshot on connect and on changes; server stores per-session snapshot; OS route/ARP parsing still needs live platform verification |
| Shared control envelope | Numeric message IDs 1–33; UTF-8 JSON-lines `type/requestId/data`; 256 KiB bound | `modules/control-protocol` | Ported; includes envelope checks, Hello/HelloAck contracts and bounded message-ID replay ledger |
| Shared control DTOs/policies | Ping/scan, remote session, screen, input, chat, monitoring, security events/alerts, administration contracts | `modules/control-protocol` | Existing numeric message IDs retained; screen frame and remote input validation DTOs plus feature payload handling are integrated; security event/alert DTO policies remain pending |
| TLS and certificate pinning | TLS 1.2/1.3; Client pins SHA-256 of raw server certificate | `modules/control-transport` | TLS 1.2/1.3 client pin verification and PKCS#12 server context ported; app config and keystore provisioning remain |
| Client registration and session lifecycle | Hello/HelloAck, one session per ClientId, heartbeat stale cleanup, reconnect | `modules/control-transport`, `modules/client-runtime`, `modules/admin-runtime` | Runnable Admin/Client apps; stable ClientId snapshots become offline on disconnect and update on reconnect with new SessionId; stale-session messages are ignored |
| Client and Admin desktop apps | WinForms Client and Admin dashboard | `modules/admin-app`, `modules/client-app` | Admin dashboard, Chat dialog, Screen Viewer, and Client consent/status/chat window are wired |
| Authorization and consent | Admin token/permission, session capabilities, screen/control consent | `modules/admin-app`, `modules/client-app`, `modules/screen-share-protocol` | Screen viewing and remote control each require explicit Client acceptance; requests bind ClientId, SessionId, requestId, and channel; reject, stop, revoke, disconnect, or reconnect ends access |
| Agent lifecycle and reconnect | Registration, telemetry, heartbeat, reconnect cleanup | `modules/control-transport`, `modules/client-runtime`, `modules/admin-runtime` | Heartbeat, inventory, Ping/Scan, cancellation, remote control, Chat, response correlation, and cleanup are wired |
| Remote input | Mouse, keyboard, release-all, policy limits | `modules/control-protocol`, `modules/client-app`, `modules/admin-app` | Consent-bound input, normalized coordinates, rate limiting, pointer coalescing, and release-all on revoke/disconnect integrated; physical desktop validation remains |
| Remote chat | Session-bound chat over control channel | `modules/client-app`, `modules/admin-app`, `modules/control-protocol` | Two-way session-bound Chat integrated with payload validation, bounded in-memory Admin transcript, and integration coverage; history is not persisted |
| System monitoring and telemetry | CPU/RAM samples, dashboard history | `modules/control-transport`, `modules/admin-app` | JDK CPU/RAM heartbeat is displayed; network OS/interface metadata is included; historical charting remains limited |
| Network tools | Ping, LAN scan, cancellation, local subnet policy | `modules/network-core`, `modules/client-runtime`, `modules/admin-runtime`, `modules/admin-app` | Ping and bounded concurrent subnet scans wired; progress/cancel supported and CIDR limited to a local Client interface subnet |
| Security event collection | Windows Event Log and NDJSON sources | Java security-monitoring module | Not migrated |
| Feature extraction and AI inference | 69-feature preprocessing and ONNX Isolation Forest | Java security-analysis module | Not migrated; model assets need to be carried forward |
| Alerts, audit, auto-disconnect policy | Bounded alert flow, audit and opt-in policy | Java security/admin modules | Not migrated |
| Logging, configuration, packaging | .NET hosting/options/installer | Java app/runtime modules | Not migrated |

## Current compile check

`build-screen-share.ps1` compiles all current Java modules with `javac --release 21`. This checks source compatibility, not runtime parity, cross-runtime interoperability, LAN behavior, or Windows integrations.
