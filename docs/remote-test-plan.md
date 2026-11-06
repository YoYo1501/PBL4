# RemoteX/PBL4 — Task 15 Test Plan và kết quả

Ngày kiểm tra: 2026-11-05  
Phạm vi: Task 01–14, chức năng PBL4 gốc và các validation harness hiện có. Không thêm chức năng runtime.

## 1. Automated test commands

Chạy tuần tự trên Windows/.NET 10:

```powershell
dotnet build NetworkAdminTool.slnx --no-restore -m:1 -v:minimal
dotnet test NetworkAdminTool.slnx --no-build --no-restore --logger "console;verbosity=minimal"
dotnet test NetworkAdminTool.slnx --no-build --no-restore --blame-hang --blame-hang-timeout 30s --blame-hang-dump-type none
dotnet test NetworkAdmin.Client.Tests/NetworkAdmin.Client.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~Security"
dotnet test NetworkAdmin.Client.Tests/NetworkAdmin.Client.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ScreenStreamingTests|FullyQualifiedName~ClientConnectionTests|FullyQualifiedName~ReconnectLifecycleTests|FullyQualifiedName~SecurityAlertIntegrationTests|FullyQualifiedName~RemoteChatServiceTests|FullyQualifiedName~ServerLifecycleTests"
dotnet build NetworkAdmin.Server/Validation/Phase10.Validation.csproj --no-restore -m:1 -v:minimal
dotnet run --project NetworkAdmin.Server/Validation/Phase10.Validation.csproj --no-build --no-restore
dotnet build NetworkAdmin.Server/Validation/Layout/Layout.Validation.csproj --no-restore -m:1 -v:minimal
git diff --check
```

Kết quả lần chạy này:

- Solution build: **PASS**, 0 warning, 0 error.
- Full suite: **PASS**, Shared 71/71 và Client 71/71.
- Hang diagnostics: **PASS**, cả hai testhost hoàn tất; không tạo hang dump/sequence file.
- Security filter: **PASS**, 27/27.
- Integration/lifecycle/streaming filter: **PASS**, 18/18.
- `Phase10.Validation`: **PASS**, 79/79 checks.
- `Layout.Validation`: **PASS**.
- `git diff --check`: **PASS**.

Task 00/01 từng ghi nhận testhost dừng ở `TestRequestSender.WaitForRequestHandlerConnection` trước khi chạy test. Vấn đề không tái hiện trong lần chạy Task 15; không có bằng chứng cần sửa production code cho test runner.

## 2. Automated coverage matrix

| Phạm vi | Trạng thái | Bằng chứng |
|---|---|---|
| Contracts/message IDs/serialization | PASS | Shared protocol tests, backward-compatible Hello và message ID tests |
| Session/authorization/consent/timeout | PASS | Remote session, screen consent, authorization và lifecycle tests |
| Full/delta/JPEG/frame ordering/resync | PASS | Screen frame protocol và viewer presenter tests |
| Remote input/rate limit/release-all | PASS | Input policy, drag/key cleanup và TLS screen tests |
| Chat/session binding/duplicate/limits | PASS | Chat service/message tests và old-session rejection |
| Reconnect/heartbeat/stop/race | PASS | Reconnect tests và 79-check validation harness |
| Security collector/fixtures | PASS | NDJSON malformed/rotation/checkpoint/cancellation tests |
| Feature extraction/preprocessing | PASS | Golden fixture, schema/artifact tests |
| ONNX contract/inference | PASS | Real artifact load, tensor contract và inference tests |
| Alert/severity/dedupe/auto-disconnect | PASS | Security alert service/integration/race tests |
| UI layout/remote dashboard | PARTIAL | Layout validation PASS; interactive UI/manual click-through chưa đủ |

## 3. Manual test checklist — 1 Server, 1 Admin và 2 Windows Clients

Chuẩn bị ba máy Windows hoặc hai Client Windows tách biệt với Server/Admin. Dùng token quản trị được phân phối ngoài kênh chat và certificate fingerprint đúng.

| # | Luồng manual | Expected result | Kết quả môi trường hiện tại |
|---:|---|---|---|
| 1 | Server start, TLS fingerprint, Client A/B connect | Hai Client có session khác nhau, không duplicate row | NOT TESTED — cần LAN/2 Client |
| 2 | Admin chọn A/B từ danh sách | Chỉ thao tác trên Client được chọn | NOT TESTED — cần interactive LAN |
| 3 | View screen, consent accept/reject | Accept mở viewer; reject không mở screen channel | Automated PASS; manual NOT TESTED |
| 4 | Full frame, delta frame, periodic full frame | Ảnh đúng thứ tự, delta không làm sai vùng khác | Automated PASS; manual NOT TESTED |
| 5 | JPEG/FPS/resize/DPI/multi-monitor | Không méo ảnh, không tích tụ frame | Logic PASS; desktop manual NOT TESTED |
| 6 | View-only rồi request control | Control chỉ hoạt động sau consent/capability hợp lệ | Automated PASS; manual NOT TESTED |
| 7 | Mouse/key/click/drag/wheel/chord | Input đúng tọa độ; stop không kẹt phím/nút | Automated PASS; desktop manual NOT TESTED |
| 8 | Chat hai chiều theo từng session | UTF-8, giới hạn, duplicate và spoof bị từ chối | Automated PASS; manual NOT TESTED |
| 9 | Rút mạng Client A | A disconnect/cleanup; B và Server vẫn hoạt động | Automated PASS; physical network NOT TESTED |
| 10 | Restart Server | Client reconnect session mới; không tự khôi phục control | Automated PASS; manual NOT TESTED |
| 11 | Restart Client B | Session cũ bị vô hiệu; session mới cần flow/consent mới | Automated PASS; manual NOT TESTED |
| 12 | Stop viewer/reconnect race | Không zombie task/socket/orphan session | Automated PASS; manual NOT TESTED |
| 13 | Sysmon/Event Log collector | Status đúng quyền; không tự cài component | Fixture PASS; Windows ACL NOT TESTED |
| 14 | ONNX model load và inference | Đúng input `[1,69]`, label/score; score không phải probability | Automated PASS; live pipeline NOT TESTED |
| 15 | Security alert/auto-disconnect | Mặc định chỉ alert; bật policy chỉ dừng đúng session | Automated PASS; manual UI NOT TESTED |
| 16 | Locked desktop/UAC/Secure Desktop | Không bypass; capture/input fail an toàn | NOT TESTED — cần Windows interactive setup |

## 4. Sysmon và security collector setup

PBL4 **không tự động tải, cài hoặc chạy Sysmon**. Quản trị viên phải tải Sysmon từ Microsoft Sysinternals, kiểm tra file/config theo policy nội bộ và mở PowerShell bằng Administrator.

Ví dụ sau khi đã có `Sysmon64.exe` và file cấu hình đã được review:

```powershell
Sysmon64.exe -accepteula -i C:\Security\sysmonconfig.xml
Get-WinEvent -LogName 'Microsoft-Windows-Sysmon/Operational' -MaxEvents 5
```

Gỡ cài đặt chỉ khi được quản trị viên cho phép:

```powershell
Sysmon64.exe -u
```

Collector mặc định đọc `Microsoft-Windows-Sysmon/Operational`. Có thể chọn nguồn/file bằng:

```text
--security-source Auto|WindowsEventLog|Ndjson
--security-event-log <log-name>
--security-ndjson-directory <directory>
--security-ndjson-pattern winlogbeat_output*.ndjson
--security-checkpoint <checkpoint-file>
```

Environment tương ứng là `NETWORKADMIN_SECURITY_SOURCE`, `NETWORKADMIN_SECURITY_EVENT_LOG`, `NETWORKADMIN_SECURITY_NDJSON_DIRECTORY`, `NETWORKADMIN_SECURITY_NDJSON_PATTERN` và `NETWORKADMIN_SECURITY_CHECKPOINT`. Nếu thiếu quyền hoặc source không tồn tại, collector báo trạng thái lỗi; không ghi raw event/command line vào diagnostic log.

## 5. AI model/artifact configuration

Artifacts thật nằm tại `NetworkAdmin.Client/Assets/SecurityModel/` và được copy cạnh executable:

- `isolation_forest_model.onnx`
- `feature_names.json`, `feature_metadata.json`
- `label_encoders.json`
- `tfidf_image.json`, `tfidf_cmdline.json`
- `scaler.json`, `onnx_metadata.json`

Contract đã kiểm chứng: input `float_input` shape `[1,69]`, outputs `label` (`int64`) và `scores` (`float`), opset 12. `label=-1` là anomaly; `scores` là raw anomaly score và `Confidence` vẫn null vì chưa calibration.

Không thay file model/scaler/encoder bằng file tự tạo. Nếu cần kiểm tra thủ công trong code/test, artifact directory phải trỏ tới `Path.Combine(AppContext.BaseDirectory, "SecurityModel")`; `SecurityInferenceEngine.Load` sẽ fail-closed khi thiếu artifact hoặc sai tensor contract. Ứng dụng hiện không tự bật collector/inference chỉ vì assets tồn tại.

## 6. Performance measurements

Chỉ ghi số đo thu được từ test run, không dùng ngưỡng giả lập:

- `SecurityPreprocessor`: 1.000 transforms trong **65.914 ms**, trung bình **0.065914 ms/event** trên máy audit; test chỉ là diagnostic, không phải SLA.
- Screen FPS thực tế, JPEG bandwidth, end-to-end latency, CPU/RAM của Server/Client và ONNX inference latency: **NOT TESTED** vì chưa có hai Windows Client/desktop workload và network capture trong môi trường hiện tại.
- Không có optimization production được thực hiện trong Task 15. Thay đổi duy nhất là sửa compile include của validation harness và cập nhật assertion test cũ theo authorization contract hiện tại.
