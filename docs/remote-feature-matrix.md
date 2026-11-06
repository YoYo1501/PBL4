# Task 00 — Remote feature matrix

Legend: **Có** = đã có và có thể tái sử dụng; **Một phần** = có nền tảng nhưng chưa đủ yêu cầu RemoteX; **Chưa có** = chưa có module/contract; **Không nên port** = RemoteX có nhưng mâu thuẫn với kiến trúc/bảo mật PBL4.

## A. Remote Desktop

| Chức năng | RemoteX source | PBL4 hiện tại | Đánh giá | Module đích / task | Vấn đề cần xử lý |
|---|---|---|---|---|---|
| Chụp màn hình | `remote/src/main/java/server/ShareScreen.java` — `Robot.createScreenCapture` | `ClientScreenShareService`, `ScreenCaptureService` | **ALREADY IMPLEMENTED + HARDENED** | Task 03 | Virtual desktop/DPI, cancellation, cleanup và retry đã được audit; desktop lock/UAC vẫn manual |
| Truyền realtime | `ShareScreen.run` + `remote.server/RelayThread.java` | `ScreenStreamingService` + TLS screen port | Có | 04 | Không dùng relay 3 cổng; cần test backpressure, cancellation và reconnect |
| Hiển thị viewer | `client/ReceiveScreen.java` | `RemoteViewerForm`, `RemoteViewerPresenter` | **ALREADY IMPLEMENTED + HARDENED** | 05 | WinForms viewer có state, background decode, ordered full/delta compositor, aspect-preserving Zoom và lifecycle cleanup; DPI/multi-monitor/UI thực tế còn cần manual |
| Full frame | `ShareScreen.sendFullFrame` | `ScreenFramePacket(IsFullFrame=true)` | **ALREADY IMPLEMENTED + AUDITED** | 04 | Full đầu tiên và periodic keyframe; sequence/header giữ nguyên |
| Delta frame 16×16 | `findChangeBoundingBox`, `BLOCK_SIZE=16` | `ScreenCaptureService.FindChangeBounds`, `BlockSize=16` | **ALREADY IMPLEMENTED + AUDITED** | 04 | Threshold 35%, bounds và no-stale-frame backpressure đã kiểm tra |
| JPEG/nén/FPS | `compressImage`, `fps=20`, `quality=0.7` | `ScreenMaxFps`, `ScreenJpegQuality`, `ScreenFrameProtocol.MaxFrameBytes` | **ALREADY IMPLEMENTED + AUDITED** | 04/15 | Runtime benchmark phụ thuộc Windows/testhost; không đổi quality/FPS algorithm |
| Chuột | `ControlEvent`, `ReceiveEvent` command `-1/-2/-5/-7/-8` | `RemoteInputEventMessage`, `RemoteInputEventPolicy`, `WindowsRemoteInputService` | **ALREADY IMPLEMENTED + HARDENED** | 06 | Normalized 0–10.000, TLS/session/control binding, mouse down/up và release-all |
| Bàn phím/tổ hợp phím | `ControlEvent.keyPressed/keyReleased` | `KeyDown/KeyUp`, `_pressedKeys`, `ReleaseAll` | **ALREADY IMPLEMENTED + HARDENED** | 06 | Modifier/chord truyền theo down/up; key range và identity validation; release khi stop/disconnect |
| Cuộn/kéo thả | `mouseWheelMoved`, `mouseDragged` | MouseWheel + MouseDown/Move/Up; pressed-button tracking | **ALREADY IMPLEMENTED + HARDENED** | 06 | Drag state rõ ràng, wheel bounded, input rate limit; double-click là hai click OS tự nhiên |
| Quy đổi tọa độ/độ phân giải | `scaleX/scaleY` theo panel width/height | normalized coordinate + Win32 `SendInput` absolute/virtual desk | **ALREADY IMPLEMENTED + HARDENED** | 06 | Zoom letterbox mapping được test; virtual desktop/DPI/multi-monitor cần manual |
| Bắt đầu/dừng phiên | `START_SESSION`, `ShareScreen.stop` | consent → capability → screen channel → `StopPendingAsync` | Có | 02/08 | Đã có một session/client slot; cần gom ownership vào Session Manager |
| Khôi phục phiên | `RESTART_SHARESCREEN`, grace period relay | Client reconnect chỉ tạo session mới; screen/control bị stop | Chưa đủ | 08 | Không tự khôi phục control; cần quyết định resume screen có consent mới |
| Mất kết nối | `Session` + `RelayThread` reconnect viewer | TCP keepalive, reconnect backoff, fail pending | Một phần | 08 | Chưa có heartbeat/message-level remote session và remote chat reconnect |
| UAC/Secure Desktop | README tuyên bố đơn giản; source dùng `Robot` | PBL4 dùng `SendInput`, không bypass UAC/Secure Desktop | Đúng hướng | 13 | Giữ giới hạn hệ điều hành, không tạo unlock/bypass |

## B. Remote Chat

| Chức năng | RemoteX source | PBL4 hiện tại | Đánh giá | Module đích / task | Vấn đề cần xử lý |
|---|---|---|---|---|---|
| Gửi/nhận realtime | `common/ChatWindow`, chat socket `writeUTF/readUTF` | `ChatService`, `ClientChatService`, `RemoteChatMessage` trên control channel | **IMPLEMENTED** | 07 | Không mở port riêng; dùng TLS, SendLock và UTF-8 JSON line |
| Người gửi/thời gian | `ChatWindow.appendToPane`, `SimpleDateFormat("HH:mm")` | MessageId, SenderId/Role, UTC Timestamp, minimal WinForms UI | **IMPLEMENTED** | 07 | Render local ở UI; server chuẩn hóa identity/timestamp |
| Theo phiên remote | RemoteX gắn vào username/session relay | `ClientId` + `SessionId` + existing screen remote session | **IMPLEMENTED** | 02, 07 | Không tạo chat socket/session manager thứ hai |
| Mất kết nối | EOF hiển thị thông báo, relay reconnect viewer | Chat ledger/rate limiter theo session; new service per reconnect; stale heartbeat cleanup | **IMPLEMENTED + HARDENED** | 08 | Không replay/offline queue; old SessionId bị reject |

## C. AI Security Monitoring

| Chức năng | RemoteX source/resource | PBL4 hiện tại | Đánh giá | Module đích / task | Vấn đề cần xử lý |
|---|---|---|---|---|---|
| Sysmon/Windows Event Log | `AgentConfig`, `Sysmon.exe`, `sysmonconfig.xml`, Winlogbeat zip | `WindowsEventLogSecurityEventSource`, native `EventLogWatcher` | **IMPLEMENTED — NO AUTO-INSTALL** | 09 | Chỉ đọc log đã có; quyền đọc Event Log/Sysmon và Sysmon installation vẫn cần kiểm tra thực tế |
| NDJSON tail | `NdjsonTailer`, pattern `winlogbeat_output*.ndjson` | `NdjsonSecurityEventSource`, bounded collector queue | **IMPLEMENTED + FIXTURE TESTED** | 09 | Rotation, partial line, UTF-8, checkpoint/offset, malformed JSON và cancellation đã có; runtime test phụ thuộc testhost |
| Feature extraction | `FeatureExtractor`, `Preprocessor` feature metadata | `SecurityFeatureInputParser`, `SecurityFeatureExtractor`, `SecurityFeatureSchema` | **IMPLEMENTED — RAW/GOLDEN TESTED** | 10 | 9 raw fields và ordered 69-model schema khớp artifact; TF-IDF/scaler/ONNX để Task 11 |
| Preprocessing | `Preprocessor`, 69 features, scaler/label/TF-IDF artifacts | `SecurityPreprocessor` + real RemoteX artifacts | **IMPLEMENTED — PARITY TESTED** | 11 | Exact artifact order/mean/scale; SHA-256 fingerprint is the version anchor because metadata has no semantic model version |
| Isolation Forest ONNX | `AnomalyDetector`, `isolation_forest_model.onnx`, input `float_input`, outputs `label/scores` | `SecurityInferenceEngine` + Microsoft.ML.OnnxRuntime | **IMPLEMENTED — RUNTIME VALIDATION PARTIAL** | 11 | Contract validated; native ONNX runtime must ship with the app; score is raw and not a probability/confidence |
| Severity | `AnomalyResult.getSeverity` với CRITICAL/HIGH/MEDIUM/LOW | `SecurityAlertPolicy` dùng label + score thresholds | **IMPLEMENTED — POLICY TESTED** | 12 | Threshold critical/warning lấy từ model metadata; các band còn lại là operational policy, không phải probability calibration |
| Hiển thị cảnh báo | `AlertService` mở Swing `JOptionPane` trên máy sharer | `SecurityAlertService` bounded display queue + `ServerRuntime.SecurityAlertReceived` | **IMPLEMENTED — NON-BLOCKING HOOK** | 12/14 | UI cụ thể để Task 14; subscriber lỗi không làm dừng collector/inference |
| Lịch sử cảnh báo | Không có persistence rõ ràng; alert chỉ hiện dialog | In-memory active store, auto-dismiss, audit log | **IMPLEMENTED — PARTIAL RETENTION** | 12 | Không tạo database/history UI; audit giữ record accepted/suppressed/disconnect outcome |
| Auto disconnect | README nói CRITICAL/HIGH disconnect; source `AlertService` không gọi socket disconnect | Explicit disabled-by-default policy gọi `ScreenStreamingService.StopForSessionAsync` | **IMPLEMENTED — POLICY OFF BY DEFAULT** | 12 | Chỉ dừng đúng SessionId; guard race và false-positive tests; không tác động phiên khác |

### Task 13 — Security hardening audit

| Khu vực | Bằng chứng đã xử lý | Trạng thái | Rủi ro còn lại |
|---|---|---|---|
| TLS / transport | SHA-256 pinning, TLS 1.2/1.3, envelope validation, frame bound | **PASS** | P2: self-signed rotation/revocation cần deployment procedure |
| Session / authorization | expiry, permission, consent, one-time capability, session identity checks | **PASS** | P1: ClientId trong Hello chưa có client-certificate proof |
| Input / chat / alert | payload limits, duplicate ledger, rate limit, forged identity rejection | **PASS** | P2: limits cần monitor/tune theo deployment |
| Resource exhaustion | bounded control/screen active connections, bounded frame/payload/queues | **PASS** | P2: OS/network firewall and service quotas remain deployment responsibility |
| Secrets / logging | no token/password/raw command line in audit output; collector avoids sensitive diagnostics | **PASS with residual** | P2: command-line/environment token exposure |
| Dependencies | `dotnet list package --vulnerable --include-transitive` on 3 projects | **PASS at audit time** | Re-run on every restore/release |

## D. Tích hợp với PBL4

| Yêu cầu | Trạng thái hiện tại | Quyết định audit |
|---|---|---|
| Chọn Client từ danh sách hiện có | Có — dashboard dùng `ServerRuntime.GetClients()`/`ClientSnapshot` | Giữ một `ClientManager`; không tạo registry thứ hai |
| Mở remote từ giao diện quản trị | Có — `ServerDashboardForm` → `ServerDashboardPresenter` → `ServerRuntime.RequestScreenAsync` | Mở rộng flow hiện có, không tạo MainStart/Viewer Java mới |
| Điều khiển theo quyền | Có nền tảng — TLS, consent, capability, input policy | Chốt role/authorization ở Task 13; giữ client consent và revoke |
| Xem trạng thái phiên | Có một phần — `ScreenStreamingService.HasSession`, viewer states | Chuẩn hóa state machine ở Task 02/08 |
| Chat | Chưa có | Bổ sung vào remote session hiện tại ở Task 07 |
| Theo dõi sự kiện bảo mật | Chưa có security event path | `SecurityEventCollector` phát `SecurityEventMessage`; AI/alert để Task 10–12 |
| Không tạo hệ thống Client thứ hai | Đang đạt ở kiến trúc PBL4 | Không port `remote.MainStart`/session registry |
| Không tạo relay server mới | PBL4 đã truyền trực tiếp Server–Client qua TLS | Không đưa `remote.server` vào solution; chỉ tham khảo thuật toán/behavior |

## 1. Ưu tiên sửa/tái sử dụng

1. Tái sử dụng PBL4 transport, TLS, `ClientManager`, screen protocol và input policy.
2. Tạo contract/session owner chung trước khi thêm chat và security event.
3. Port AI preprocessing/model có kiểm chứng artifact, không copy singleton Java.
4. Chỉ sau khi có policy/audit mới bật auto disconnect.

## E. Task 14 — Remote administration UI

| Khu vực | Bằng chứng | Trạng thái | Giới hạn kiểm thử |
|---|---|---|---|
| Client selection / remote open | `ServerDashboardForm` dùng danh sách Client hiện có và gọi `ServerRuntime.RequestScreenAsync` | **IMPLEMENTED** | Cần manual test với Client thật |
| Viewer / control / chat | `RemoteViewerForm` và `RemoteChatForm` gọi service/runtime thật, giữ permission và session hiện có | **IMPLEMENTED — EXISTING FLOW RETAINED** | Không click-through được trong desktop automation hiện tại |
| Remote/reconnect/stop/error state | Viewer state label và lifecycle cleanup hiện có | **IMPLEMENTED** | Reconnect end-to-end cần hai tiến trình/máy |
| Monitoring / security alerts | Dashboard panel nhận `SecurityAlertReceived`, hiển thị bounded history và status alert stream | **IMPLEMENTED** | Collector live status chưa có contract riêng |
| Auto-disconnect policy | Dashboard confirmation + `IServerRuntime.SetAutoDisconnectEnabled`, default OFF, audit | **IMPLEMENTED — FAIL-CLOSED** | Khi thiếu admin token, runtime từ chối thay đổi |
| UI safety / cleanup | UI callback marshal, unsubscribe khi close, không đổi screen/input/chat protocol | **IMPLEMENTED** | DPI, multi-monitor, UAC/Secure Desktop cần Windows manual test |

Task 14 không tạo UI framework, app quản trị, relay, TCP port hoặc message contract mới.

## F. Task 15 — Integration, regression và performance

| Phạm vi | Trạng thái | Bằng chứng / giới hạn |
|---|---|---|
| Solution build | **PASS** | 0 warning, 0 error |
| Unit/full suite | **PASS** | Shared 71/71, Client 71/71 |
| Security tests | **PASS** | Filtered 27/27 |
| Integration/lifecycle/streaming tests | **PASS** | Filtered 18/18; validation harness 79/79 |
| Testhost hang investigation | **PASS — NOT REPRODUCED** | Hang diagnostics timeout 30s hoàn tất, không có dump |
| Screen/input/chat/reconnect/alert automated flows | **PASS** | Covered by existing unit/integration/security tests |
| Sysmon/Event Log live setup | **PARTIAL** | Fixture/source tests PASS; Windows ACL/Sysmon installation manual NOT TESTED |
| ONNX live pipeline | **PARTIAL** | Real artifact/model contract tests PASS; live collector-to-alert pipeline NOT TESTED |
| Performance | **PARTIAL** | Preprocessor đo được 0.065914 ms/event; FPS/latency/bandwidth/CPU/RAM/inference latency NOT TESTED |
| Manual 2-Client LAN/UI/DPI/UAC | **NOT TESTED** | Cần Windows interactive environment với Server/Admin + hai Clients |

Task 15 không có production optimization vì chưa có bottleneck thực đo được.
