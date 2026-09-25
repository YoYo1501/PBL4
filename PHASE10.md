# Phase 10 — Server Network Administration GUI

Implemented the Server WinForms dashboard on `net10.0-windows`. Phase 11 was not started.

## Launch

```powershell
dotnet run --project NetworkAdmin.Server/NetworkAdmin.Server.csproj -- --host 0.0.0.0 --port 5000
dotnet run --project NetworkAdmin.Client/NetworkAdmin.Client.csproj -- --host <server-LAN-IP> --port 5000
```

The existing options/environment variables remain supported. `ServerOptions.Load` still defaults to loopback; explicitly use `--host 0.0.0.0` or the server LAN address for clients on other machines. The dashboard displays the actual configured listening address and startup errors. The pre-existing edit to `ServerOptions.cs` and the pre-existing `publish/` directory were preserved.

The console interface remains available through `--console` (opens a console window): `clients`, `ping <sessionId> <target>`, `scan <sessionId> [subnet]`, `exit`.

## Files created

- `NetworkAdmin.Server/ServerDashboardForm.cs`
- `NetworkAdmin.Server/ServerRuntime.cs`
- `NetworkAdmin.Shared/Protocol/ScanSubnet.cs`
- `NetworkAdmin.Server/Validation/Phase10.Validation.csproj`
- `NetworkAdmin.Server/Validation/Program.cs`
- `PHASE10.md`

## Files modified by Phase 10

- `Interfaces/IPingService.cs`
- `NetworkAdmin.Client/Services/ClientCommandHandler.cs`
- `NetworkAdmin.Client/Services/NetworkInfoService.cs`
- `NetworkAdmin.Client/Services/NetworkScannerService.cs`
- `NetworkAdmin.Client/Services/PingService.cs`
- `NetworkAdmin.Server/ClientManager.cs`
- `NetworkAdmin.Server/ClientSessionInfo.cs`
- `NetworkAdmin.Server/NetworkAdmin.Server.csproj`
- `NetworkAdmin.Server/Program.cs`
- `NetworkAdmin.Server/ServerCommandDispatcher.cs`
- `NetworkAdmin.Server/TcpHelloServer.cs`
- `NetworkAdmin.Shared/Models/NetworkInterfaceInfo.cs`
- `NetworkAdmin.Shared/Models/PingResult.cs`

## Architecture and behavior

`ServerDashboardForm -> ServerRuntime -> ClientManager / ServerCommandDispatcher / TcpHelloServer -> Shared TCP protocol -> Client Agent`.

Client and Server still reference Shared, with no project reference between Client and Server. The form contains no sockets, protocol parsing, local scanning, or local CPU/RAM collection. The existing framed JSON connection carries Hello/HelloAck, network snapshots, telemetry, Ping and Scan. Additional Ping statistics and interface subnet fields are additive.

The runtime starts the listener on a background task, catches startup failures, and exposes locked session snapshots. A 500 ms WinForms timer reads those snapshots on the UI thread. Awaited commands resume on the UI thread. Selection is keyed by SessionId, so separate sessions remain separate even when ClientIds or hostnames match. Recent disconnected sessions remain visible (up to 200); their commands are disabled and their retained telemetry is explicitly marked non-live. Telemetry older than ten seconds is marked stale.

HelloAck uses the per-session send lock; sessions become selectable after acknowledgement. Disconnect removes the session before failing pending requests. Send semaphores are not disposed while concurrent writers may still use them. Responses must match both request ID and originating session. Cancellation now propagates as cancellation instead of a timeout. Shutdown cancels commands and waits for connection handlers before closing the form. Malformed peers are isolated from other clients.

## GUI pages

- **Dashboard:** listener status/address, connected count, selected identity/IP, CPU/RAM, telemetry timestamp, and latest completed scan summary.
- **Clients:** hostname, IPv4, MAC, status, explicitly labeled SessionId/ClientId, connection time and last seen; selected client's interface/IP/MAC/gateway/subnet table.
- **IP Scanner:** selected-agent remote Scan, subnet prefill when reported, validation, results and originating agent. Previous results are clearly identified while another scan runs or fails.
- **Ping:** shared agent selector, target entry, four attempts with individual status/RTT, sent/received/lost, loss percentage and average successful RTT. Results identify the originating session even if selection changes mid-command.
- **Monitoring:** selected-agent CPU/RAM percentages and progress bars; empty, waiting, stale and disconnected states.

## Ping

Yes: remote Ping now sends **four** probes, including after a successful reply, using `PingService.PingSeriesAsync`. Each probe has a one-second timeout and supports cancellation. The dispatcher allows fifteen seconds for the operation. `Success` means at least one reply; average RTT covers successful replies only, and no replies produce an unavailable average. All four attempt statuses are returned in the same PingResult message.

Existing single-host/retry service methods remain unchanged for legacy UI and discovery. Older clients can still return their old PingResult; the GUI indicates unavailable packet statistics instead of inventing them. Update agents to enable four-request remote Ping.

## Scan matching and limits

The GUI sends ScanRequest to the selected agent and receives ScanResult through the existing dispatcher. No scanner was added to the Server.

Each scanned IPv4 is matched to **all reported interface addresses of currently connected sessions**. Before NetworkInfo arrives, the TCP peer address is the fallback. Matching names are shown and Managed Client is Yes; otherwise it is No. Matches refresh as clients connect/disconnect. Scanned hosts never receive invented CPU/RAM values.

The dashboard accepts unicast IPv4 CIDR `/24` through `/30`, plus the legacy three-octet `/24` notation. It rejects multicast/Class D, reserved high ranges, loopback, zero-network, malformed input and unsupported sizes. Remote validation runs on the agent too. The legacy scanner's existing range support is retained with added non-unicast rejection. The existing console's omitted-subnet path still uses the agent's local subnet.

## Build validation

Executed sequentially, all **PASS**, each with **0 warnings and 0 errors**:

1. `dotnet build NetworkAdmin.Shared/NetworkAdmin.Shared.csproj`
2. `dotnet build NetworkAdmin.Client/NetworkAdmin.Client.csproj`
3. `dotnet build NetworkAdmin.Server/NetworkAdmin.Server.csproj`
4. `dotnet build NetworkAdminTool.csproj`
5. `dotnet build NetworkAdminTool.slnx -m:1`

`git diff --check` passed.

## Automated validation

```powershell
dotnet run --project NetworkAdmin.Server/Validation/Phase10.Validation.csproj
```

**29 checks PASS.** These cover subnet rejection/normalization; real ICMP four-probe loopback and cancellation; real Hello/HelloAck; independent sessions/network snapshots/telemetry; Ping/Scan routing; cross-session response rejection; telemetry while commands are pending; response serialization; managed-host matching and reclassification on disconnect; cancellation and pending-command failure on disconnect; malformed peer isolation; the real Client command handler performing remote four-probe Ping; and awaited server shutdown.

The harness uses actual loopback TCP connections, synthetic A/B agents for deterministic telemetry and ScanResult data, and the real Client connection/command handler/PingService for end-to-end remote Ping. It does **not** prove physical LAN discovery or GUI interaction. Validation sources are excluded from the Server application build.

## Manual test results A–P

The Windows computer-use helper was unavailable: `Computer Use native pipe is unavailable ... The system cannot find the file specified. (os error 2)`. Retry and session recovery failed. No visual/interactive GUI or multi-machine LAN test is claimed.

| Test | Manual result | Separate evidence / outstanding validation |
|---|---|---|
| A. GUI starts and listens | NOT TESTED | Background runtime listening passed automatically; visual startup pending. |
| B. Client appears in Clients list | NOT TESTED | TCP registration/snapshots passed; visible list pending. |
| C. Correct client hostname/IP | NOT TESTED | Reported network snapshots passed; GUI rendering pending. |
| D. CPU/RAM update live | NOT TESTED | Telemetry snapshots passed; visual updates pending. |
| E. Two clients remain separate | NOT TESTED | Two synthetic TCP sessions passed; physical clients/GUI pending. |
| F. Selecting A/B switches telemetry | NOT TESTED | Session isolation passed; selector interaction pending. |
| G. Remote Ping from selected client | NOT TESTED | Real Client command-handler remote Ping passed; GUI interaction pending. |
| H. Four attempts and packet loss shown | NOT TESTED | Four real loopback probes and result statistics passed; display pending. |
| I. Remote /24 scan | NOT TESTED | Routing and synthetic ScanResult passed; actual LAN discovery pending. |
| J. Scan IP/MAC/status table | NOT TESTED | Result mapping passed; GUI rendering pending. |
| K. Connected host marked Managed | NOT TESTED | Deterministic matching passed; physical scan/GUI pending. |
| L. No fake CPU/RAM for non-agent host | NOT TESTED | Scan row model has no telemetry fields; visual verification pending. |
| M. Telemetry during Ping | NOT TESTED | Telemetry while Ping awaited a response passed automatically. |
| N. Telemetry during Scan | NOT TESTED | Telemetry while Scan awaited a response passed automatically. |
| O. Disconnect updates GUI safely | NOT TESTED | Offline snapshots and pending-command cleanup passed; GUI pending. |
| P. GUI closes cleanly | NOT TESTED | Runtime shutdown passed; interactive form closure pending. |

## Known limitations and Phase 11 boundary

- Visual layout, actual GUI interaction and a physical multi-machine LAN scan still require manual validation.
- Total/used RAM is not in the existing telemetry model; only actual reported CPU/RAM percentages are displayed.
- Older agents do not report subnet or four-probe statistics; the GUI leaves these unavailable.
- Remote dashboard scans are deliberately limited to `/24`–`/30`. If the reported subnet is larger, enter a supported subnet explicitly.
- Existing discovery considers ping replies or ARP-cache presence, so an Online result is a discovery observation, not proof of an installed agent or a continuously reachable host. MAC data may be absent, especially across routers.
- There is no protocol-level per-command cancel message. Closing/disconnecting cancels the connection/runtime; a server-side command timeout removes pending state but cannot individually stop a remote scan that is already running.
- Data is in memory only. NetworkInfo is currently reported when the agent connects. There is no new telemetry connection or automatic network snapshot refresh.
- No Phase 10 feature was intentionally deferred to Phase 11. History persistence, export, richer charts and broader scan controls remain possible future work; none was implemented here. No remote-control features were added.
