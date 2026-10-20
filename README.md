# NetworkAdminTool — Tiện ích quản trị mạng

## Cấu trúc thư mục

```
NetworkAdminTool/
├── NetworkAdmin.Client/
│   ├── Forms/              # Giao diện cục bộ
│   ├── Interfaces/         # Hợp đồng của các service Client
│   ├── Models/             # Tiến độ quét cục bộ
│   ├── Services/           # Đo CPU/RAM, Ping, Scan, kết nối Server
│   ├── Assets/
│   ├── appsettings.json
│   ├── Program.cs
│   └── NetworkAdmin.Client.csproj
├── NetworkAdmin.Server/
│   ├── UI/                 # Giao diện quản trị
│   ├── Validation/         # Chương trình kiểm tra
│   ├── Program.cs
│   └── NetworkAdmin.Server.csproj
├── NetworkAdmin.Shared/
│   ├── Models/             # Dữ liệu dùng chung
│   ├── Messages/           # Yêu cầu và phản hồi
│   ├── Protocol/           # Giao thức JSON qua TCP
│   └── NetworkAdmin.Shared.csproj
└── NetworkAdminTool.slnx
```

## Cách build & chạy (bắt buộc trên Windows)

```powershell
dotnet build NetworkAdminTool.slnx

# Server và Client chạy ở hai terminal riêng; mở Server trước.
dotnet run --project NetworkAdmin.Server
dotnet run --project NetworkAdmin.Client

# Chạy Server ở chế độ console để xem địa chỉ bind và log kết nối.
dotnet run --project NetworkAdmin.Server -- --console

# Chạy cục bộ, không kết nối Server:
dotnet run --project NetworkAdmin.Client -- --standalone
```

## Chạy Server và Client trên hai máy Windows

1. Trên máy Server, lấy địa chỉ IPv4 trong mạng LAN (ví dụ `192.168.1.10`) bằng `ipconfig`.
2. Mở TCP cổng 5000 trong Windows Firewall trên máy Server (PowerShell chạy với quyền Administrator):

   ```powershell
   New-NetFirewallRule -DisplayName "NetworkAdmin Server TCP 5000" -Direction Inbound -Protocol TCP -LocalPort 5000 -Action Allow
   ```

   Hoặc dùng Command Prompt chạy với quyền Administrator:

   ```cmd
   netsh advfirewall firewall add rule name="NetworkAdmin Server TCP 5000" dir=in action=allow protocol=TCP localport=5000
   ```

3. Khởi chạy Server. Mặc định Server bind `0.0.0.0:5000` (mọi interface IPv4); ở chế độ console, lệnh `dotnet run --project NetworkAdmin.Server -- --console` in địa chỉ và cổng đang nghe.
4. Trên máy Client, kết nối tới IP LAN của máy Server:

   ```powershell
   dotnet run --project NetworkAdmin.Client -- --host 192.168.1.10 --port 5000
   ```

   Thay `192.168.1.10` bằng địa chỉ IPv4 thực tế của máy Server. Đảm bảo hai máy thông mạng và cùng cho phép lưu lượng TCP 5000.

Để chỉ nhận kết nối từ chính máy Server, khởi chạy với `--host 127.0.0.1`:

```powershell
dotnet run --project NetworkAdmin.Server -- --host 127.0.0.1
```

Yêu cầu: .NET 10 SDK, hệ điều hành **Windows** (WinForms và
`PerformanceCounter` chỉ chạy trên Windows, không chạy được trên
macOS/Linux).

Ứng dụng gốc đã được gộp vào Client với tùy chọn `--standalone`. Client tự biên dịch code trong thư mục của mình và tham chiếu Shared qua `ProjectReference`; không còn liên kết source từ thư mục gốc. Cấu hình được đọc từ `appsettings.json` cạnh file thực thi.

## Trạng thái từng phần

Phase 11: scanner hỗ trợ IPv4 `/23`–`/30`, tối đa 510 host và 32 probe đồng thời mỗi lần quét. Client tự kết nối lại sau mất kết nối; kết quả Ping/Scan trên Server được giữ theo phiên đang chọn. Xem [PHASE11.md](PHASE11.md) để biết phụ thuộc, thay đổi, kiểm tra tự động và quy trình thử trên hai máy.

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
- Validate subnet trước khi quét: nhận CIDR như `172.31.99.116/23`; Phase 11 giới hạn `/23`–`/30` (tối đa 510 host), áp dụng cả trên Server và Client.
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
- Badge `Alerts` trên menu Dashboard không còn là số cố định; số badge lấy từ số cảnh báo mới chưa xem trong `NetworkDashboardState` và được đánh dấu đã xem khi mở form `Alerts`.
- `Lưu lượng mạng` trên Dashboard đã chuyển từ dữ liệu mẫu sang lấy real-time từ các network interface đang hoạt động bằng `NetworkInterface.GetIPv4Statistics()`, tính Mbps download/upload theo delta byte mỗi giây và tự co trục biểu đồ theo dữ liệu thật.
- `Ping trung bình` trên Dashboard lấy từ `ResponseTimeMs` của các thiết bị online trong lần quét gần nhất; nếu chưa có dữ liệu ping thì hiển thị `-`.
- `Thời gian hoạt động` trên Dashboard lấy từ `Environment.TickCount64`, tức uptime hệ thống Windows hiện tại, không dùng số cố định.
- `Disk Usage` ưu tiên tính theo ổ hệ thống Windows; nếu không lấy được ổ hệ thống thì fallback sang trung bình các ổ đĩa đang sẵn sàng.
- `MonitorForm` đã bổ sung card `Disk Usage` chạy cùng timer CPU/RAM; `Network Info` reload lại danh sách adapter đang hoạt động khi form được mở/kích hoạt.
- `CPU Usage` ở Dashboard và Monitoring dùng chung cache mẫu đo trong `SystemMonitorService`, tránh việc hai form gọi `PerformanceCounter.NextValue()` lệch nhịp và hiển thị khác nhau.
- Rà soát các form `Ping`, `Network Info`, `Alerts`, `Logs`, `Settings`, `About` và chuyển các chuỗi tiếng Việt còn thiếu dấu sang có dấu.
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
- Cho phép chọn adapter cụ thể cho biểu đồ lưu lượng mạng thay vì cộng tất cả interface đang hoạt động.
- Bổ sung test đơn vị cho parse CIDR/dải IP, đếm host và normalize MAC address.

## Cách test độc lập từng Service (khuyến nghị trước khi chạy UI)

Tạo 1 project Console tạm, gọi thử:

```csharp
var scanner = new NetworkScannerService(new PingService(), new NetworkInfoService());
var devices = await scanner.ScanNetworkAsync("192.168.1");
foreach (var d in devices) Console.WriteLine($"{d.IpAddress} - {d.ResponseTimeMs}ms");
```

Việc này giúp bạn chắc chắn logic đúng trước khi gắn vào giao diện, dễ
debug hơn nhiều so với bấm nút trên Form rồi đoán lỗi ở đâu.
