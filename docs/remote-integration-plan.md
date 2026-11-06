# Task 00 — Audit và kế hoạch tích hợp RemoteX vào PBL4

Ngày audit: 2026-11-04  
Phạm vi: chỉ Task 00. Chưa sửa business logic, chưa đổi message ID và chưa triển khai Task 01–15.

## 1. Nguồn đã kiểm tra

| Nguồn | Phạm vi | Kết quả |
|---|---|---|
| PBL4, commit `969f297` (`dev`) | `NetworkAdmin.Client`, `NetworkAdmin.Server`, `NetworkAdmin.Shared`, test project, README/PHASE docs, project files và lịch sử commit remote/screen | Đã có remote administration, screen sharing và remote input; chưa có chat hoặc AI security monitoring |
| `C:\Users\PC_HP\Downloads\REMOTE_X.zip` | `remote` client, `remote.server` relay, Java source, resources AI và git history | RemoteX có 3 socket relay riêng, screen/control/chat và monitoring AI local |
| PBL4 working tree | `NetworkAdmin.Client/Properties/launchSettings.json` đang modified từ trước | Không sửa file này |

PBL4 không có Java source, không có project reference tới RemoteX và không có dấu vết namespace/package `RemoteX`. Việc tích hợp trước đây được thể hiện bằng phần screen streaming có cùng ý tưởng 16×16 change blocks, JPEG delta/full và được README PBL4 ghi rõ là theo RemoteX; không có bằng chứng về việc copy nguyên source Java.

## 2. Kiến trúc hiện tại

### PBL4

- `NetworkAdmin.Client` kết nối tới `NetworkAdmin.Server` bằng một TCP control channel, JSON line protocol và TLS pinning.
- `NetworkAdmin.Server` bind control port mặc định `5000`; `ScreenStreamingService` mở screen port mặc định `5001`.
- `NetworkAdmin.Shared.Messages.MessageType` hiện dùng ID `1..20`; đã có các message cho hello, telemetry, ping/scan, remote administration, screen consent/start/stop và remote control.
- Screen channel là TCP/TLS riêng, được cấp one-time capability sau khi Client consent. Frame binary dùng `ScreenFrameProtocol`.
- `ClientRuntime` có reconnect cấp kết nối control với backoff 1–10 giây. Remote screen/control đang bị kết thúc khi session TCP hiện tại mất; không có resume phiên remote.
- `ServerCommandDispatcher`, `ClientManager` và `SendLock` đã tập trung việc định tuyến response và tuần tự hóa ghi vào control stream.
- UI Server đã chọn Client từ danh sách hiện có và mở `RemoteViewerForm`; UI Client đã có consent screen/control và nút dừng.

### RemoteX

- `remote` là Java Swing application; `remote.server` là relay server Java.
- Relay mở ba cổng độc lập, mặc định `5002` (screen), `6002` (control), `7002` (chat), có thể đổi bằng `.env`.
- Mỗi channel bắt đầu bằng `DataOutputStream.writeUTF` một chuỗi CSV gồm username, password, role, width, height và loại channel. Relay ghép sáu socket vào một `Session` rồi dùng `RelayThread` hai chiều.
- Sharer gửi `START_SESSION` qua control. Screen gửi width/height, sau đó gửi full/delta frame; viewer gửi command chuột/bàn phím qua control; chat gửi chuỗi UTF trực tiếp.
- Monitoring chạy ở máy sharer: Sysmon/Winlogbeat → NDJSON tailer → feature extraction → preprocessing → Isolation Forest ONNX → Swing alert.

## 3. Kết luận audit và lỗi/khoảng thiếu chính

| Mức | Phát hiện | Bằng chứng | Hướng xử lý |
|---|---|---|---|
| Blocker | Chưa có source RemoteX trước khi nhận archive; hiện đã có archive nhưng RemoteX và PBL4 dùng protocol/kiến trúc khác nhau | RemoteX Java `ClientHandler`, `Session`, `RelayThread`; PBL4 `TcpHelloServer`, `ScreenStreamingService` | Không port/copy relay; chuẩn hóa contract trên protocol PBL4 ở Task 01 |
| Cao | Chat hoàn toàn chưa có trong PBL4 | Không có `Chat` message type/DTO/service; `MessageType` kết thúc ở `ControlInputEvent = 20` | Thiết kế một chat contract có session/request/message ID ở Task 01, triển khai Task 07 |
| Cao | AI security monitoring hoàn toàn chưa có trong PBL4 | Không có Sysmon/NDJSON/ONNX dependency/module; `NetworkAlert` hiện chỉ là alert nội bộ dashboard | Tách collector, feature pipeline, inference và alert policy; không đưa model vào transport ở Task 09–12 |
| Cao | RemoteX tự nhận auto-disconnect nhưng source không thực hiện | `AlertService` chỉ hiện `JOptionPane`; `MonitoringManager` giữ socket và chỉ đóng khi stop, không có callback disconnect | Phải có policy/alert event và lệnh stop session rõ ràng; kiểm thử trước khi bật auto disconnect |
| Cao | RemoteX truyền password và dữ liệu điều khiển qua relay socket không có TLS/pinning | `MainStart` dùng `Socket`; `ClientHandler` đọc CSV; `RelayThread` chuyển byte thô; README nói encrypted nhưng source không thiết lập TLS | Chỉ tái sử dụng TLS/capability của PBL4; không đưa password RemoteX vào protocol mới |
| Trung bình | PBL4 nhận diện Client chủ yếu bằng `ClientId` tự khai báo; admin token hiện phục vụ remote administration | `HelloMessage`, `ClientManager.Register`, `RemoteAdministrationCommandMessage` | Task 13 cần quyết định credential/pairing/role cho screen, control, chat và security actions |
| Trung bình | Reconnect PBL4 không khôi phục screen/control | `ClientRuntime` tạo session mới; `TcpHelloServer` fail pending và `ScreenStreamingService.StopForSessionAsync` | Task 08 phải định nghĩa resume hay luôn yêu cầu consent mới; mặc định an toàn là không tự khôi phục control |
| Trung bình | RemoteX screen viewer không kiểm tra đầy đủ bounds/độ dài delta trước khi cấp phát buffer | `ReceiveScreen.processFullFrame/processDeltaFrame` cấp phát theo `dataLength`; không có giới hạn protocol | PBL4 đã có giới hạn 12 MiB, dimension và compositor bounds; giữ nguyên, bổ sung test độc lập |
| Trung bình | RemoteX reconnect relay khởi động lại nhiều `RelayThread` theo từng socket và dùng sleep/grace period | `Session.handleViewerReconnection`, `restartRelayForConnection`, `RelayThread.handleDisconnection` | Không mang mô hình này sang PBL4; quản lý một `RemoteSession` có state machine và cancellation |
| Thấp | RemoteX có singleton monitoring, `stop()` không interrupt worker và lifecycle socket phụ thuộc UI | `MonitoringManager`, `NdjsonTailer`, `MainStart` | Khi port sang .NET phải dùng `CancellationToken`, `IAsyncDisposable` và ownership rõ ràng |

## 4. Chức năng PBL4 đã có và có thể tái sử dụng

- Screen capture: `ClientScreenShareService` + `ScreenCaptureService`, JPEG, FPS/quality/resolution config, full frame và delta frame.
- Screen transport: `ScreenFrameProtocol` có header binary big-endian, sequence, full/delta rectangle, JPEG validation và giới hạn kích thước.
- Viewer/compositor: `RemoteViewerPresenter` kiểm tra sequence liên tục, full frame đầu tiên, delta bounds, decode và FPS.
- Remote input: `RemoteInputEventMessage`, `RemoteInputEventPolicy`, `WindowsRemoteInputService`; có normalized coordinate 0–10.000, mouse, wheel, key down/up và release-all.
- Consent/lifecycle: `IScreenShareConsent`, `ScreenShareUi`, capability one-time, dừng share và dừng control.
- Security/transport: TLS 1.2/1.3, SHA-256 certificate pinning, control write lock, admin token và audit log cho remote administration.
- Session/discovery: `ClientManager`, `ClientSessionInfo`, `ServerRuntime` và dashboard snapshot; đây là nguồn Client duy nhất cần dùng cho tích hợp.

## 5. Kế hoạch task theo dependency

| Task | Nội dung | Phụ thuộc | Phạm vi module dự kiến | Gate hoàn thành |
|---|---|---|---|---|
| 00 | Audit PBL4/RemoteX, baseline, matrix, contract notes | — | `docs/` | Tài liệu này, matrix và contracts; PASS/PARTIAL theo baseline |
| 01 | Chuẩn hóa DTO, message protocol, interface | 00 | `NetworkAdmin.Shared`, interface Client/Server | Đã thực hiện ở Task 01; ID không trùng, schema/version rõ, contract tests |
| 02 | Remote Session Manager | 01 | Server application/infrastructure | **Đã triển khai — PARTIAL**: một owner/session, state transition, authorization, timeout, audit và cleanup test |
| 03 | Hoàn thiện capture Client | 01, 02 | Client screen sharing | **ALREADY IMPLEMENTED + HARDENED**: capture/resize/cancellation/resource cleanup test |
| 04 | Streaming, compression, delta | 02, 03 | Shared protocol + Client/Server runtime | **ALREADY IMPLEMENTED + AUDITED**: frame round-trip, gap, bounds, cancellation và backpressure test |
| 05 | Viewer tại Admin | 04 | Server presenter/form | **ALREADY IMPLEMENTED + HARDENED**: aspect-preserving fit, background decode, lifecycle cleanup, ordered full/delta tests; interactive UI manual test remains environment-dependent |
| 06 | Mouse/keyboard | 01, 02, 05 | Shared policy + Client input + viewer | **ALREADY IMPLEMENTED + HARDENED**: drag state, normalized mapping, authorization/rate-limit and release-all tests |
| 07 | Remote Chat | 01, 02 | Shared messages + Server/Client UI | **IMPLEMENTED**: control-channel ChatService, session identity, duplicate/limit tests and minimal UI |
| 08 | Reconnect/heartbeat/lifecycle | 02–07 | ClientRuntime/Server session manager | **IMPLEMENTED + HARDENED**: telemetry heartbeat, stale timeout, reconnect cleanup and old-session rejection |
| 09 | Sysmon/Security Log Collector | 01, 02 | Client monitoring infrastructure | **IMPLEMENTED — PARTIAL RUNTIME VALIDATION**: native Event Log/NDJSON collector, bounded queue, checkpoint/rotation và fixture tests |
| 10 | Feature Extraction | 09 | Shared/Client security pipeline | **IMPLEMENTED — RAW/GOLDEN TESTED**: parser/extractor/schema 69 feature names; TF-IDF/scaler/ONNX để Task 11 |
| 11 | Preprocess + ONNX inference | 10 | Client security module | **PARTIAL** — real artifacts, 69-feature parity, model contract and real inference tests; deployment/native-runtime packaging must be preserved |
| 12 | Alert + auto disconnect | 02, 08, 11 | Server alert state/policy + session manager | Severity policy, history, safe stop, no false implicit disconnect |
| 13 | Auth/authorization | 01, 02, 06, 07, 12 | Shared security policy + transport | TLS, role/capability, revoke, audit, negative tests |
| 14 | UI tích hợp PBL4 | 05–07, 12 | Existing dashboard/forms | Không tạo Client list/relay/UI quản trị thứ hai |
| 15 | Integration/regression/performance | 01–14 | Test/validation/docs | LAN test, reconnect, resource cleanup, FPS/bandwidth và regression |

## 9. Trạng thái Task 02

Task 02 đã mở rộng owner session hiện có `ScreenStreamingService`/`PendingScreenShare`; không tạo `RemoteSessionManager` thứ hai.

- Bổ sung state transition có audit cho `Requested`, `AwaitingConsent` (`PendingConsent`), `Accepted`, `Starting`, `Active`, `Stopping`, `Closed` và các terminal/error state tương đương.
- Bổ sung `AdminId`, quyền view-only/control, thời hạn phiên, cancellation token và cleanup khi timeout/disconnect.
- Authorization của Server runtime kiểm tra `AdminId`, admin token, permission và expiry trước khi yêu cầu session; Client consent và TLS/capability hiện có vẫn được giữ nguyên.
- Giữ một control slot trên mỗi Client session; race khi yêu cầu control thứ hai bị từ chối.
- Tái sử dụng `RemoteAdministrationAuditLog` cho audit event của remote session.
- Bổ sung unit tests cho authorization, state transition và control race.

**PARTIAL** — source và test compile đã hoàn thành; cần xác nhận test runtime. Test host đã có blocker được ghi ở Task 00/01 và sẽ được kiểm tra lại trong Task 02.

## 10. Trạng thái Task 03

**ALREADY IMPLEMENTED + HARDENED** — PBL4 đã có capture lifecycle hoạt động trong `NetworkAdmin.Client`; Task 03 chỉ bổ sung các điểm an toàn còn thiếu, không viết lại capture.

Bằng chứng audit:

- `ScreenCaptureService` đã dùng `GetSystemMetrics(SM_*VIRTUALSCREEN)` trong per-monitor DPI V2 context, nên bao phủ virtual desktop/màn hình phụ và tọa độ âm.
- Kích thước được giới hạn tối đa `3840×2160`; JPEG quality `30..85`; FPS `1..20`; full/delta 16×16 và periodic full frame đã tồn tại.
- `ClientScreenShareService` chỉ tạo capture loop sau request hợp lệ, Client consent và `ScreenShareStart` có capability; loop dùng linked `CancellationToken`, `PeriodicTimer`, dispose `Bitmap` hiện tại/trước đó và release input khi kết thúc.
- Task 03 bổ sung cancellation check trong capture/pixel-diff, retry 1 giây cho lỗi capture/pixel processing và dọn reference cancellation khi `Stop()`.
- Không thay đổi screen frame protocol, JPEG/delta algorithm, TLS, UI, chat, AI hoặc remote input.

Unit tests bổ sung cho cancellation, encoded payload độc lập khỏi source bitmap và consent cancellation giải phóng approval. Kiểm thử DPI, multi-monitor, desktop bị khóa, UAC/Secure Desktop và quyền desktop vẫn cần manual test trên Windows session tương ứng.

## 11. Trạng thái Task 04

**ALREADY IMPLEMENTED + AUDITED** — PBL4 đã có full frame, delta frame, sequence, JPEG compression, validation và viewer compositor; Task 04 không cần đổi wire protocol.

- Client gửi tuần tự, không có frame queue tích tụ; `WriteFrameAsync` được await nên TCP backpressure dừng producer thay vì tăng latency.
- Frame đầu tiên và periodic keyframe là full frame; delta dùng block 16×16, threshold 35%, sequence chỉ tăng khi packet thực sự được gửi.
- `ScreenFrameProtocol` giới hạn JPEG 12 MiB, dimension 7.680, header 29 byte, đọc đủ payload và kiểm tra JPEG marker.
- Viewer kiểm tra sequence liên tục, full-frame header/decoded dimensions, delta bounds và dispose bitmap cũ/patch/composite.
- Reconnect tạo session/presenter mới và bắt đầu lại bằng full frame; sequence gap trong cùng stream bị reject để không composite trạng thái không xác định. Full frame periodic được kiểm thử như resync khi sequence hợp lệ.

Đã bổ sung test round-trip full/delta/full, invalid delta header, cancellation, periodic full-frame resync và sequence gap. Không có benchmark runtime đáng tin cậy trong môi trường này vì vstest testhost bị chặn socket trước khi chạy test; FPS cấu hình mặc định là 20 và không có thay đổi thuật toán chưa đo đạc.

Task 01 là gate bắt buộc. Nếu không chốt contract và ownership trước, việc đưa chat/AI vào sẽ tạo thêm protocol/session song song và tái diễn xung đột cũ.

## 6. Baseline kỹ thuật

Đã chạy ngày 2026-11-04:

```text
dotnet build NetworkAdminTool.slnx --no-restore -m:1 -v:minimal
Build succeeded. 0 Warning(s), 0 Error(s).
```

Các project chính và test project đều build được khi chạy tuần tự (`NetworkAdmin.Shared`, `NetworkAdmin.Client`, `NetworkAdmin.Server`, `NetworkAdmin.Shared.Tests`, `NetworkAdmin.Client.Tests`). `dotnet test`/`dotnet vstest` khởi động nhưng test host không trả kết quả trong giới hạn khoảng 30 giây của môi trường audit, kể cả filter vào test policy đơn giản; chưa có kết quả PASS/FAIL đáng tin cậy để dùng làm bằng chứng. Đây là vấn đề kiểm thử môi trường/runner cần xử lý trước Task 15, không tự ý sửa trong Task 00.

## 7. Trạng thái Task 00

**PARTIAL — audit source và lập kế hoạch đã hoàn thành; baseline build PASS.**  
Lý do không PASS tuyệt đối: test host hiện không hoàn tất, và cần dùng các contract ở Task 01 để xác nhận chi tiết mapping triển khai. Không có business logic nào được sửa trong task này.

## 8. Trạng thái Task 01

Task 01 đã được triển khai riêng trong `NetworkAdmin.Shared` và `NetworkAdmin.Shared.Tests`:

- Giữ nguyên message ID `1..20`; thêm ID `21..33` cho remote session, chat và security monitoring.
- Bổ sung DTO/policy cho remote session request/response/start/stop/status/error/cancel.
- Bổ sung DTO/policy cho chat, giới hạn nội dung 4.096 ký tự và payload 16 KiB.
- Bổ sung DTO/policy cho security event/alert và monitoring status/start/stop; anomaly score và confidence là hai trường độc lập.
- Bổ sung protocol version/capabilities dạng field additive trong `Hello` và `HelloAck`.
- Bổ sung envelope validation và `MessageIdLedger` chống duplicate message ID trong phạm vi ledger.
- Không triển khai ChatService, AI inference, Sysmon, session manager, UI, TLS thay đổi hoặc business routing.

**PARTIAL** — contract code và test compile đã hoàn thành; build PASS. Test runtime vẫn bị test host treo trước khi kết nối được với testhost, nên chưa thể tuyên bố unit test PASS. Diagnostic cho thấy `vstest.console` dừng tại `TestRequestSender.WaitForRequestHandlerConnection` sau khi đã launch `testhost.exe`; không có test case nào được thực thi. Đây là blocker của test runner/môi trường, cần tách thành công việc kiểm thử riêng.

## 12. Trạng thái Task 05

**ALREADY IMPLEMENTED + HARDENED** — PBL4 đã có Admin viewer WinForms, presenter, compositor full/delta và state lifecycle; Task 05 chỉ sửa lỗi fit mode làm méo tỷ lệ và bổ sung test logic/lifecycle.

- `RemoteViewerPresenter` giữ thứ tự sequence, yêu cầu full frame trước delta, kiểm tra kích thước/bounds và giải phóng bitmap cũ; decode/composite chạy ngoài UI thread bằng `Task.Run`.
- `RemoteViewerForm` nhận frame qua single-slot pending buffer, loại bỏ/dispose frame chờ cũ để tránh tích tụ latency; cập nhật PictureBox bằng `BeginInvoke` và dispose bitmap khi thay thế/đóng viewer.
- Fit mode dùng `PictureBoxSizeMode.Zoom` và mapping input hiện có tính letterbox; Actual Size giữ `Normal`/AutoScroll. Không thay đổi remote input semantics.
- State UI hiện có `WAITING`, `CONNECTING`, `STREAMING`, `DISCONNECTED`, `ERROR`; dừng viewer gọi `StopAsync`, unsubscribe và dispose session/bitmap/timer.
- Viewer chỉ chạy trên `IScreenViewerSession` đã được server cấp từ flow request/consent/capability hiện có. Reconnect không tự resume: request mới và consent mới tạo lifecycle mới, rồi bắt đầu lại bằng full frame.
- Bổ sung test aspect-ratio bounds và presenter disposal; các test trước đó đã bao phủ full/delta, ordering, resync, invalid payload, cancellation và error cleanup.

Manual test DPI, resize, nhiều màn hình, desktop bị khóa/UAC và thao tác UI thực tế vẫn cần chạy trên Windows interactive session. Trong môi trường audit, helper UI không khởi tạo được và vstest testhost bị chặn socket nên chưa có kết quả runtime.

## 13. Trạng thái Task 06

**ALREADY IMPLEMENTED + HARDENED** — Remote input PBL4 tái sử dụng control channel/TLS/capability/consent hiện có. Task 06 bổ sung đúng phần còn thiếu cho drag và cleanup, không tạo protocol/socket riêng.

- Giữ nguyên `RemoteInputEventKind` ID `1..5`; thêm additive `MouseDown=6` và `MouseUp=7` để biểu diễn button hold/drag. Double-click dùng hai chuỗi down/up tự nhiên của WinForms, không thêm wire event trùng nghĩa.
- `RemoteInputEventPolicy` kiểm tra identity `ClientId/SessionId/ScreenRequestId/ControlRequestId`, tọa độ normalized `0..10.000`, button, wheel và virtual-key; phía Client chỉ xử lý TLS-authenticated, active, consented control request.
- Thêm rate limit tối đa 240 input event/giây ở Client; mouse move vẫn có throttle riêng khoảng 60 Hz.
- `WindowsRemoteInputService` track pressed keys và mouse buttons; `ReleaseAll()` nhả cả hai khi stop, consent revoke, session cancellation hoặc disconnect. Không bypass UAC/Secure Desktop.
- Viewer bắt `MouseDown/MouseUp`, giữ tọa độ cuối để vẫn phát release khi con trỏ rời vùng ảnh; `KeyPreview`/KeyDown/KeyUp hỗ trợ modifier và chord thông thường. Fit/Zoom mapping giữ letterbox và virtual desktop normalized coordinates.
- Bổ sung policy/rate-limit/drag sequence/coordinate mapping tests. Manual test thực tế trên Windows vẫn cần kiểm tra multi-monitor, DPI, drag/drop, double-click và quyền hệ điều hành.

## 14. Trạng thái Task 07 — Remote Chat

**IMPLEMENTED** — Chat dùng `MessageType.RemoteChatMessage` trên control channel JSON/TLS hiện có; không tạo port hoặc relay mới.

- `ChatService` phía Server và `ClientChatService` phía Client dùng `MessageId` làm `RequestId`, UTF-8 JSON line, giới hạn 4.096 ký tự/16 KiB payload, duplicate ledger và rate limit 60 message/phút.
- Server chỉ chấp nhận message có `ClientId`, `SessionId`, `SenderId` khớp authenticated `ClientSessionInfo`; sender spoof bị loại bỏ. Server chuẩn hóa sender/timestamp trước khi phát event.
- Server Admin chat được gắn với screen remote session hiện hữu qua `IRemoteChatSession`; Client chat được tạo mới theo từng TCP session. Không queue/replay tin cũ khi reconnect.
- UI WinForms tối thiểu đã có ở viewer Admin và Client; UX nâng cao, history persistence, delivery/read receipt vẫn để Task 14.
- Test compile/integration bao phủ round-trip, identity, sender spoof, duplicate MessageId, oversized content, rate limit, Admin authorization và old-session rejection. Parser envelope fail-closed với `messageId` sai kiểu; runtime test phụ thuộc testhost môi trường.

## 15. Trạng thái Task 08 — Reconnect/Heartbeat/Lifecycle

**IMPLEMENTED + HARDENED** — ClientRuntime tiếp tục reconnect với backoff 1–10 giây; mỗi lần kết nối tạo `ServerConnection`, `ClientChatService` và command handler mới.

- Telemetry hiện hữu 2 giây được dùng làm heartbeat; interval cấu hình Client bị giới hạn tối đa 5 giây.
- Server có stale-session monitor mỗi 5 giây, timeout mặc định 30 giây, đóng socket stale để kích hoạt cleanup/reconnect. Có thể cấu hình `--heartbeat-timeout-seconds` hoặc `NETWORKADMIN_HEARTBEAT_TIMEOUT_SECONDS` trong khoảng 10–300 giây.
- Disconnect/reconnect giải phóng chat ledger/rate limiter, command tasks, screen/control session và không gửi lại tin chat cũ vào session mới.
- Chat/Admin send kiểm tra `ClientId + SessionId`; session cũ sau disconnect bị từ chối. Screen/control resume policy không thay đổi và không tự resume.
- Bổ sung test stale session registry cleanup, heartbeat bỏ qua session chưa ready, reconnect sau Server restart với SessionId mới, thay thế session cùng ClientId, concurrent disconnect idempotency và old-session chat rejection. Registry khi thay thế/disconnect đánh dấu session inactive và dispose cả stream/socket; test runtime vẫn bị giới hạn bởi testhost socket blocker.

## 16. Trạng thái Task 09 — Windows Security Monitoring và Sysmon Log Collector

**IMPLEMENTED — PARTIAL RUNTIME VALIDATION** — Đã bổ sung collector native C#/.NET ở Client; không tự động cài Sysmon, Winlogbeat, driver hoặc Windows service.

- `SecurityEventSourceKind.WindowsEventLog` dùng `EventLogWatcher`/`EventLogQuery` trên log đã tồn tại. `SecurityEventSourceKind.Ndjson` dùng polling async cho file `winlogbeat_output*.ndjson`; `Auto` chọn nguồn theo dữ liệu hiện hữu.
- NDJSON có bounded read, partial-line buffer, strict UTF-8, malformed-line skip, file rotation, truncation detection và checkpoint chỉ lưu filename/byte offset.
- Collector dùng bounded queue, `CancellationToken`, status `Starting/Running/Stopped/Faulted` và graceful shutdown. Output được giới hạn/sanitize rồi kiểm tra bằng `SecurityMonitoringContractPolicy.ValidateEvent`.
- Không ghi raw JSON, command line hoặc username vào diagnostic log. Không có feature extraction, ONNX inference, alert policy hoặc auto-disconnect trong Task 09.
- Fixture tests bao phủ parse event, malformed JSON, partial line, rotation, checkpoint offset, source unavailable và cancellation. Build PASS; runtime test vẫn phụ thuộc testhost môi trường.

## 17. Trạng thái Task 10 — Security Feature Extraction

**IMPLEMENTED — RAW/GOLDEN TESTED** — Đã đọc `FeatureExtractor.java`, `Preprocessor.java`, `feature_names.json`, `feature_metadata.json`, `label_encoders.json`, TF-IDF artifacts và `onnx_metadata.json` của RemoteX.

- Raw input giữ đúng 9 trường Java dùng: `event_code`, `timestamp`, `user`, `image_path`, `process_name`, `parent_image`, `parent_name`, `command_line`, `dest_port`.
- `SecurityFeatureInputParser` đọc `winlog.event_id`/`winlog.event_data`, giữ default Java (`event_code=0`, `user=Unknown`, `parent_name=-`, text rỗng, port `0`) và reject malformed JSON.
- `SecurityProcessName` chuẩn hóa cả `\\`/`/` Windows path, lấy basename và lower-case invariant; không thêm feature ngoài pipeline.
- `SecurityFeatureSchema` cố định 69 feature names/order: 19 scalar/time/rule/frequency, 30 image TF-IDF và 20 command-line TF-IDF. Task 10 chưa tính TF-IDF/scaler và chưa chạy ONNX.
- Golden fixture không chứa username/path thực tế. Khác biệt có chủ đích: Java fallback timestamp lỗi về thời điểm hiện tại trong `Preprocessor`, còn C# raw extractor giữ chuỗi timestamp để Task 11 quyết định policy deterministic; Java `toLowerCase()` phụ thuộc locale, C# dùng invariant lower-case.

## 18. Trạng thái Task 11 — AI Preprocessing và ONNX Isolation Forest

**PARTIAL — artifact/preprocessing tương thích, runtime ONNX đã kiểm chứng trên máy phát triển; deployment cần giữ native asset**.

- Đã đưa đúng artifacts thật từ RemoteX vào `NetworkAdmin.Client/Assets/SecurityModel/`: feature names/metadata, label encoders, hai TF-IDF files, scaler, ONNX metadata và `isolation_forest_model.onnx`.
- `SecurityPreprocessor` giữ đúng 69 feature/order, local-time features, label encoding, suspicious rules, counters, TF-IDF/L2 normalization và StandardScaler. Golden test so sánh prefix 19 giá trị với phép tính tham chiếu độc lập trên cùng fixture.
- Model contract: input `float_input` là `float[1,69]`; outputs `label` (`int64`) và `scores` (`float`); opset 12, Isolation Forest, 100 estimators, contamination 0.05. Metadata không có semantic `model_version`; SHA-256 ONNX fingerprint là version anchor.
- `SecurityInferenceEngine` có load/contract validation, vector validation, timeout/cancellation, serialized inference và async disposal. `label == -1` là anomaly; `scores` là raw anomaly score; `Confidence` để null vì chưa có calibration.
- Build PASS; targeted Task 11 tests PASS 6/6, gồm real-model inference. Full suite còn regression ngoài phạm vi Task 11 ở screen streaming/handshake và cancellation assertion. Native runtime phải được ship cùng app; không dựa vào DLL ngoài System32.
- Không triển khai alert UI, auto-disconnect, Sysmon, feature mới hoặc thay đổi streaming/remote input.

## 19. Trạng thái Task 12 — Security Alert và Auto Disconnect

**IMPLEMENTED — policy/service/test hoàn thành; UI hiển thị đầy đủ để Task 14**.

- `SecurityAlertService` dùng control channel/message ID hiện có, xác minh alert thuộc authenticated `ClientSessionInfo`, không tin session/client ID sai từ payload.
- Severity được tính lại từ `IsAnomaly` và anomaly score. Dedupe, rate limit, active store, auto-dismiss và audit log đều nằm trong Server service.
- Alert event chạy qua bounded background queue; auto-disconnect không dùng UI notification thay thế session termination.
- Policy auto-disconnect mặc định OFF; khi bật chỉ gọi `ScreenStreamingService.StopForSessionAsync` đúng session đạt ngưỡng Critical, có guard chống race và false-positive.
- Targeted Task 12 tests và Task 11 tests đều PASS; full suite sau wiring PASS (Shared 71/71, Client 70/70), không thay đổi AI model/streaming/input.

## 20. Trạng thái Task 13 — Security Hardening

**PARTIAL — hardening tương thích ngược đã hoàn thành; còn P1 identity binding và các rủi ro triển khai cần xử lý ở deployment/task riêng.**

- TLS main/control channel và screen channel dùng TLS 1.2/1.3 với certificate SHA-256 pinning ở Client. Certificate callback fail-closed khi thiếu certificate hoặc fingerprint không hợp lệ; không dùng thuật toán mã hóa tự viết.
- `RemoteSessionAuthorizationPolicy` và Client remote-command authorization dùng constant-time token comparison, yêu cầu token khác rỗng, kiểm tra permission/expiry; screen capability là random one-time value và bị vô hiệu sau khi bind.
- Chat, input và security alert đều bind với authenticated transport session; server không dùng `SenderId`, `ClientId` hoặc `SessionId` tự khai báo để thay thế `ClientSessionInfo`. Input còn yêu cầu control consent active, request IDs đúng và có rate limit; chat/alert có duplicate ledger/rate limit.
- `JsonLineProtocol` nay validate message type, request ID, payload presence và giới hạn frame cả khi send lẫn receive; malformed/unknown envelope bị fail-closed trước dispatch. Bổ sung giới hạn kết nối đang xử lý cho control listener (128 mặc định) và screen listener (64 mặc định), có thể cấu hình bằng args/env.
- Negative security tests mới kiểm tra unknown type, null payload, oversized frame, empty authorization token và replay message ID. Existing tests tiếp tục bao phủ spoofed sender, wrong session, invalid input, capability replay, TLS fingerprint mismatch, duplicate chat/alert và auto-disconnect đúng session.
- Dependency audit bằng `dotnet list package --vulnerable --include-transitive` cho Client/Server/Shared không phát hiện package vulnerable từ các source hiện tại.

### Findings còn lại

- **P1 — identity binding của Client:** Hello hiện xác nhận `ClientId` do agent khai báo trên kênh TLS server-authenticated; chưa có client certificate hoặc provisioning/allow-list cryptographic proof để chứng minh máy sở hữu ClientId. Không tự đổi protocol trong Task 13 vì sẽ phá deployment tương thích. Khuyến nghị task triển khai riêng: cấp client certificate/managed identity và migration handshake.
- **P2 — certificate provisioning:** Server tự tạo self-signed certificate trong CurrentUser\\My; pinning chống MITM khi fingerprint được phân phối ngoài kênh an toàn, nhưng revocation/rotation và ACL store cần quy trình deployment được quản trị viên kiểm soát.
- **P2 — secret handling:** admin token vẫn nhận từ command-line hoặc environment để giữ compatibility. Command-line có thể lộ qua process inspection; production nên dùng secret store/ACL-protected configuration và không đưa token vào audit/log.
- **P2 — Windows/manual:** UAC/Secure Desktop, Event Log permission, Sysmon availability, DPI/multi-monitor và locked desktop chưa thể chứng minh trong non-interactive test host.

Không có finding P0. Không sửa screen capture/streaming algorithm, remote input semantics, chat behavior, AI model/preprocessing hoặc UI ngoài hardening transport/config.

## 21. Trạng thái Task 14 — Remote Administration UI

**PARTIAL — đã hoàn thiện các flow UI có service thật; manual validation bị giới hạn bởi môi trường desktop.**

- `ServerDashboardForm` giữ nguyên framework WinForms và các màn hình quản trị hiện có: chọn Client từ danh sách, mở `RemoteViewerForm`, yêu cầu view/control, dừng phiên, mở chat theo session và hiển thị trạng thái viewer/reconnect/error.
- Dashboard bổ sung khu vực Monitoring: trạng thái alert stream, danh sách tối đa 100 alert gần nhất và nút bật/tắt auto-disconnect. Nút gọi `IServerRuntime.SetAutoDisconnectEnabled`; policy mặc định OFF, yêu cầu admin identity/token hợp lệ và có confirmation khi bật. Thay đổi policy được audit, không ghi secret.
- Viewer tiếp tục dùng service thật của PBL4 cho screen/control/chat; lifecycle hiện có giải phóng subscription, bitmap/stream và input state khi stop/disconnect. Không thay đổi screen capture, streaming, input protocol hoặc chat transport.
- Alert callback được marshal về UI thread; dashboard giữ lịch sử in-memory bounded. Khi đóng form, callback được unsubscribe để tránh giữ form hoặc rò rỉ resource.
- Smoke test đã launch được Server Dashboard và xác nhận trạng thái Running/TLS. Không hoàn tất được click-through chọn Client/viewer/chat/control/alert trong CUA vì foreground window không cung cấp geometry input và bị chặn bởi desktop focus; DPI, multi-monitor, locked desktop, UAC/Secure Desktop và test end-to-end giữa hai máy vẫn cần manual test trên Windows tương tác.

### Task 14 còn lại

- Protocol không có field riêng để báo trạng thái collector/AI monitoring đang chạy; UI hiện hiển thị trạng thái alert stream, không giả mạo trạng thái collector.
- Cần manual acceptance test với một Client thật để xác nhận toàn bộ flow, DPI/multi-monitor và cleanup sau reconnect/stop.

## 22. Trạng thái Task 15 — Integration, Regression và Performance

**PARTIAL — automated validation PASS; Windows LAN/manual performance chưa thực hiện được.**

- Solution build PASS với 0 warning/0 error. Full suite PASS 142/142; hang diagnostics với timeout 30 giây cho cả testhost cũng PASS, nên blocker test runner của Task 00 không còn tái hiện.
- Security filter PASS 27/27; integration/lifecycle/streaming filter PASS 18/18. Validation harness `Phase10.Validation` sau khi đồng bộ compile include và assertion authorization hiện PASS 79/79; Layout validation PASS.
- Validation harness ban đầu fail vì assertion cũ chờ “not configured”, trong khi Client fixture không có token nên contract hardening trả “Rejected/authorization failed”. Chỉ sửa harness assertion, không đổi runtime behavior.
- Benchmark thực đo duy nhất trong môi trường này là SecurityPreprocessor: 1.000 transforms/65.914 ms = 0.065914 ms/event. FPS, end-to-end latency, JPEG bandwidth, CPU/RAM và ONNX inference latency trên LAN/desktop: **NOT TESTED**.
- Đã thêm test plan, hướng dẫn Sysmon, cấu hình artifact model và checklist Server/Admin/2 Windows Clients vào README và `docs/remote-test-plan.md`. Feature matrix được cập nhật trạng thái Task 15.
- Không tối ưu production vì chưa có bottleneck đo được; không thay đổi streaming, input, chat, AI artifacts, TLS hay message protocol.
