# Task 00 — Remote contracts và protocol inventory

Tài liệu này là inventory/audit contract. Phần PBL4 hiện tại giữ nguyên; phần mở rộng bên dưới là contract đã chuẩn hóa trong Task 01. Task 07 đã bổ sung ChatService; AI inference vẫn chưa triển khai.

## 1. PBL4 contract hiện tại

### 1.1. Transport và session

| Kênh | Mặc định | Framing | Bảo vệ | Owner |
|---|---:|---|---|---|
| Control | TCP `5000` | JSON UTF-8, một envelope mỗi dòng `\n` | TLS 1.2/1.3 nếu chạy thật; Client pin SHA-256 certificate | `ServerConnection` / `TcpHelloServer` |
| Screen | TCP `5001` | Hello JSON line sau TLS, sau đó binary `ScreenFrameProtocol` | TLS cùng certificate, one-time capability | `ScreenStreamingService` / `ClientScreenShareService` |
| Chat | TCP `5000` control channel | JSON UTF-8 envelope `RemoteChatMessage` | TLS 1.2/1.3, SendLock, session identity | `ChatService` / `ClientChatService` |
| Security event | Chưa có | — | — | — |

`ClientId` được suy ra ổn định từ machine/domain/user ở Client nhưng `HelloMessage` vẫn là self-reported identity. `SessionId` do Server cấp trong `HelloAck`. Screen request và control request được ràng buộc thêm bởi request ID/capability và Client consent.

### 1.2. Message ID đang dùng

Không được tái sử dụng các ID sau khi mở rộng contract:

| ID | `MessageType` | Payload/ý nghĩa |
|---:|---|---|
| 1 | `Hello` | `HelloMessage(ClientId, ClientName)` |
| 2 | `HelloAck` | `HelloAckMessage(Accepted, SessionId, Message)` |
| 3 | `SystemStats` | telemetry Client |
| 4 | `NetworkInfo` | snapshot adapter/network |
| 5 | `PingRequest` | request ping từ Server |
| 6 | `PingResult` | response ping từ Client |
| 7 | `ScanRequest` | request scan |
| 8 | `ScanResult` | response scan |
| 9 | `RemoteAdministrationCommand` | command có `AuthorizationToken` |
| 10 | `RemoteAdministrationResult` | lifecycle Accepted/Preparing/Completed/Failed/... |
| 11 | `RemoteAdministrationCancel` | hủy command |
| 12 | `ScreenShareRequest` | Server yêu cầu consent |
| 13 | `ScreenShareConsent` | Client trả consent |
| 14 | `ScreenShareStop` | kết thúc screen session |
| 15 | `ScreenChannelReady` | server accept screen channel |
| 16 | `ScreenShareStart` | capability + screen port |
| 17 | `ControlRequest` | Server yêu cầu control consent |
| 18 | `ControlConsent` | Client trả control consent |
| 19 | `ControlStop` | thu hồi control |
| 20 | `ControlInputEvent` | mouse/keyboard event đã bind session/request |

### 1.3. Screen frame wire format

Mỗi frame là binary big-endian:

```text
int32 jpegLength       (offset 0, >0, <= 12 MiB)
int64 sequence         (offset 4, >=0)
byte  kind             (offset 12: 1 = full, 0 = delta)
int32 x                (offset 13)
int32 y                (offset 17)
int32 width            (offset 21)
int32 height           (offset 25)
byte[jpegLength] jpeg  (offset 29, phải có SOI/EOI JPEG)
```

Full frame phải có `x=y=0`; width/height có thể ghi kích thước bitmap. Delta phải có width/height dương; compositor Server kiểm tra `x + width` và `y + height` nằm trong full frame. `MaxScreenDimension=7680`, `MaxFrameBytes=12 MiB`. Sequence phải liên tục tại viewer; full frame được gửi định kỳ để phục hồi hình ảnh, còn PBL4 hiện không có resume giữa các session.

### 1.4. Input contract

`RemoteInputEventMessage` chứa `ClientId`, `SessionId`, `ScreenRequestId`, `ControlRequestId`, `Kind`, `X/Y`, button, wheel delta và virtual key. `RemoteInputEventPolicy` chỉ chấp nhận coordinate `0..10000`, wheel `-1200..1200`, button enum hợp lệ và key `1..255`; Server chỉ gửi khi control active, Client/session/request khớp và control stream TLS-authenticated.

## 2. RemoteX contract hiện tại

### 2.1. Initial handshake

Mỗi trong ba socket gửi bằng Java `DataOutputStream.writeUTF` chuỗi CSV:

```text
username,password,role,screenWidth,screenHeight,connectType
```

`connectType` là `screen`, `control` hoặc `chat`. Relay trả `writeUTF("true,...")` hoặc `writeUTF("false,...")`. Password đi qua relay và được so sánh bằng chuỗi trong `Session`; không có TLS/certificate pinning trong source.

RemoteX mặc định dùng `5002/6002/7002`, khác PBL4 `5000/5001`. Đây là protocol độc lập; không được đưa trực tiếp vào `MessageType` PBL4.

### 2.2. Session/control

Relay ghép socket bằng `Session`, gửi `START_SESSION` hoặc `RESTART_SHARESCREEN` trên control. Control event là chuỗi UTF với mã âm:

| Mã | Ý nghĩa | Dữ liệu chính |
|---:|---|---|
| -1 | press mouse | button |
| -2 | release mouse | button |
| -3 | press key | keyCode |
| -4 | release key | keyCode |
| -5 | move mouse | button, x ratio, y ratio |
| -7 | mouse wheel | rotation |
| -8 | drag | button, x ratio, y ratio |

`ReceiveEvent` dùng Java `Robot`; `ControlEvent` scale theo kích thước panel. Không có version, request ID, capability, message length limit hay explicit authorization trên từng event.

### 2.3. Screen frame

Sharer gửi trước `int32 screenWidth`, `int32 screenHeight`, sau đó:

```text
boolean isFullFrame
int32 sequence

// full
int32 jpegLength
byte[jpegLength] jpeg

// delta
int32 x, y, width, height
int32 jpegLength
byte[jpegLength] jpeg
```

`ShareScreen` dùng block 16×16, ngưỡng full frame `0.35`, JPEG quality mặc định `0.7`, FPS `20`. PBL4 đã có cùng semantics nhưng header an toàn hơn và có giới hạn/bounds validation.

### 2.4. Chat

Chat dùng `DataOutputStream.writeUTF(message)` và `DataInputStream.readUTF()` trên socket riêng. Timestamp và tên người gửi chỉ được render ở Swing UI; wire không có message ID, UTC timestamp, sender identity, size policy hoặc delivery status.

### 2.5. Monitoring/AI

Luồng RemoteX:

```text
Sysmon/Winlogbeat NDJSON
  -> NdjsonTailer
  -> FeatureExtractor
  -> Preprocessor
  -> ONNX IsolationForest
  -> AlertService (JOptionPane)
```

Model contract được resource mô tả là 69 feature, input `float_input`, output `label` và `scores`. Feature groups gồm event/time, label encoding, rule-based, frequency/destination port, 30 image TF-IDF và 20 command-line TF-IDF; `scaler.json` có mean/scale cho 69 phần tử. Các artifact đi kèm: `feature_metadata.json`, `feature_names.json`, `label_encoders.json`, `tfidf_image.json`, `tfidf_cmdline.json`, `scaler.json`, `onnx_metadata.json` và model ONNX.

Source có hai điểm cần xác minh trước khi port:

- README nói CRITICAL/HIGH tự ngắt nhưng `AlertService` chỉ tạo dialog; không có callback tới relay/session để disconnect.
- `onnx_metadata.json` mô tả ngưỡng model khác với các threshold severity hard-code trong `AnomalyDetector`; không được coi là cùng một policy nếu chưa kiểm định bằng fixture.

## 3. Contract đã chuẩn hóa trong Task 01

### 3.1. Nguyên tắc

1. Giữ control stream, TLS, `ClientManager` và `ScreenStreamingService` hiện có; không thêm `remote.server` relay.
2. Tất cả message mới dùng envelope hiện tại (`Type`, `RequestId`, `Data`) và được validation giới hạn kích thước.
3. ID mới dùng dải `21..33`; ID `1..20` không thay đổi.
4. Chat và security event gắn `SessionId`/`ClientId`; chat có `MessageId` để ledger chống gửi trùng.
5. Security alert là event/policy do Server quản lý; auto disconnect vẫn chưa triển khai và sau này phải gọi session manager có audit/revoke.

### 3.2.1. Message ID mới

| ID | `MessageType` | Contract |
|---:|---|---|
| 21 | `RemoteSessionRequest` | `RemoteSessionRequestMessage` |
| 22 | `RemoteSessionResponse` | `RemoteSessionResponseMessage` |
| 23 | `RemoteSessionStart` | `RemoteSessionStartMessage` |
| 24 | `RemoteSessionStop` | `RemoteSessionStopMessage` |
| 25 | `RemoteSessionStatus` | `RemoteSessionStatusMessage` |
| 26 | `RemoteSessionError` | `RemoteSessionErrorMessage` |
| 27 | `RemoteSessionCancel` | `RemoteSessionCancelMessage` |
| 28 | `RemoteChatMessage` | `RemoteChatMessage` |
| 29 | `SecurityEvent` | `SecurityEventMessage` |
| 30 | `SecurityAlert` | `SecurityAlertMessage` |
| 31 | `MonitoringStatus` | `MonitoringStatusMessage` |
| 32 | `MonitoringStart` | `MonitoringStartRequestMessage` |
| 33 | `MonitoringStop` | `MonitoringStopRequestMessage` |

Không có ID trùng trong enum; `RemoteChatMessage` được Server/Client dispatcher xử lý từ Task 07, còn security/monitoring message vẫn chỉ là schema.

### 3.2.2. DTO đã thêm

| Contract đề xuất | Trường tối thiểu | Ghi chú |
|---|---|---|
| `RemoteChatMessage` | `ClientId`, `SessionId`, `SenderId`, `ReceiverId?`, `MessageId`, `SenderRole`, `Content`, `TimestampUtc` | Content tối đa 4.096 ký tự; payload tối đa 16 KiB; timestamp UTC |
| `SecurityEventMessage` | `ClientId`, `SessionId`, `EventId`, `DetectionTimestampUtc`, source/process/user/command fields | Có giới hạn field/command line và payload 32 KiB; chưa thu thập log |
| `SecurityAlertMessage` | `AlertId`, `EventId`, `Severity`, `AnomalyScore`, `Confidence`, timestamp, process/user, policy flag | `AnomalyScore` là điểm model; `Confidence` là xác suất/độ tin cậy chuẩn hóa `0..1`, không trộn hai giá trị |
| `MonitoringStatusMessage` | `ClientId`, `SessionId`, `State`, `LastEventTimestampUtc?`, `Detail`, `ModelVersion` | Chỉ contract, chưa có monitoring runtime |
| `MonitoringStartRequestMessage` | Client/session/requester, model version, enable alerts, timestamp | Chưa có authorization handler |
| `MonitoringStopRequestMessage` | Client/session/requester, reason, timestamp | Chưa có stop handler |
| `RemoteSession*Message` | Client/session, kind/state, consent/result, timeout/cancel/error fields | Bao phủ request, accept/reject, start/stop, status, timeout và cancellation |

`RemoteProtocolContract` cung cấp version `1`, capability names, envelope/type validation, identifier/session validation, payload limits và `ValidateHello/ValidateHelloAck`. `MessageIdLedger` là helper bounded để phát hiện duplicate ID; ownership/lifecycle của remote session vẫn để Task 02.

### 3.2.3. Backward compatibility

`HelloMessage.ProtocolVersion` và `.Capabilities` cùng các field tương ứng trong `HelloAckMessage` là additive. Khi peer cũ không gửi field, peer mới dùng default version `1` và capability rỗng; peer cũ bỏ qua field JSON không biết. Các DTO/message cũ và ID `1..20` không đổi. Unknown message type không được coi là hợp lệ bởi `ValidateEnvelope`; việc dispatcher xử lý/ghi log unknown type vẫn thuộc các task lifecycle sau.

## 4. Không tương thích cần tránh

- Không map RemoteX command codes âm vào `RemoteInputEventKind`; PBL4 đã có enum/policy và request binding riêng.
- Không map RemoteX `username/password` thành `HelloMessage`; identity, authentication và authorization của PBL4 phải được tách rõ.
- Không dùng port `5002/6002/7002` hoặc tạo relay thứ hai chỉ để giữ nguyên Java topology.
- Không để screen, control, chat và security module cùng ghi trực tiếp vào một stream mà không qua owner/send lock.
- Không tái sử dụng message ID `1..20`, không đưa model/NDJSON payload vào frame screen hoặc JSON line không có giới hạn.
- Không tự resume remote control sau reconnect; phải có consent và capability mới, trừ khi Task 13/08 định nghĩa rõ một chính sách an toàn hơn.

## 5. Task 02 — Remote Session Manager hiện tại

### 5.1. Ownership và state

`ScreenStreamingService` tiếp tục là owner duy nhất cho screen/control remote session; `ClientManager` chỉ quản lý connection registry. `PendingScreenShare` là state holder của session hiện hữu, không có manager thứ hai và không có TCP/relay protocol mới.

Lifecycle được audit và ghi log theo thứ tự hợp lệ:

```text
Requested -> AwaitingConsent (PendingConsent) -> Accepted -> Starting -> Active
                                                                  |
                                      Rejected/TimedOut/Canceled/Failed
                                                                  v
                                                          Stopping -> Closed
```

`Stopped` trong contract vẫn là trạng thái tương đương kết thúc cũ; runtime mới ghi `Closed` sau cleanup để phân biệt terminal cleanup với lý do reject/timeout/cancel/failure đã được audit trước đó.

### 5.2. Identity, authorization và quyền

Session snapshot gắn `ClientId`, `SessionId`, `AdminId`, `RequestId`, `Kind`, `GrantedPermission` và `ExpiresAtUtc`. `RemoteSessionAuthorizationPolicy` kiểm tra:

- Admin ID hợp lệ và admin token khớp bằng constant-time comparison.
- Permission `ViewOnly`, `Control` hoặc `ViewAndControl` đủ cho operation.
- Authorization chưa hết hạn.

Token chỉ được dùng trong Server runtime; không đưa token vào screen/control payload. Screen request bắt đầu ở view-only; control vẫn cần TLS, Client consent, capability/session/request binding và chỉ có một control slot trên cùng Client session.

### 5.3. Cleanup và audit

Consent reject, cancellation, timeout, explicit stop, Client stop, disconnect/reconnect và Server shutdown đều giải phóng `_clientSlots`, `_requests`, control state, stream và expiry cancellation. Session lifetime mặc định là 30 phút, có thể cấu hình bằng `--remote-session-timeout-seconds` hoặc `NETWORKADMIN_REMOTE_SESSION_TIMEOUT_SECONDS` trong khoảng 60 giây–24 giờ.

State transition được ghi vào audit log hiện có `Logs/remote-administration.log` với `event=remote-session`, `admin`, `client`, `session`, state và detail đã sanitize. Không ghi authorization token.

Task 02 không thay đổi capture, frame protocol, JPEG/full-delta streaming, remote input, chat, UI, TLS handshake hoặc message ID `1..33`.

## 6. Task 03 — Client Screen Capture

PBL4 đã có `ScreenCaptureService` và `ClientScreenShareService`; không port lại `ShareScreen.java`. So với RemoteX, PBL4 giữ cùng ý tưởng 20 FPS, JPEG và block delta 16×16 nhưng có lifecycle/cancellation/TLS/capability riêng của PBL4.

### Audit kết quả

- Capture dùng virtual screen bounds trong Per-Monitor DPI V2, thay vì chỉ primary display; hỗ trợ màn hình phụ và virtual desktop có tọa độ âm.
- `ScreenMaxFps`, `ScreenMaxWidth`, `ScreenMaxHeight` và `ScreenJpegQuality` được clamp; frame JPEG vẫn chịu giới hạn `ScreenFrameProtocol.MaxFrameBytes`.
- Capture chỉ bắt đầu sau request hợp lệ, consent accepted, `ScreenShareStart` và capability hợp lệ; không capture khi chỉ mới có request hoặc khi session chưa active.
- `CancellationToken` liên kết với control session/active share; `PeriodicTimer` giữ nhịp capture; bitmap hiện tại và bitmap trước đó được dispose trong mọi đường thoát.
- Bổ sung cancellation check trong capture/pixel-diff, retry có delay cho lỗi capture/pixel processing và clear cancellation reference khi stop. Không thay đổi thuật toán delta hoặc wire protocol.

### Giới hạn manual

DPI scaling, màn hình phụ, resize/thay đổi display topology, workstation lock, Secure Desktop/UAC và desktop không có quyền capture cần kiểm tra trên Windows interactive session. `BitBlt` có thể trả lỗi hoặc ảnh đen khi desktop bị khóa; code ghi lỗi, dừng/retry capture theo lifecycle nhưng không bypass giới hạn bảo mật Windows.

## 7. Task 04 — Screen Streaming và Frame Protocol

PBL4 giữ nguyên wire format hiện có và không port relay/socket topology của RemoteX. Header binary là 29 byte big-endian: JPEG length, sequence, full/delta kind và rectangle. RemoteX có cùng semantics full/delta, block 16×16, threshold 35% và 20 FPS; PBL4 bổ sung giới hạn payload, JPEG marker validation, TLS/capability và compositor bounds.

### Audit kết quả

- Client producer capture → diff → JPEG → `WriteFrameAsync` tuần tự; không có queue frame phía sau nên write backpressure dừng producer thay vì tích tụ frame cũ.
- Sequence bắt đầu từ 0 và chỉ tăng khi packet đã được gửi. Full frame đầu tiên và periodic full frame mỗi giây tạo keyframe/resync; delta chỉ chứa bounding box thay đổi.
- Reader giới hạn JPEG tối đa 12 MiB, dimension tối đa 7.680, đọc đủ payload và reject header/JPEG/truncated data không hợp lệ trước khi decode.
- `RemoteViewerPresenter` yêu cầu sequence liên tục, reject delta trước full, kiểm tra header với decoded size và kiểm tra delta nằm trong composite bounds. Full frame periodic được chấp nhận như resync khi sequence hợp lệ; sequence gap cùng stream bị reject thay vì ghép trạng thái không xác định.
- Reconnect/stop tạo lifecycle mới; presenter mới bắt đầu lại từ full frame. Control/heartbeat dùng control channel riêng, không bị frame decode pipeline chặn.

Đã bổ sung test full/delta/full round-trip, invalid delta header, cancellation, full-frame resync và sequence gap. Chưa đổi message ID hoặc wire protocol.

## 8. Task 05 — Admin Screen Viewer

Viewer Admin hiện có là WinForms `RemoteViewerForm` dùng `RemoteViewerPresenter`; không tạo cửa sổ hoặc framework UI mới.

- Presenter decode JPEG và composite full/delta trên worker thread, kiểm tra sequence liên tục, yêu cầu full frame trước delta, kiểm tra kích thước/bounds và phát hiện lỗi stream.
- Form cập nhật UI bằng `BeginInvoke`, giữ tối đa một frame pending và dispose frame cũ khi bị thay thế. Bitmap hiện tại, pending bitmap, timer và session được giải phóng khi dừng/đóng viewer.
- State hiển thị gồm `WAITING`, `CONNECTING`, `STREAMING`, `DISCONNECTED`, `ERROR`; quyền xem được kế thừa từ session đã xác thực/được cấp bởi flow request, consent, capability và authorization hiện có.
- Fit mode dùng `Zoom` để giữ đúng tỷ lệ và xử lý vùng letterbox; Actual Size dùng `Normal`/AutoScroll. Việc này không đổi remote input mapping.
- Reconnect là lifecycle mới với request/consent mới, không tự động resume phiên cũ; stream mới bắt đầu bằng full frame. Không thay đổi frame wire protocol, TLS, remote input, chat hoặc AI.

Đã bổ sung test bounds giữ tỷ lệ ảnh và presenter disposal; test hiện có đã bao phủ composition, frame ordering, full-frame resync, invalid payload, cancellation và cleanup. Manual DPI, multi-monitor, resize thực tế, desktop lock/UAC và UI interaction vẫn cần chạy trên Windows interactive session.

## 9. Task 06 — Remote Mouse/Keyboard Control

Remote input tiếp tục dùng `MessageType.ControlInputEvent` hiện có và không tạo TCP/relay mới.

- Các enum hiện có `MouseMove=1`, `MouseClick=2`, `MouseWheel=3`, `KeyDown=4`, `KeyUp=5` được giữ nguyên. Bổ sung additive `MouseDown=6`, `MouseUp=7` cho button hold và drag/drop.
- Double-click không cần wire event riêng: WinForms phát hai chuỗi `MouseDown`/`MouseUp`, để Windows nhận đúng semantics double-click. Modifier/chord dùng thứ tự `KeyDown`/`KeyUp` với virtual-key code hợp lệ `1..255`.
- Mọi event phải khớp `ClientId`, `SessionId`, `ScreenRequestId`, `ControlRequestId`, normalized coordinate `0..10.000`, button/wheel/key payload hợp lệ và control consent đang active. Server còn kiểm tra TLS certificate, authorization permission, active session và client identity trước khi gửi.
- Client giới hạn tối đa 240 event/giây; mouse move có throttle khoảng 60 Hz. `WindowsRemoteInputService` dùng `SendInput` với absolute/virtual-desktop flags, không vượt UAC/Secure Desktop.
- Client lưu trạng thái phím và mouse button đang giữ. Stop control, revoke consent, cancellation và disconnect gọi `ReleaseAll()` để phát key-up/mouse-up, tránh kẹt input.
- Viewer map Zoom/letterbox về normalized coordinate, bắt mouse down/up/move/wheel và keyboard event khi control active. Khi release xảy ra ngoài vùng ảnh, dùng tọa độ hợp lệ cuối cùng để vẫn giải phóng button.

Đã bổ sung test policy cho mouse down/up và identity mismatch, rate limiter, coordinate mapping vùng letterbox và integration sequence drag plus key events. Manual test DPI, multi-monitor/negative virtual origin, drag/drop, double-click, modifier chord và UAC/Secure Desktop vẫn cần chạy trên Windows interactive session.

## 10. Task 07 — Remote Chat

Chat dùng `MessageType.RemoteChatMessage = 28` trên control TCP channel hiện có. Không có socket/port/relay riêng.

- Envelope JSON line được bảo vệ bởi TLS hiện tại và tuần tự hóa qua `ClientManager.SendLock`/`ServerConnection` write lock.
- `RemoteChatMessage` chứa `ClientId`, `SessionId`, `SenderId`, `ReceiverId`, `MessageId`, `SenderRole`, UTF-8 `Content` và `TimestampUtc`. `MessageId` đồng thời là `RequestId` để phát hiện duplicate.
- Payload giới hạn 4.096 ký tự/16 KiB; timestamp wire là UTC và UI render local. `RemoteChatRateLimiter` giới hạn 60 message/phút mỗi session.
- Server xác nhận message `ClientId/SessionId/SenderId` theo `ClientSessionInfo`; sender khai báo sai bị reject. Server chuẩn hóa sender và timestamp trước khi phát event.
- Admin send bắt buộc `RemoteSessionAuthorizationPolicy` với token, AdminId, quyền ViewOnly trở lên và thời hạn còn hiệu lực; payload/envelope sai kiểu bị loại bỏ an toàn.
- Admin chat gắn với `IRemoteChatSession` của screen viewer; Client chat service gắn với từng `ServerConnection`. UI WinForms tối thiểu có ở hai phía.

## 11. Task 08 — Reconnect, Heartbeat và Lifecycle

- `ClientRuntime` giữ reconnect backoff 1–10 giây; mỗi connection tạo mới `ClientChatService`, ledger và rate limiter. Tin chat cũ không được replay.
- Telemetry định kỳ là heartbeat hiện hữu; Client giới hạn telemetry interval tối đa 5 giây. Server monitor mỗi 5 giây và đóng session có `LastSeen` quá 30 giây, configurable 10–300 giây.
- Khi disconnect, Server dọn chat ledger/rate limiter, screen/control session và pending commands; Client dừng service/reader rồi dispose connection trước khi backoff. Registry đánh dấu session inactive và dispose cả stream/socket khi disconnect hoặc thay thế cùng ClientId.
- Mọi chat send tiếp tục kiểm tra cả `ClientId` và `SessionId`; session cũ bị từ chối sau reconnect. Screen/control vẫn không tự resume và cần consent/capability mới.

Đã bổ sung test chat round-trip, duplicate/sender spoof/oversized input, old-session rejection, stale registry cleanup, Server restart/new session, unready heartbeat filtering, session replacement và concurrent disconnect. Runtime test vẫn phụ thuộc môi trường testhost.

## 12. Task 09 — Windows Security Monitoring và Sysmon Log Collector

- `SecurityEventCollector` là worker native C#/.NET độc lập ở Client; không tự động cài Sysmon, Winlogbeat, driver hoặc Windows service.
- `SecurityEventSourceKind.WindowsEventLog` dùng `EventLogWatcher`/`EventLogQuery` trên log đã tồn tại; `SecurityEventSourceKind.Ndjson` dùng polling async cho file `winlogbeat_output*.ndjson`. `Auto` chọn nguồn theo dữ liệu hiện hữu.
- NDJSON tailer có bounded read, partial-line buffer, strict UTF-8, malformed-line skip, file rotation, truncation detection và checkpoint chỉ lưu filename/byte offset. Không ghi raw JSON, command line hoặc username vào diagnostic log.
- Collector dùng bounded `Channel<SecurityEventMessage>` với backpressure, `CancellationToken`, status `Starting/Running/Stopped/Faulted` và graceful shutdown. Mọi output được giới hạn/sanitize rồi kiểm tra bằng `SecurityMonitoringContractPolicy.ValidateEvent`.
- Fixture tests bao phủ parse event, malformed JSON, partial line, rotation, checkpoint offset, source unavailable và cancellation. Không có feature extraction, ONNX inference, alert policy hay auto-disconnect trong Task 09.

## 13. Task 10 — Security Feature Extraction

- `SecurityFeatureInputParser`/`SecurityFeatureExtractor` tách khỏi `SecurityEventJsonParser` của collector để giữ parity với RemoteX `FeatureExtractor.java`: chỉ đọc `winlog.event_id` và `winlog.event_data` cho 9 raw fields.
- Missing values dùng default tương đương Java; malformed JSON bị reject; Unicode được giữ trong raw text và Windows path được normalize basename nhất quán.
- `SecurityFeatureSchema.ModelFeatureNames` giữ đúng 69 phần tử theo `feature_names.json`/`onnx_metadata.json`, không thêm feature mới: 19 scalar/time/rule/frequency + 30 image TF-IDF + 20 command-line TF-IDF.
- Golden fixture/test kiểm tra output raw và schema order. Chưa triển khai label encoding, TF-IDF, StandardScaler, ONNX inference hoặc UI alert; các phần đó thuộc Task 11/12.

## 14. Trạng thái Task 11 — AI Preprocessing và ONNX Isolation Forest

Task 11 bổ sung pipeline AI ở Client nhưng chưa bật alert hoặc tự động ngắt phiên.

- Artifacts thật của RemoteX được đóng gói tại `NetworkAdmin.Client/Assets/SecurityModel/`. `feature_names.json` và `onnx_metadata.json` xác nhận đúng 69 feature theo thứ tự: 19 scalar/time/rule/frequency, 30 image TF-IDF và 20 command-line TF-IDF.
- Model contract đã xác nhận: input `float_input` shape `[1,69]`, element `float`; outputs `label` (`int64`) và `scores` (`float`), opset 12, Isolation Forest, 100 estimators, contamination 0.05. Không có semantic `model_version` trong metadata; SHA-256 model fingerprint được dùng làm version anchor.
- `SecurityPreprocessor` giữ semantics RemoteX: Java Monday-based weekday, local timezone, unknown label fallback `0`, suspicious rules, per-user/process counters, cleaning/tokenization, TF-IDF với L2 normalization và StandardScaler. Không thêm feature.
- `SecurityInferenceEngine` fail-closed khi artifact/model contract sai, kiểm tra finite vector, serialize inference, hỗ trợ timeout/cancellation và dispose session/output. `label == -1` là anomaly; `scores` là raw anomaly score. `Confidence` luôn null/status chưa calibration, không được hiểu là xác suất.
- Test có golden parity prefix, artifact contract, missing-value/finiteness, real-model load/inference, invalid vector và batch benchmark. Runtime ONNX còn phụ thuộc native runtime asset tương thích với package/OS; không coi việc có DLL ngoài app là deployment hợp lệ.

## 15. Trạng thái Task 12 — Security Alert và Auto Disconnect

- `SecurityAlertService` phía Server nhận `MessageType.SecurityAlert = 30` trên control channel hiện có; không tạo port, relay hoặc protocol mới.
- Alert gắn `ClientId`, `SessionId`, `AlertId`, `EventId`, severity, anomaly score, timestamp, process, user và `DetectionReason`. `Confidence` là nullable vì Task 11 chưa calibration; không coi anomaly score là probability.
- `SecurityAlertPolicy` phân loại anomaly theo label + score: critical `<= -0.5`, high `<= -0.25`, medium `<= -0.1`, low `< 0`; non-anomaly là `Normal`. Threshold critical/warning được đối chiếu với ONNX metadata, các band còn lại là policy vận hành.
- Service dedupe theo AlertId và fingerprint, rate-limit theo authenticated session, lưu active alert với auto-dismiss cấu hình được, và ghi audit cho accepted/suppressed/auto-disconnect outcome.
- Event hiển thị được đưa qua bounded queue/background dispatcher; subscriber lỗi không làm dừng đường nhận alert. `ServerRuntime.SecurityAlertReceived` là hook cho UI Task 14.
- Auto-disconnect mặc định tắt. Khi quản trị viên bật `--security-auto-disconnect` hoặc `NETWORKADMIN_SECURITY_AUTO_DISCONNECT=true`, chỉ alert anomaly đạt ngưỡng configured mới gọi `ScreenStreamingService.StopForSessionAsync` đúng `SessionId`; guard race ngăn gọi lặp cho cùng session.
- Không thay đổi model, ONNX artifacts, streaming, remote input, TLS hoặc consent. Collector/inference chỉ phát alert khi có publisher; Task 12 không tự động bật thu thập Windows log.

## 16. Trạng thái Task 13 — Security Hardening

- `JsonLineProtocol` validate message type, request ID, payload và giới hạn frame trước khi dispatch; unknown type/null payload/oversized frame bị reject fail-closed.
- TLS dùng certificate pinning SHA-256 ở Client cho cả main channel và screen channel; token authorization yêu cầu giá trị không rỗng, permission hợp lệ, expiry còn hiệu lực và so sánh constant-time.
- Session-bound checks hiện diện ở screen capability, control input, chat và security alert; replay được chặn bằng one-time capability hoặc `MessageIdLedger`, còn input/chat/alert đều có rate limit và payload bounds.
- Control listener và screen listener có bounded active connection slots, mặc định lần lượt 128 và 64; cấu hình qua `--max-client-connections`, `--max-screen-connections` hoặc environment tương ứng.
- Negative tests mới nằm trong `RemoteTransportSecurityTests`; các test trước đó tiếp tục bao phủ TLS pin mismatch, capability replay, forged sender/session, invalid input, duplicate chat/alert và authorization expiry.

### Security findings còn lại

- **P1:** Hello vẫn nhận ClientId tự khai báo trên server-authenticated TLS; chưa có client certificate/managed identity proof. Đây là deployment/identity provisioning gap, không tự thay đổi wire protocol trong Task 13.
- **P2:** self-signed certificate và `NoCheck` revocation phù hợp pinning LAN nhưng cần quy trình phân phối fingerprint, rotation và ACL certificate store.
- **P2:** admin token có thể nằm trong command line/environment; cần secret store ở deployment production. Không token/password nào được ghi vào audit log.
- **P2:** Windows interactive/manual cases (Event Log ACL, locked desktop, UAC/Secure Desktop, DPI/multi-monitor) chưa được kiểm chứng trong test host.

Task 13 không đổi message ID, field semantics hiện có, TLS protocol version, screen wire format, remote input, chat, AI artifacts hoặc authorization flow.

## 17. Trạng thái Task 14 — UI quản trị

- Không có message ID, DTO hoặc wire protocol mới trong Task 14. Dashboard tái sử dụng `ClientSnapshot`, `RemoteSession`, screen/control/chat và `SecurityAlertMessage` hiện có.
- Nút bật/tắt auto-disconnect gọi runtime policy hiện hữu; thay đổi được kiểm tra bằng admin identity/token và ghi audit. Policy vẫn mặc định tắt.
- UI chỉ hiển thị alert stream nhận được từ server. Contract hiện tại chưa cung cấp trạng thái live riêng của collector/inference worker, nên không suy diễn trạng thái đó từ việc dashboard đang chạy.
- Không thay đổi serialization, TLS, authentication/authorization, screen frame protocol hoặc input message semantics.
