# Phase 10.1 - Server dashboard redesign

Implemented a dark, centralized Server dashboard using the supplied image as the visual design reference. No Phase 11 work or legacy cleanup was performed.

## 1. UI files created

- `NetworkAdmin.Server/UI/DashboardTheme.cs`: shared navy palette, buffered layouts/tables, card panels, summary cards, buttons, inputs and drawn globe branding.
- `NetworkAdmin.Server/UI/ClientDetailView.cs`: selected-client identity, Online/Offline state, secondary identifiers/timestamps and primary network details.
- `NetworkAdmin.Server/UI/OperationViews.cs`: reusable Scan and Ping sections used on both Dashboard and dedicated pages.
- `NetworkAdmin.Server/UI/TelemetryView.cs`: custom circular CPU/RAM gauges, history chart and explicit telemetry freshness labels.
- `NetworkAdmin.Server/UI/TelemetryHistory.cs`: in-memory, per-session sample history, limited to 60 distinct received timestamps.

## 2. UI files modified

- `NetworkAdmin.Server/ServerDashboardForm.cs`: replaced the plain TabControl-based form with the sidebar shell, header, shared selection and seven in-window pages. Continues to call the existing runtime for commands, snapshots and shutdown.

## 3. Backend files modified and exact reason

Only `NetworkAdmin.Server/ServerRuntime.cs` was changed, in its presentation snapshot projection:

- Carry the already-received network-interface `Description` into `InterfaceSnapshot`.
- Identify common virtual adapters by name/description, including VMware and vEthernet.
- Choose a non-virtual IPv4 interface, preferring one with a default gateway, for the primary IP/MAC summary.
- Keep all reported adapters in the detailed table. If there is no eligible physical interface, do not label a virtual interface as physical; show the TCP peer IP with other primary-interface fields unavailable.

The Client's existing network information service already reports Up adapters. No new wire fields, Client changes or local Server network probes were needed. `ClientManager`, `ServerCommandDispatcher`, `TcpHelloServer`, Shared messages/protocol, Client services and project dependencies were not changed.

Validation files also changed: `Validation/Phase10.Validation.csproj` links the presentation history class; `Validation/Program.cs` adds seven adapter/history regression checks.

## 4. Dashboard

- Navy sidebar with NET ADMIN / Server branding; Dashboard, Clients, IP Scanner, Ping Tool, Monitoring, Settings and About.
- Live listener status and configured endpoint, distinct connected-agent count, clock/date and observed listener uptime.
- Blue/green/purple/red summary cards for known agents this run, connected agents, most recent scan host count and last Ping average RTT.
- Selected Client and Network Information panel beside live CPU/RAM gauges and history.
- Recent Scan and Ping panels below, with real results and executable remote commands.
- No fabricated example values. Empty values show `--`; no-client states remain explicit.

Known-agent totals count unique ClientIds observed during this Server process. Selection and command routing continue to use SessionId, so multiple sessions for one identity remain independently selectable.

## 5. Clients

A dark fleet table shows status, hostname, primary IPv4/MAC, CPU, RAM and last-seen time. Selecting a row updates global selection for every page. Offline rows show unavailable live CPU/RAM, while the detail and monitoring panels retain and label the last received data. Full Client ID / Session ID remain secondary detail fields, with tooltips for truncated text. A separate adapter table includes virtual adapters and their descriptions.

## 6. Scanner

The dedicated page shows the selected agent, subnet input, Scan Network button, busy state, result table and summary. Dashboard has Scan Now; if no subnet is available, it opens the Scanner page for entry. Subnet prefill uses the selected primary interface. Columns are IP Address, Hostname, MAC Address, Status and Managed.

The existing runtime sends ScanRequest to the selected agent. Results use the existing managed-host matcher and update as agents connect/disconnect. Tables identify the originating agent/subnet and completion time. Old results remain explicitly labeled while a new operation runs or fails. No Server scanner was introduced.

## 7. Ping

Dashboard and dedicated Ping page share target text, commands and results. Summary tiles show Sent, Received, Lost, Packet Loss and Average RTT. A dark table shows individual attempt status and response time; results are no longer a raw multiline block. Each result identifies its original agent/session, target and completion time even if global selection changes. Busy/error status is visible, and previous results are labeled when retained.

Execution still uses the existing remote four-request Ping flow. No Ping implementation was moved to the Server.

## 8. Monitoring

Custom-drawn CPU/RAM gauges and a dual-line chart read only selected-agent snapshots. The cache retains up to 60 samples per SessionId, adding a sample only when the received telemetry timestamp advances. Polling never invents additional points. Histories remain independent across selection changes and are released when sessions leave the runtime's retained snapshot set.

Disconnected values/history are retained but labeled OFFLINE / STALE DATA. Connected clients with telemetry older than ten seconds are also labeled stale. The chart says **last 60 samples**, not last 60 seconds: actual time coverage depends on the client's telemetry interval.

## 9. Encoding

Removed the Server form's malformed dash/ellipsis sequences by replacing the old UI text. New source strings use safe ASCII or explicit Unicode escapes for symbols. A search of Server C# sources found no `\u00e2`/`\u00c3` mojibake markers or replacement characters. No unrelated legacy files were cleaned.

## 10. Responsive layout

Uses nested TableLayoutPanels with percentage columns, Dock-based page/card contents, a shared global selector and a fixed sidebar. Pages switch in the main panel; no independent tool windows open. Content viewports grow with the window and scroll vertically below their minimum usable heights, keeping dense cards readable on compact displays rather than squeezing them together. Tables use proportional columns and their own scrolling. DPI autoscaling and buffered custom controls are enabled; no external UI framework was added.

The design targets 1366x768 and 1920x1080. Following the user's screenshot, the actual control tree was rendered off-screen at 96 DPI (1366x768 and 1920x1080) and 192 DPI (2732x1536 and 3200x1904). Layout assertions passed and the 1366/96 and 3200/192 renders were visually inspected. Interactive window behavior remains unverified.

## 11. Build results

All required builds were executed sequentially and **PASS**, with zero warnings and zero errors:

1. `dotnet build NetworkAdmin.Shared/NetworkAdmin.Shared.csproj`
2. `dotnet build NetworkAdmin.Client/NetworkAdmin.Client.csproj`
3. `dotnet build NetworkAdmin.Server/NetworkAdmin.Server.csproj`
4. `dotnet build NetworkAdminTool.csproj`
5. `dotnet build NetworkAdminTool.slnx -m:1`

## 12. Tests performed

`dotnet run --project NetworkAdmin.Server/Validation/Phase10.Validation.csproj`: **36 checks PASS**.

Includes the existing real loopback TCP/ICMP, multi-session telemetry, command routing, cross-session rejection, disconnect cleanup, malformed-peer isolation and shutdown tests. Seven new checks verify physical/gateway adapter preference, virtual-adapter retention and exclusion from primary selection, history capacity, duplicate-timestamp/disconnect behavior, per-session separation and eviction.

Interactive/manual validation: **NOT TESTED**. The Windows computer-use helper was unavailable. Subsequent off-screen render validation uses the actual WinForms controls without showing a window or starting networking; it does not replace interactive testing. The user's reported manual Phase 10 baseline remains separate from these tests.

### DPI correction after the user's screenshot

- Defer form autoscaling until the complete control tree has been constructed. Previously fonts grew at 200% DPI while controls added after the initial scale retained unscaled row widths/heights.
- Give horizontal TableLayoutPanels an explicit percentage-height row. Their default AutoSize row could preserve an oversized preferred height and make cards overflow the assigned dashboard rows after scaling.
- Use proportional dashboard rows (14% summary, 39% client/monitoring, 47% results), a 210-unit sidebar, DPI-aware gauge/chart painting and an initial window size calculated in logical pixels.
- Add drawn monitor/shield/network/pulse tiles to the four summary cards. Preserve literal ampersands in labels.
- Add `Validation/Layout/Layout.Validation.csproj` and `Program.cs`: render the real control tree off-screen and assert that titles/navigation fit and visible cards stay inside their allocated rows.
- Run `dotnet run --project NetworkAdmin.Server/Validation/Layout/Layout.Validation.csproj -- --96` for baseline DPI, and run without `--96` for native display DPI. Generated PNGs are under ignored `artifacts/layout/`.
- Four size/DPI cases passed. Renders at 1366x768/96 DPI and 3200x1904/192 DPI were inspected. No backend execution or routing changes were made for this correction.
- The correction's default-output build could not replace `NetworkAdmin.Server.exe` because the user's running Server locked it (MSB3021/MSB3027). Validation therefore uses a separate `-p:OutputPath=bin/DpiCheck/` output. Close the old Server before rebuilding/running the normal Debug output; its open window will not pick up source changes.

## 13. Known limitations

- Interactive use, populated-data screenshots and monitor-to-monitor DPI changes still require manual validation. Off-screen renders can differ from native window chrome and native ComboBox rendering.
- Compact windows intentionally use vertical scrolling; the entire dense dashboard need not fit on one laptop screen at once.
- Physical-interface classification uses reported names/descriptions, not a new hardware-capability field. Unknown or unusually renamed virtual adapters may evade the heuristic.
- Settings is an information page showing current configuration and startup options; it does not edit connection settings or restart the server.
- Existing limitations remain: in-memory history/results, /24-/30 dashboard scans, percentage-only RAM telemetry and no protocol-level individual remote scan cancellation.
- No Client, Shared protocol, networking service or legacy project cleanup was performed. Phase 11 was not started.
