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
│   ├── MainForm.cs         # Dashboard, menu chức năng
│   ├── ScannerForm.cs      # Giao diện Scan IP
│   ├── PingForm.cs         # Giao diện Ping
│   ├── MonitorForm.cs      # Giao diện CPU/RAM
│   └── FeatureForms.cs     # Network Info, Alerts, Logs, Settings, About
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
| `NetworkScannerService` | ✅ Đã cài đặt — quét song song, có callback tiến độ và đọc ARP cache |
| `SystemMonitorService` | ✅ Đã cài đặt — CPU qua `PerformanceCounter`, RAM qua `ComputerInfo` |
| `MainForm` / `ScannerForm` / `PingForm` / `MonitorForm` / `FeatureForms` | ✅ UI đã nối service tương ứng, layout dùng `Dock`/`TableLayoutPanel` để co giãn tốt hơn |
| `LoggerService` | ✅ Ghi log theo ngày vào `/Logs` |

## Đã làm trong lần chỉnh giao diện và IP Scanner

- Làm đẹp `ScannerForm`: bố cục lại bằng `TableLayoutPanel`, `Dock`, margin/padding rõ ràng; giảm phụ thuộc vào tọa độ cứng cho control giao diện.
- Chỉnh dashboard chính trong `MainForm`: chữ trong thẻ thống kê, đồng hồ, logo và cảnh báo tự co/ellipsis trong khung để hạn chế lỗi chữ đè chữ hoặc tràn khỏi khung.
- Sửa vùng chọn chuột ở menu dashboard: hover/click theo đúng chức năng đang trỏ chuột, không còn lệch do dùng chỉ số tọa độ/hit-area chung với nút cửa sổ.
- Thêm thanh tiến trình trong `ScannerForm`: hiển thị số host đã quét, tổng host, số thiết bị online và IP hiện tại.
- Mở rộng `NetworkScannerService`/`INetworkScannerService` với `IProgress<NetworkScanProgress>` để UI nhận tiến độ trong lúc quét.
- Sửa lỗi tiềm ẩn khi đếm host CIDR bằng dịch bit `int`; chuyển sang tính bằng `long` và giới hạn về `int`.
- Validate subnet trước khi quét: nhận CIDR như `192.168.1.0/24` hoặc dải `192.168.1.1 - 192.168.1.254`; chặn dải quá lớn hơn `/16` để tránh treo lâu.
- Reset/cập nhật trạng thái nút quét, đồng hồ và progress bar khi hoàn tất hoặc hủy quét.
- Làm đẹp `PingForm` và `MonitorForm`: dùng layout co giãn, font/màu đồng nhất, `ProgressBar` cho CPU/RAM.
- Thêm giao diện riêng cho các mục `Network Info`, `Alerts`, `Logs`, `Settings`, `About` và nối menu dashboard để mở đúng chức năng.
- Chỉnh lại `ScannerForm` và `PingForm` sang giao diện sáng, dùng màu navy/xanh đồng bộ Dashboard, giảm kích thước cửa sổ mặc định và nới chiều cao vùng nội dung để tránh mất chữ/đè chữ.
- Dashboard đã có thêm vùng click trực tiếp cho `Quét lại`, `Xem tất cả thiết bị`, `Xem tất cả cảnh báo`.
- Dashboard đã chỉnh các số tổng thiết bị/online/offline/ping trung bình khớp với bảng thiết bị gần đây; CPU/RAM/Disk/Uptime lấy từ service/hệ thống thay vì toàn bộ là số cố định.
- Dashboard đồng bộ với kết quả quét mới nhất của `ScannerForm` qua `NetworkDashboardState`: nếu IP Scanner quét được 3 thiết bị online thì Dashboard hiển thị theo đúng state gần nhất, không còn dùng bảng mặc định 5 thiết bị.
- `NetworkDashboardState` ghi nhớ các thiết bị đã từng online trong subnet vừa quét. Nếu lần quét sau thiết bị đó không còn phản hồi, `ScannerForm` và Dashboard vẫn hiển thị thiết bị đó với trạng thái `Offline` và giữ `LastSeen`.
- `ScannerForm` có thể bấm vào card `Online` để lọc thiết bị đang kết nối, bấm `Offline` để lọc thiết bị đã từng kết nối nhưng hiện không phản hồi, bấm `Đã quét` để xem toàn bộ.
- Tối ưu IP Scanner để giảm lag khi quét: giới hạn tần suất cập nhật progress UI và bỏ DNS reverse lookup đồng bộ khỏi lúc render kết quả/Dashboard.
- Dashboard phần `Thiết bị trong mạng` hiển thị cả thiết bị online và thiết bị offline đã từng kết nối từ `NetworkDashboardState`; bảng tự giới hạn số dòng theo chiều cao khung để không tràn giao diện.
- `Cảnh báo gần đây` trên Dashboard và form `Alerts` đọc cùng lịch sử từ `NetworkDashboardState`: thiết bị mới online, thiết bị mất kết nối, thiết bị online trở lại và bản ghi hoàn tất quét đều cập nhật theo lần scan gần nhất.
- `Disk Usage` ưu tiên tính theo ổ hệ thống Windows; nếu không lấy được ổ hệ thống thì fallback sang trung bình các ổ đĩa đang sẵn sàng.
- Thêm cấu hình copy toàn bộ `/Assets` ra thư mục output. `ScannerForm` sẽ thử dùng `Assets/icon.png` nếu file ảnh hợp lệ; hiện tại file asset trong repo đang rỗng nên app bỏ qua an toàn để không phát sinh lỗi load ảnh.

## Việc cần phát triển tiếp

- `MonitorForm`: vẽ biểu đồ đường/cột thay vì chỉ hiện số (dùng `Chart`
  control hoặc vẽ tay bằng `Graphics`).
- Xử lý trường hợp `SendPingAsync` bị chặn bởi Firewall (một số máy tắt
  phản hồi ICMP mặc định — cần lưu ý khi demo).
- Bổ sung bộ icon PNG hợp lệ vào `/Assets` vì `icon.png` và `chart.png` hiện đang là file rỗng.
- Thêm tra cứu vendor OUI đầy đủ bằng database local hoặc API/cache thay vì danh sách mẫu.
- Cho phép export kết quả quét ra CSV/Excel.
- Lưu lịch sử quét theo thời gian để so sánh thiết bị mới/mất kết nối.
- Thêm tùy chọn timeout, retry count và số luồng quét trên giao diện.
- Lưu `NetworkDashboardState` ra file/database nếu muốn giữ kết quả Dashboard sau khi tắt mở lại ứng dụng.
- Thay biểu đồ lưu lượng mạng demo bằng dữ liệu thật từ interface mạng.
- Bổ sung test đơn vị cho parse CIDR/dải IP, đếm host và normalize MAC address.

## Cách test độc lập từng Service (khuyến nghị trước khi chạy UI)

Tạo 1 project Console tạm, gọi thử:

```csharp
var scanner = new NetworkScannerService();
var devices = await scanner.ScanNetworkAsync("192.168.1");
foreach (var d in devices) Console.WriteLine($"{d.IpAddress} - {d.ResponseTimeMs}ms");
```

Việc này giúp bạn chắc chắn logic đúng trước khi gắn vào giao diện, dễ
debug hơn nhiều so với bấm nút trên Form rồi đoán lỗi ở đâu.
