# Phase 11 — Functional hardening and project cleanup

## Dependency inventory (recorded before Phase 11 edits)

The preceding user-requested cleanup is already present as uncommitted changes. Do not repeat or discard it.

| Original root content | Current owner / consumers | Decision |
| --- | --- | --- |
| Forms/*.cs | Client UI, registered by Client Program | Already moved to Client/Forms; keep |
| Interfaces/*.cs | Client services and forms; validation links IPingService/INetworkScannerService | Already moved to Client/Interfaces; keep and update validation paths |
| Models/NetworkScanProgress.cs | Local scanner/progress UI and validation | Already moved to Client/Models; not a wire DTO |
| Services/LoggerService.cs, NetworkDashboardState.cs | Local Client dashboard, alerts, logs | Already moved to Client/Services; keep |
| Assets/*, appsettings.json | Client runtime content copied to output | Already moved to Client; settings loaded beside executable |
| Root Program.cs / NetworkAdminTool.csproj | Duplicate local-only startup and linked Client services | Previously consolidated into Client --standalone; no production reference remains |
| Shared/Models, Messages, Protocol | Both Client and Server | Keep in Shared; existing NetworkAdminTool.Models namespace is intentional compatibility |
| Server/UI, runtime/session/dispatcher/listener | Server only | Keep in Server |
| Server/Validation | Standalone test executables linking internal production classes | Test-only source links; excluded from Server application compilation |

Production project graph: Client -> Shared and Server -> Shared. No Client/Server project reference. Client no longer has external Compile/Link items. Validation links Client source only for tests, not for the shipped Server. Existing namespaces/usings remain compatible; no namespace-only rewrite is needed. Existing publish/, bin/, obj/ and logs are build/runtime artifacts, not source dependencies; they are not deleted.

Phase 10/10.1 reports describe historical builds and limits; this report supersedes those limits when Phase 11 validation completes. No additional legacy source deletion is planned before validation.

## Files created

- `NetworkAdmin.Shared/Models/NetworkInterfacePolicy.cs`: selection rules shared by Client and Server, without wire fields.
- `NetworkAdmin.Server/UI/SessionCommandState.cs`: per-session presentation state for commands.
- `NetworkAdmin.Server/Validation/Phase11Checks.cs`: focused regression checks using the existing harness.
- `PHASE11.md`: dependency inventory, implementation, evidence and manual procedure.

## Files modified

- Client: `ClientOptions.cs`, `Program.cs`, `Services/ClientCommandHandler.cs`, `Services/ClientRuntime.cs`, `Services/NetworkInfoService.cs`, `Services/NetworkScannerService.cs`, `Services/ServerConnection.cs`, and the moved `Forms/ScannerForm.cs`.
- Server: `ServerCommandDispatcher.cs`, `ServerDashboardForm.cs`, `ServerRuntime.cs`, `TcpHelloServer.cs`, `UI/OperationViews.cs`, `UI/TelemetryView.cs`.
- Shared: `Protocol/ScanSubnet.cs`, `Protocol/JsonLineProtocol.cs`.
- Validation: `NetworkAdmin.Server/Validation/Program.cs`, `Phase10.Validation.csproj`, `Layout/Program.cs`.
- Cleanup/documentation: `NetworkAdmin.Client/NetworkAdmin.Client.csproj`, `NetworkAdminTool.slnx`, `README.md` (includes preceding cleanup changes).

## Files moved and deleted

The preceding cleanup moved these 16 files; Phase 11 verified their consumers and retained these moves:

| Old location | New location |
| --- | --- |
| `Forms/{MainForm,ScannerForm,PingForm,MonitorForm,FeatureForms}.cs` | `NetworkAdmin.Client/Forms/` |
| `Interfaces/{ILoggerService,INetworkInfoService,INetworkScannerService,IPingService,ISystemMonitorService}.cs` | `NetworkAdmin.Client/Interfaces/` |
| `Models/NetworkScanProgress.cs` | `NetworkAdmin.Client/Models/NetworkScanProgress.cs` |
| `Services/{LoggerService,NetworkDashboardState}.cs` | `NetworkAdmin.Client/Services/` |
| `Assets/{icon,chart}.png` | `NetworkAdmin.Client/Assets/` |
| `appsettings.json` | `NetworkAdmin.Client/appsettings.json` |

Deleted in that preceding cleanup: root `Program.cs` and `NetworkAdminTool.csproj`. Phase 11 deletes no additional source files. Duplicate CIDR enumeration implementations and the unused `ClientOptions.Duration` / `--duration-seconds` parser were removed from existing files. The duration option had no runtime consumer.

`NetworkAdminTool.csproj` does **not** remain. Its local UI/services are all compiled into Client, its source links are unnecessary, and `dotnet run --project NetworkAdmin.Client -- --standalone` preserves local-only operation without starting the connection runtime. Production builds and the solution pass without it. Interactive standalone feature smoke testing remains on the manual checklist; no manual Client window test is claimed.

## Scanner hardening

- A full 32-bit IPv4 mask computes the network, replacing the last-octet-only mask. `172.31.99.116/23` normalizes to `172.31.98.0/23`.
- Usable hosts are `172.31.98.1` through `172.31.99.254`: 510 addresses. `172.31.98.255` and `172.31.99.0` are valid hosts in this /23; only the actual network and broadcast are excluded.
- Server validation, remote Client validation, direct Client scanning and connected-device enumeration use the bounded Shared subnet helper. Allowed range is `/23` through `/30`, plus legacy three-octet /24 notation. Larger CIDRs are intentionally rejected, including through the local scanner UI.
- No network is hardcoded. Server prefills from the selected Client's reported primary interface, including when NetworkInfo arrives after Hello. Switching to an agent without subnet data clears the previous agent's prefill.
- Scan execution stays on Client. Existing two-pass discovery uses at most 32 active probes per scan. The remote command handler permits one running scan per connection and rejects overlapping scan requests instead of accumulating tasks.
- Per-probe timeouts are bounded to 500–1000 ms in discovery; the warm-up pass remains 250 ms. The ARP subprocess has a real two-second async deadline and is stopped on cancellation/timeout.
- The GUI/dispatcher retains a 60-second scan deadline. Both Ping and Scan deadlines now cover send-lock/write time as well as response waiting.

## Interfaces

The existing Client reporting already filters to Up, non-loopback/non-tunnel adapters with IPv4. All reported virtual adapters remain in the detailed list. Shared name/description classification prevents known VMware, vEthernet, Hyper-V, virtual, VPN/tunnel-style adapters from becoming primary when a physical adapter exists. Both sides require usable IPv4 and prefer a valid default gateway among physical candidates. Local default gateway selection follows the same primary interface. No DTO/service was renamed and no protocol fields were added.

## Connection, telemetry and result association

- Client retries initial failure and disconnection with cancellable backoff of 1, 2, 4, 8, then at most 10 seconds; each connection/Hello attempt has a 10-second deadline. A successful connection resets backoff.
- Each attempt owns a new `ServerConnection` and session cancellation source. The stable ClientId algorithm and Server-generated SessionId remain unchanged. NetworkInfo is resent and telemetry restarts on reconnect.
- On session loss, read/telemetry tasks and the tracked scan are canceled and observed before reconnection. Old handlers never get the new stream. Shutdown also interrupts backoff.
- TCP keepalive is enabled on both endpoints (10-second idle, 2-second interval, 3 probes). This assists detection of broken idle connections; actual network-loss detection timing still depends on TCP/OS behavior. No heartbeat/message type was added.
- Server bounds the initial Hello wait to 10 seconds. Existing per-client exception isolation, session removal and pending-command failure remain in place.
- Telemetry older than 10 seconds is stale. Monitoring labels retained values; the Clients table shows unavailable CPU/RAM when stale instead of implying live values.
- Ping/Scan display state is keyed by SessionId. Async callbacks update the captured originating state; rendering reads the selected state. Switching A -> B while A finishes cannot put A's results under B. Selecting A again restores A's results; reconnect starts an empty command state.
- A newly started command clears the previous result of that command type for that session, avoiding old rows under a new target/error. Other sessions' results remain retained. Summary cards follow the selected session. Existing dark layout and navigation are preserved.
- Client startup remains on its STA thread across host initialization, so WinForms startup does not depend on synchronous completion of an async host call.

## Automated validation

The original Phase 10/10.1 harness passed all 36 checks before adding regressions. The expanded harness passes **69 checks** (the original 36 plus 33 regression checks):

```powershell
dotnet run --project NetworkAdmin.Server/Validation/Phase10.Validation.csproj --no-restore
```

Covers /23 normalization and endpoints/cross-boundary hosts, /24 and /30, invalid/excessive prefixes, actual scanner enumeration with fake ICMP (510 addresses, two passes, maximum 32 probes), cancellation, adapter selection/list retention, freshness, cross-session Ping/Scan rejection, delayed old response rejection, same-ClientId reconnect, real ClientRuntime initial failure retry, abrupt socket reset, Server shutdown/restart, scan overlap rejection/cancellation, and real loopback Ping after reconnect. Send-lock contention verifies that both command deadlines include the send phase.

This uses real loopback TCP and ICMP, real local adapter reporting, synthetic CPU/RAM and a controlled scanner for connection tests. It performs no physical LAN scan. The actual scanner range/concurrency test uses fake Ping responses; it does not prove reachability of 510 LAN hosts.

```powershell
dotnet run --project NetworkAdmin.Server/Validation/Layout/Layout.Validation.csproj --no-restore -- --96
dotnet run --project NetworkAdmin.Server/Validation/Layout/Layout.Validation.csproj --no-build --no-restore
```

Both pass five off-screen UI state checks (completion for A while B selected, restoration of A, new-session isolation, late /23 prefill/clearing, stale label). These model asynchronous completion by updating the captured A state; actual routing/lifecycle is independently exercised by the TCP harness. Layout bounds pass at 1366x768 and 1920x1080 / 96 DPI, and 2732x1536 and 3200x1904 / 192 DPI. PNGs are generated under ignored `artifacts/layout/`. These are automated off-screen checks, not manual UI interaction or a two-PC test.

## Build results

All four required builds passed with **0 warnings and 0 errors**:

```powershell
dotnet build NetworkAdmin.Shared/NetworkAdmin.Shared.csproj
dotnet build NetworkAdmin.Client/NetworkAdmin.Client.csproj
dotnet build NetworkAdmin.Server/NetworkAdmin.Server.csproj
dotnet build NetworkAdminTool.slnx -m:1
```

`git diff --check` passed. The root project is not needed to build the current solution.

## Remaining limitations

- No protocol-level cancel-one-command message. A Server timeout cancels its wait and rejects late results; a remote command already running stops through completion or connection cancellation, not a new wire cancel operation.
- Existing single-host discovery Ping calls finish their bounded timeout/retries before noticing scan cancellation (up to about two seconds with the enforced limit).
- Connection status represents a TCP session. Silent cable/Wi-Fi failure detection is not instantaneous; stale telemetry is separately marked after 10 seconds. Keepalive cannot guarantee an exact disconnect time across networks/OSes.
- Interface classification remains a name/description heuristic. Unknown renamed virtual adapters can evade it. NetworkInfo refreshes on connection/reconnection, not continuously during a session.
- Scan routes are chosen by the Client OS. Reported subnet selection does not bind ICMP to a physical NIC. ARP cache entries can outlive reachability, and MAC addresses may be unavailable across routers.
- Sessions/results/history remain in memory. UI permits one outstanding Ping and one Scan globally; this change does not introduce parallel UI command scheduling.
- Existing empty image placeholders, historical phase reports and generated publish artifacts were preserved. Old published binaries must be rebuilt/redeployed to gain Phase 11 behavior.
- Manual two-PC behavior and interactive local/standalone features remain unverified in this run. The user's earlier working two-PC baseline is distinct from Phase 11 validation.

## Exact manual tests on two physical PCs

Use updated code/binaries on both PCs. Replace `<PC1-LAN-IP>` with the real IPv4 of PC1. For the example /23, confirm the real adapter mask is 255.255.254.0 with `ipconfig`; do not change addressing just to match the example. Allow the existing Server TCP port through Windows Firewall where required.

1. **Start Server on PC1:** `dotnet run --project NetworkAdmin.Server -- --host 0.0.0.0 --port 5000`. **Start remote Client B on PC2:** `dotnet run --project NetworkAdmin.Client -- --host <PC1-LAN-IP> --port 5000`. Verify hostname/IP, CPU/RAM and separate ClientId/SessionId in Clients.
2. **Adapter selection:** on PC2 keep VMware/vEthernet adapters enabled alongside physical Wi-Fi/Ethernet. Select B on Server. Verify physical IPv4/gateway/subnet is primary and all reported virtual adapters remain in the details table.
3. **/23 scan:** if PC2 is `172.31.99.116/23`, verify prefill `172.31.98.0/23`. Enter `172.31.99.116/23` and scan. With known reachable devices in each half, verify discoveries from both `172.31.98.x` and `172.31.99.x`. Compare with Ping from PC2. Absence of a host that blocks ICMP and has no ARP entry is not proof of a range bug. Retest a /24. Enter /22, /31, malformed IPv4: reject without launching a scan.
4. **Selection while pending:** start an additional Client A on PC1 using `dotnet run --project NetworkAdmin.Client -- --host 127.0.0.1 --port 5000`. Select B, start Ping to an unreachable address, immediately select A. B results must not appear for A. Select B again and verify four attempts belong to B. Repeat with B Scan while switching A/B; each view and summary must remain per-session.
5. **Stale vs disconnect:** pause PC2 Client in a debugger for more than 10 seconds while leaving its TCP connection open. Server must label telemetry stale and clear live CPU/RAM in the Clients table. Resume and verify fresh data restores. Then close/terminate Client during Ping/Scan: Server stays usable, pending command fails, old session becomes Offline.
6. **Reconnect and network loss:** restart PC2 Client, verify same ClientId/new SessionId and empty results for the new session. Disable and re-enable PC2 Wi-Fi/Ethernet while Client keeps running: observe stale state, eventual Offline when TCP detects loss, then automatic reconnect and fresh telemetry. Select the new Online session and rerun Ping/Scan.
7. **Server restart and startup order:** close Server during a scan; reopen it on the same endpoint while Client remains running. Verify automatic reconnection, telemetry and working remote Ping. Also start Client before Server, then open Server and verify it connects without restarting Client.
8. **Local replacement smoke test:** close the local Client A and run `dotnet run --project NetworkAdmin.Client -- --standalone` on PC1. Verify local Dashboard, Ping, Scanner, Monitoring, Network Info, Alerts, Logs, Settings and About open normally. No additional Server session should be registered by this standalone process. Close both apps and verify their processes exit.

Phase 11 only. Phase 12 was not started.
