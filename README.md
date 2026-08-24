# NetworkAdminTool — Tiện ích quản trị mạng

## Cấu trúc thư mục

```
NetworkAdminTool/
├── Models/
│   ├── NetworkDevice.cs      # 1 thiết bị/host sau khi scan
│   ├── PingResult.cs         # Kết quả ping 1 host
│   └── SystemStats.cs        # Snapshot CPU + RAM tại 1 thời điểm
├── Services/
│   ├── NetworkInfoService.cs     # Lấy IP/subnet của máy hiện tại
│   ├── NetworkScannerService.cs  # Quét cả dải IP (song song)
│   ├── PingService.cs            # Ping 1 host
│   ├── SystemMonitorService.cs   # CPU + RAM real-time
│   └── LoggerService.cs          # Ghi log ra /Logs
├── Forms/
│   ├── MainForm.cs      # Dashboard, mở 3 form con
│   ├── ScannerForm.cs   # Giao diện Scan IP
│   ├── PingForm.cs      # Giao diện Ping
│   └── MonitorForm.cs   # Giao diện CPU/RAM
├── Assets/
├── Logs/
├── Program.cs
└── NetworkAdminTool.csproj
```

## Cách build & chạy (bắt buộc trên Windows)

```powershell
dotnet build
dotnet run
```

Yêu cầu: .NET 10 SDK, hệ điều hành **Windows** (WinForms và
`PerformanceCounter` chỉ chạy trên Windows, không chạy được trên
macOS/Linux).

## Trạng thái từng phần

| File | Trạng thái |
|---|---|
| `NetworkInfoService` | ✅ Đã cài đặt — lấy IP LAN của máy |
| `PingService` | ✅ Đã cài đặt — ping bằng `System.Net.NetworkInformation.Ping` |
| `NetworkScannerService` | ✅ Đã cài đặt — quét song song 254 IP (32 luồng cùng lúc) |
| `SystemMonitorService` | ✅ Đã cài đặt — CPU qua `PerformanceCounter`, RAM qua `ComputerInfo` |
| `MainForm` / `ScannerForm` / `PingForm` / `MonitorForm` | ✅ UI cơ bản đã nối với service tương ứng |
| `LoggerService` | ✅ Ghi log theo ngày vào `/Logs` |

## Việc còn lại (gợi ý cải tiến, không bắt buộc)

- Làm đẹp giao diện: căn chỉnh control bằng `Anchor`/`Dock` thay vì tọa độ
  cứng, thêm icon từ `/Assets`.
- `ScannerForm`: thêm thanh tiến trình (progress bar) khi đang quét.
- `MonitorForm`: vẽ biểu đồ đường/cột thay vì chỉ hiện số (dùng `Chart`
  control hoặc vẽ tay bằng `Graphics`).
- Xử lý trường hợp `SendPingAsync` bị chặn bởi Firewall (một số máy tắt
  phản hồi ICMP mặc định — cần lưu ý khi demo).
- Validate input kỹ hơn ở `ScannerForm`/`PingForm` (subnet sai định dạng...).

## Cách test độc lập từng Service (khuyến nghị trước khi chạy UI)

Tạo 1 project Console tạm, gọi thử:

```csharp
var scanner = new NetworkScannerService();
var devices = await scanner.ScanNetworkAsync("192.168.1");
foreach (var d in devices) Console.WriteLine($"{d.IpAddress} - {d.ResponseTimeMs}ms");
```

Việc này giúp bạn chắc chắn logic đúng trước khi gắn vào giao diện, dễ
debug hơn nhiều so với bấm nút trên Form rồi đoán lỗi ở đâu.
