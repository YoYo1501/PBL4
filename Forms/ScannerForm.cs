using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Net;
using NetworkAdminTool.Interfaces;
using NetworkAdminTool.Models;
using NetworkAdminTool.Services;

namespace NetworkAdminTool.Forms
{
    public class ScannerForm : Form
    {
        private readonly INetworkScannerService _scannerService;
        private readonly INetworkInfoService _networkInfoService;
        private readonly ILoggerService _logger;
        private readonly NetworkDashboardState _dashboardState;

        private readonly ComboBox _cboSubnet;
        private readonly ComboBox _cboInterfaces;
        private readonly Button _btnStartScan;
        private readonly TextBox _txtSearch;
        private readonly Label _lblScannedValue;
        private readonly Label _lblOnlineValue;
        private readonly Label _lblOfflineValue;
        private readonly Label _lblDurationValue;
        private readonly Label _lblResultTitle;
        private readonly Label _lblProgressStatus;
        private readonly ProgressBar _scanProgress;
        private Label _lblAdapterValue;
        private Label _lblIpValue;
        private Label _lblMaskValue;
        private Label _lblGatewayValue;
        private Label _lblMacValue;
        private readonly DataGridView _gridResults;
        private readonly System.Windows.Forms.Timer _scanTimer;
        private readonly Stopwatch _scanStopwatch;

        private CancellationTokenSource? _scanCts;
        private bool _isScanning;
        private List<NetworkDeviceRow> _allRows = new();
        private int _totalHosts;
        private DateTime _lastProgressUiUpdateUtc = DateTime.MinValue;
        private DeviceFilter _deviceFilter = DeviceFilter.All;

        private static readonly Color PageBack = Color.FromArgb(244, 248, 253);
        private static readonly Color PanelBack = Color.White;
        private static readonly Color PanelBack2 = Color.White;
        private static readonly Color Border = Color.FromArgb(213, 224, 240);
        private static readonly Color Ink = Color.FromArgb(12, 28, 83);
        private static readonly Color Muted = Color.FromArgb(58, 72, 118);
        private static readonly Color Blue = Color.FromArgb(13, 101, 238);
        private static readonly Color Green = Color.FromArgb(12, 164, 91);
        private static readonly Color Red = Color.FromArgb(255, 47, 60);
        private static readonly Color Purple = Color.FromArgb(117, 61, 222);

        private static Font UiFont(float pixels, FontStyle style = FontStyle.Regular, string family = "Segoe UI") =>
            new(family, pixels, style, GraphicsUnit.Pixel);

        public ScannerForm(INetworkScannerService scannerService, INetworkInfoService networkInfoService, ILoggerService logger, NetworkDashboardState dashboardState)
        {
            _scannerService = scannerService;
            _networkInfoService = networkInfoService;
            _logger = logger;
            _dashboardState = dashboardState;

            Text = "IP Scanner";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1320, 780);
            MinimumSize = new Size(1120, 720);
            MaximizeBox = true;
            BackColor = PageBack;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            Font = UiFont(14F);

            _lblDurationValue = new Label { Text = "00:00:00" };
            _scanStopwatch = new Stopwatch();
            _scanTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _scanTimer.Tick += (_, _) => _lblDurationValue.Text = _scanStopwatch.Elapsed.ToString(@"hh\:mm\:ss");

            _lblAdapterValue = new Label();
            _lblIpValue = new Label();
            _lblMaskValue = new Label();
            _lblGatewayValue = new Label();
            _lblMacValue = new Label();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                BackColor = PageBack,
                ColumnCount = 2,
                RowCount = 1
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            var leftPanel = BuildLeftInfoPanel();
            AddLeftDetails(leftPanel);

            var rightPanel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = PanelBack,
                BorderColor = Border,
                Radius = 16,
                Padding = new Padding(38, 26, 34, 32),
                Margin = new Padding(9, 0, 0, 0)
            };

            _cboSubnet = BuildCombo(ComboBoxStyle.DropDown);
            _cboInterfaces = BuildCombo(ComboBoxStyle.DropDownList);
            _cboInterfaces.SelectedIndexChanged += (_, _) => OnInterfaceChanged();
            _btnStartScan = BuildScanButton();
            _btnStartScan.Click += BtnStartScan_Click;

            _lblScannedValue = new Label();
            _lblOnlineValue = new Label();
            _lblOfflineValue = new Label();
            _lblProgressStatus = new Label();
            _scanProgress = BuildProgressBar();
            _lblResultTitle = new Label
            {
                Text = "Kết quả quét (0 thiết bị online)",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiFont(23F, FontStyle.Bold, "Segoe UI Semibold"),
                ForeColor = Ink,
                Margin = new Padding(0, 0, 8, 0)
            };
            _txtSearch = BuildSearchBox();
            _txtSearch.TextChanged += (_, _) => ApplyFilter();
            _gridResults = BuildGrid();

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 6
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 136F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            mainLayout.Controls.Add(BuildTitlePanel(), 0, 0);
            mainLayout.Controls.Add(BuildFilterPanel(), 0, 1);
            mainLayout.Controls.Add(BuildProgressPanel(), 0, 2);
            mainLayout.Controls.Add(BuildCardsPanel(), 0, 3);
            mainLayout.Controls.Add(BuildResultsHeader(), 0, 4);
            mainLayout.Controls.Add(_gridResults, 0, 5);

            rightPanel.Controls.Add(mainLayout);
            root.Controls.Add(leftPanel, 0, 0);
            root.Controls.Add(rightPanel, 1, 0);
            Controls.Add(root);

            LoadAvailableInterfaces();
            if (!LoadLastScanState())
            {
                ResetStats();
            }
        }

        private Control BuildTitlePanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 1
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68F));

            var textPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2
            };
            textPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            textPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
            textPanel.Controls.Add(new Label
            {
                Text = "IP Scanner",
                Font = UiFont(38F, FontStyle.Bold, "Segoe UI Semibold"),
                ForeColor = Ink,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft
            }, 0, 0);
            textPanel.Controls.Add(new Label
            {
                Text = "Thiết bị đang hoạt động trong LAN",
                Font = UiFont(17F),
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);

            panel.Controls.Add(textPanel, 0, 0);
            var assetIcon = TryLoadAssetImage("icon.png");
            if (assetIcon != null)
            {
                panel.Controls.Add(new PictureBox
                {
                    Dock = DockStyle.Fill,
                    Image = assetIcon,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Margin = new Padding(10, 4, 0, 12)
                }, 1, 0);
            }

            return panel;
        }

        private Control BuildFilterPanel()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0, 5, 0, 0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178F));

            layout.Controls.Add(BuildLabeledInput("Phạm vi mạng", _cboSubnet, new Padding(0, 0, 40, 0)), 0, 0);
            layout.Controls.Add(BuildLabeledInput("Giao diện mạng", _cboInterfaces, new Padding(0, 0, 54, 0)), 1, 0);
            layout.Controls.Add(_btnStartScan, 2, 0);
            return layout;
        }

        private Control BuildLabeledInput(string label, Control input, Padding margin)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Margin = margin,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            panel.Controls.Add(new Label
            {
                Text = label,
                ForeColor = Ink,
                Font = UiFont(15F),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            input.Dock = DockStyle.Top;
            input.Height = 44;
            panel.Controls.Add(input, 0, 1);
            return panel;
        }

        private Control BuildProgressPanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0, 0, 0, 8)
            };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 14F));

            _lblProgressStatus.Text = "Sẵn sàng quét";
            _lblProgressStatus.Dock = DockStyle.Fill;
            _lblProgressStatus.ForeColor = Muted;
            _lblProgressStatus.Font = UiFont(15F);
            _lblProgressStatus.TextAlign = ContentAlignment.MiddleLeft;
            _scanProgress.Dock = DockStyle.Fill;

            panel.Controls.Add(_lblProgressStatus, 0, 0);
            panel.Controls.Add(_scanProgress, 0, 1);
            return panel;
        }

        private Control BuildCardsPanel()
        {
            var cardsPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 8)
            };
            for (var i = 0; i < 4; i++)
                cardsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

            var scannedCard = BuildStatCard("Đã quét", "địa chỉ", _lblScannedValue, Blue, "screen");
            var onlineCard = BuildStatCard("Online", "thiết bị", _lblOnlineValue, Green, "dot");
            var offlineCard = BuildStatCard("Offline", "đã từng kết nối", _lblOfflineValue, Red, "wifi");
            MakeFilterCard(scannedCard, DeviceFilter.All);
            MakeFilterCard(onlineCard, DeviceFilter.Online);
            MakeFilterCard(offlineCard, DeviceFilter.Offline);

            cardsPanel.Controls.Add(scannedCard, 0, 0);
            cardsPanel.Controls.Add(onlineCard, 1, 0);
            cardsPanel.Controls.Add(offlineCard, 2, 0);
            cardsPanel.Controls.Add(BuildStatCard("Thời gian quét", "", _lblDurationValue, Purple, "clock"), 3, 0);
            return cardsPanel;
        }

        private void MakeFilterCard(Control control, DeviceFilter filter)
        {
            control.Cursor = Cursors.Hand;
            control.Click += (_, _) =>
            {
                _deviceFilter = filter;
                ApplyFilter();
            };

            foreach (Control child in control.Controls)
            {
                MakeFilterCard(child, filter);
            }
        }

        private Control BuildResultsHeader()
        {
            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 1
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250F));

            var searchWrap = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 9, 0, 9),
                Radius = 8,
                BackColor = Color.White,
                BorderColor = Color.FromArgb(209, 218, 233),
                Padding = new Padding(36, 10, 10, 0)
            };
            searchWrap.Controls.Add(new SearchGlyph
            {
                Dock = DockStyle.Left,
                Width = 26,
                ForeColor = Ink,
                Margin = new Padding(0, 0, 8, 0)
            });
            searchWrap.Controls.Add(_txtSearch);

            header.Controls.Add(_lblResultTitle, 0, 0);
            header.Controls.Add(searchWrap, 1, 0);
            return header;
        }

        private static ComboBox BuildCombo(ComboBoxStyle style)
        {
            return new ComboBox
            {
                DropDownStyle = style,
                Font = UiFont(19F),
                ForeColor = Ink,
                BackColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                IntegralHeight = false
            };
        }

        private static Button BuildScanButton()
        {
            var button = new Button
            {
                Text = "Quét",
                Height = 48,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 20),
                FlatStyle = FlatStyle.Flat,
                BackColor = Blue,
                ForeColor = Color.White,
                Font = UiFont(19F, FontStyle.Bold, "Segoe UI Semibold"),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        private static ProgressBar BuildProgressBar()
        {
            return new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };
        }

        private static TextBox BuildSearchBox()
        {
            return new TextBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                PlaceholderText = "Tìm kiếm...",
                Font = UiFont(18F),
                ForeColor = Ink,
                BackColor = Color.White,
                Margin = new Padding(0)
            };
        }

        private RoundedPanel BuildLeftInfoPanel()
        {
            var leftPanel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = PanelBack,
                BorderColor = Border,
                Radius = 16,
                Padding = new Padding(18, 18, 16, 16),
                Margin = new Padding(0, 14, 0, 14)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 7
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            for (var i = 0; i < 5; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));

            layout.Controls.Add(new Label
            {
                Text = "Connected",
                Font = UiFont(20F, FontStyle.Bold, "Segoe UI Semibold"),
                ForeColor = Green,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = "Thông tin mạng",
                Font = UiFont(20F, FontStyle.Bold, "Segoe UI Semibold"),
                ForeColor = Ink,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.TopLeft
            }, 0, 1);

            leftPanel.Controls.Add(layout);

            return leftPanel;
        }

        private void AddLeftDetails(Control leftPanel)
        {
            if (leftPanel.Controls[0] is not TableLayoutPanel layout)
                return;

            layout.Controls.Add(BuildLeftInfoRow("Adapter", ref _lblAdapterValue, "screen"), 0, 2);
            layout.Controls.Add(BuildLeftInfoRow("IP Address", ref _lblIpValue, "dot"), 0, 3);
            layout.Controls.Add(BuildLeftInfoRow("Subnet Mask", ref _lblMaskValue, "nodes"), 0, 4);
            layout.Controls.Add(BuildLeftInfoRow("Gateway", ref _lblGatewayValue, "link"), 0, 5);
            layout.Controls.Add(BuildLeftInfoRow("MAC Address", ref _lblMacValue, "chip"), 0, 6);
        }

        private static Control BuildLeftInfoRow(string title, ref Label valueLabel, string icon)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0, 0, 0, 8)
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            row.Controls.Add(new SmallIconPanel
            {
                IconName = icon,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 10, 18),
                BackColor = Color.FromArgb(221, 234, 255),
                ForeColor = Blue
            }, 0, 0);
            row.SetRowSpan(row.Controls[0], 2);
            row.Controls.Add(new Label
            {
                Text = title,
                ForeColor = Ink,
                Font = UiFont(15F),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 1, 0);
            valueLabel = new Label
            {
                Text = "-",
                ForeColor = Ink,
                Font = UiFont(15F, FontStyle.Bold, "Segoe UI Semibold"),
                Dock = DockStyle.Fill,
                AutoEllipsis = true
            };
            row.Controls.Add(valueLabel, 1, 1);
            return row;
        }

        private static RoundedPanel BuildStatCard(string title, string subtitle, Label valueLabel, Color accent, string icon)
        {
            var panel = new RoundedPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 22, 14),
                BackColor = PanelBack2,
                BorderColor = Color.FromArgb(170, accent),
                Radius = 13
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(16, 14, 14, 10)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.Controls.Add(new StatIconPanel
            {
                IconName = icon,
                Accent = accent,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 14, 20)
            }, 0, 0);
            layout.SetRowSpan(layout.Controls[0], 3);
            layout.Controls.Add(new Label
            {
                Text = title,
                ForeColor = title == "Offline" ? accent : Muted,
                Font = UiFont(18F),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 1, 0);

            valueLabel.Text = "0";
            valueLabel.ForeColor = Ink;
            valueLabel.Font = UiFont(34F, FontStyle.Bold, "Segoe UI Semibold");
            valueLabel.Dock = DockStyle.Fill;
            valueLabel.AutoEllipsis = true;
            layout.Controls.Add(valueLabel, 1, 1);

            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                layout.Controls.Add(new Label
                {
                    Text = subtitle,
                    ForeColor = Muted,
                    Font = UiFont(17F),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.TopLeft
                }, 1, 2);
            }

            panel.Controls.Add(layout);
            return panel;
        }

        private static DataGridView BuildGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                AutoGenerateColumns = false,
                EnableHeadersVisualStyles = false,
                GridColor = Color.FromArgb(226, 232, 242),
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single
            };

            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(13, 101, 238);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = UiFont(16F, FontStyle.Bold, "Segoe UI Semibold");
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(13, 101, 238);
            grid.ColumnHeadersHeight = 44;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;

            grid.DefaultCellStyle.Font = UiFont(15F);
            grid.DefaultCellStyle.ForeColor = Color.FromArgb(29, 47, 100);
            grid.DefaultCellStyle.BackColor = Color.White;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(235, 242, 255);
            grid.DefaultCellStyle.SelectionForeColor = Ink;
            grid.RowTemplate.Height = 42;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "IP Address", DataPropertyName = nameof(NetworkDeviceRow.IpAddress), FillWeight = 15 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Hostname", DataPropertyName = nameof(NetworkDeviceRow.Hostname), FillWeight = 20 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "MAC Address", DataPropertyName = nameof(NetworkDeviceRow.MacAddress), FillWeight = 17 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Trạng thái", DataPropertyName = nameof(NetworkDeviceRow.StatusText), FillWeight = 11 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Độ trễ (ms)", DataPropertyName = nameof(NetworkDeviceRow.ResponseTimeText), FillWeight = 13 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nhà cung cấp", DataPropertyName = nameof(NetworkDeviceRow.Vendor), FillWeight = 20 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Lần thấy cuối", DataPropertyName = nameof(NetworkDeviceRow.LastSeen), FillWeight = 17 });

            grid.Columns[0].DefaultCellStyle.Font = UiFont(15F, FontStyle.Bold, "Segoe UI Semibold");
            grid.Columns[4].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.CellPainting += Grid_CellPainting;
            return grid;
        }

        private static void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (sender is not DataGridView || e.RowIndex < 0 || e.ColumnIndex != 3)
                return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
            var text = Convert.ToString(e.FormattedValue) ?? string.Empty;
            var online = text.Equals("Online", StringComparison.OrdinalIgnoreCase);
            var pill = new Rectangle(e.CellBounds.X + 18, e.CellBounds.Y + 12, 92, 28);
            using var path = RoundedPanel.CreateRoundPath(pill, 13);
            using var fill = new SolidBrush(online ? Color.FromArgb(232, 249, 240) : Color.FromArgb(255, 235, 238));
            using var border = new Pen(online ? Color.FromArgb(199, 235, 215) : Color.FromArgb(255, 207, 213));
            using var dot = new SolidBrush(online ? Green : Red);
            using var textBrush = new SolidBrush(online ? Green : Red);
            using var font = UiFont(15F);

            var graphics = e.Graphics;
            if (graphics == null)
                return;

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            graphics.FillEllipse(dot, pill.X + 13, pill.Y + 11, 7, 7);
            graphics.DrawString(text, font, textBrush, pill.X + 29, pill.Y + 5);
            e.Handled = true;
        }

        private void LoadAvailableInterfaces()
        {
            var interfaces = _networkInfoService.GetAvailableNetworkInterfaces();
            _cboInterfaces.Items.Clear();

            foreach (var item in interfaces)
                _cboInterfaces.Items.Add(item);

            if (_cboInterfaces.Items.Count > 0)
            {
                _cboInterfaces.SelectedIndex = 0;
            }
            else
            {
                _lblAdapterValue.Text = "-";
                _lblIpValue.Text = "-";
                _lblMaskValue.Text = "-";
                _lblGatewayValue.Text = "-";
                _lblMacValue.Text = "-";
            }
        }

        private void OnInterfaceChanged()
        {
            if (_cboInterfaces.SelectedItem is not NetworkInterfaceInfo selected)
                return;

            var cidr = _networkInfoService.GetSubnetPrefix(selected.IpAddress);
            if (!string.IsNullOrWhiteSpace(cidr) && !_cboSubnet.Items.Contains(ToRangeText(cidr)))
                _cboSubnet.Items.Insert(0, ToRangeText(cidr));

            _cboSubnet.Text = ToRangeText(cidr);
            _lblAdapterValue.Text = selected.Description;
            _lblIpValue.Text = selected.IpAddress;
            _lblGatewayValue.Text = string.IsNullOrWhiteSpace(selected.Gateway) ? "-" : selected.Gateway;
            _lblMaskValue.Text = TryGetMaskFromCidr(cidr, out var mask) ? mask : "-";
            _lblMacValue.Text = TryGetLocalMac(selected.Name, out var mac) ? mac : "-";
        }

        private async void BtnStartScan_Click(object? sender, EventArgs e)
        {
            if (_isScanning)
            {
                _scanCts?.Cancel();
                return;
            }

            var subnet = ToCidrText(_cboSubnet.Text.Trim());
            if (!TryValidateScanSubnet(subnet, out var validationMessage))
            {
                MessageBox.Show(validationMessage, "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _scanCts = new CancellationTokenSource();
            _isScanning = true;
            _allRows = new List<NetworkDeviceRow>();
            _totalHosts = CountHosts(subnet);
            _lastProgressUiUpdateUtc = DateTime.MinValue;
            SetScanProgress(0, _totalHosts, 0, string.Empty);
            _btnStartScan.Text = "Dừng";
            _scanStopwatch.Restart();
            _scanTimer.Start();
            _lblDurationValue.Text = "00:00:00";
            _lblResultTitle.Text = "Kết quả quét (đang quét...)";

            var selectedInterface = _cboInterfaces.SelectedItem as NetworkInterfaceInfo;
            var progress = new Progress<NetworkScanProgress>(p =>
            {
                var now = DateTime.UtcNow;
                if (p.ScannedHosts < p.TotalHosts && (now - _lastProgressUiUpdateUtc).TotalMilliseconds < 150)
                    return;

                _lastProgressUiUpdateUtc = now;
                SetScanProgress(p.ScannedHosts, p.TotalHosts, p.OnlineHosts, p.CurrentIpAddress);
                _lblScannedValue.Text = p.ScannedHosts.ToString();
                _lblOnlineValue.Text = p.OnlineHosts.ToString();
                _lblOfflineValue.Text = _dashboardState.GetLatestDevices().Count(device => !device.IsOnline).ToString();
            });

            try
            {
                var devices = await _scannerService.ScanNetworkAsync(
                    subnet,
                    800,
                    interfaceName: selectedInterface?.Name,
                    cancellationToken: _scanCts.Token,
                    progress: progress);

                var scanAt = DateTime.Now;
                _dashboardState.UpdateScanResult(subnet, devices, scanAt);
                _allRows = _dashboardState.GetLatestDevices()
                    .Select(device => CreateDeviceRow(device, scanAt))
                    .OrderBy(row => GetIpValue(row.IpAddress))
                    .ToList();

                ApplyFilter();
                UpdateStats();
                SetScanProgress(_totalHosts, _totalHosts, _allRows.Count(row => row.StatusText == "Online"), string.Empty);
                _logger.Log($"IP scanner completed: {subnet}, online={_allRows.Count(row => row.StatusText == "Online")}, known={_allRows.Count}");
            }
            catch (OperationCanceledException)
            {
                _logger.Log($"IP scanner cancelled: {subnet}");
                _lblResultTitle.Text = $"Kết quả quét (đã dừng, {_allRows.Count} thiết bị online)";
            }
            catch (Exception ex)
            {
                _logger.Log($"IP scanner failed: {ex.Message}");
                MessageBox.Show($"Quét mạng thất bại: {ex.Message}", "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _scanStopwatch.Stop();
                _scanTimer.Stop();
                _lblDurationValue.Text = _scanStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                _btnStartScan.Text = "Quét";
                _scanCts?.Dispose();
                _scanCts = null;
                _isScanning = false;
                _lblProgressStatus.Text = _scanProgress.Value >= _scanProgress.Maximum
                    ? $"Hoàn tất: {_allRows.Count(row => row.StatusText == "Online")} online, {_allRows.Count(row => row.StatusText == "Offline")} offline trong {_scanStopwatch.Elapsed:mm\\:ss}"
                    : "Sẵn sàng quét";
            }
        }

        private void ApplyFilter()
        {
            var keyword = _txtSearch.Text.Trim();
            var rows = _allRows.AsEnumerable();

            rows = _deviceFilter switch
            {
                DeviceFilter.Online => rows.Where(row => row.StatusText == "Online"),
                DeviceFilter.Offline => rows.Where(row => row.StatusText == "Offline"),
                _ => rows
            };

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                rows = rows.Where(row =>
                    row.IpAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || row.Hostname.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || row.MacAddress.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || row.Vendor.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            var filteredRows = rows.ToList();

            _gridResults.DataSource = filteredRows;
            var suffix = _deviceFilter switch
            {
                DeviceFilter.Online => "online",
                DeviceFilter.Offline => "offline đã từng kết nối",
                _ => "thiết bị"
            };
            _lblResultTitle.Text = $"Kết quả quét ({filteredRows.Count} {suffix}, {_allRows.Count(row => row.StatusText == "Online")} online)";
        }

        private void UpdateStats()
        {
            var online = _allRows.Count(row => row.StatusText == "Online");
            var offline = _allRows.Count(row => row.StatusText == "Offline");
            var scanned = _totalHosts > 0 ? _totalHosts : _allRows.Count;

            _lblScannedValue.Text = scanned.ToString();
            _lblOnlineValue.Text = online.ToString();
            _lblOfflineValue.Text = offline.ToString();
        }

        private bool LoadLastScanState()
        {
            if (!_dashboardState.HasScan)
                return false;

            _totalHosts = string.IsNullOrWhiteSpace(_dashboardState.LastSubnet) ? 0 : CountHosts(_dashboardState.LastSubnet);
            _allRows = _dashboardState.GetLatestDevices()
                .Select(device => CreateDeviceRow(device, _dashboardState.LastScanAt ?? DateTime.Now))
                .OrderBy(row => GetIpValue(row.IpAddress))
                .ToList();

            ApplyFilter();
            UpdateStats();
            _lblDurationValue.Text = "00:00:00";
            _lblProgressStatus.Text = $"Kết quả gần nhất: {_dashboardState.LastScanAt:HH:mm:ss}";
            return true;
        }

        private void ResetStats()
        {
            _lblScannedValue.Text = "0";
            _lblOnlineValue.Text = "0";
            _lblOfflineValue.Text = "0";
            _lblDurationValue.Text = "00:00:00";
            SetScanProgress(0, 100, 0, string.Empty);
        }

        private void SetScanProgress(int scanned, int total, int online, string currentIp)
        {
            var maximum = Math.Max(1, Math.Min(total, int.MaxValue));
            _scanProgress.Maximum = maximum;
            _scanProgress.Value = Math.Max(0, Math.Min(scanned, maximum));

            _lblProgressStatus.Text = _isScanning
                ? $"Đang quét {scanned}/{total} địa chỉ - online: {online}{(string.IsNullOrWhiteSpace(currentIp) ? string.Empty : $" - {currentIp}")}"
                : "Sẵn sàng quét";
        }

        private static bool TryGetMaskFromCidr(string cidr, out string subnetMask)
        {
            subnetMask = string.Empty;
            var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 32)
                return false;

            var mask = prefix == 0 ? 0U : uint.MaxValue << (32 - prefix);
            var bytes = BitConverter.GetBytes(mask);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            subnetMask = new IPAddress(bytes).ToString();
            return true;
        }

        private static string ToRangeText(string cidr)
        {
            if (!TryParseCidr(cidr, out var network, out var prefix) || prefix is < 1 or > 30)
                return cidr;

            var mask = uint.MaxValue << (32 - prefix);
            var first = (network & mask) + 1;
            var last = (network & mask | ~mask) - 1;
            return $"{ToIpString(first)}  -  {ToIpString(last)}";
        }

        private static string ToCidrText(string text)
        {
            if (text.Contains('/'))
                return text;

            var parts = text.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var firstAddress))
                return text;

            var first = ToUInt32(firstAddress);
            var network = first & 0xFFFFFF00;
            return $"{ToIpString(network)}/24";
        }

        private static bool TryValidateScanSubnet(string subnet, out string message)
        {
            message = string.Empty;
            if (string.IsNullOrWhiteSpace(subnet))
            {
                message = "Vui lòng nhập subnet hoặc CIDR, ví dụ: 192.168.1.0/24";
                return false;
            }

            if (!TryParseCidr(subnet, out _, out var prefix) || prefix is < 1 or > 30)
            {
                message = "Subnet không hợp lệ. Hãy nhập dạng CIDR như 192.168.1.0/24 hoặc dải 192.168.1.1 - 192.168.1.254.";
                return false;
            }

            var hostCount = CountHosts(subnet);
            if (hostCount > 65_534)
            {
                message = "Dải quét quá lớn. Vui lòng dùng /16 hoặc nhỏ hơn để tránh quét quá lâu.";
                return false;
            }

            return true;
        }

        private static bool TryParseCidr(string cidr, out uint network, out int prefix)
        {
            network = 0;
            prefix = 0;
            var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || !int.TryParse(parts[1], out prefix))
                return false;

            network = ToUInt32(address);
            return true;
        }

        private static int CountHosts(string subnet)
        {
            var parts = subnet.Split('/', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], out var prefix) && prefix >= 1 && prefix <= 30)
                return (int)Math.Min(int.MaxValue, (1L << (32 - prefix)) - 2);

            return 254;
        }

        private static NetworkDeviceRow CreateDeviceRow(NetworkDevice device, DateTime scanTime)
        {
            return new NetworkDeviceRow
            {
                IpAddress = device.IpAddress,
                Hostname = "-",
                MacAddress = string.IsNullOrWhiteSpace(device.MacAddress) ? "-" : device.MacAddress,
                StatusText = device.IsOnline ? "Online" : "Offline",
                ResponseTimeText = device.ResponseTimeMs.HasValue ? device.ResponseTimeMs.Value.ToString() : "-",
                Vendor = ResolveVendorByMac(device.MacAddress),
                LastSeen = (device.LastSeen ?? scanTime).ToString("HH:mm:ss")
            };
        }

        private static string ResolveVendorByMac(string macAddress)
        {
            if (string.IsNullOrWhiteSpace(macAddress))
                return "Unknown";

            var normalized = macAddress.Replace(":", "-").ToUpperInvariant();
            if (normalized.Length < 8)
                return "Unknown";

            var oui = normalized[..8];
            return oui switch
            {
                "54-04-63" => "MediaTek Inc.",
                "30-4A-26" => "Intel Corporation",
                "6C-22-1A" => "Dell Inc.",
                "E2-A7-C3" => "Apple Inc.",
                "AE-01-A9" => "HP Inc.",
                "5A-D2-5B" => "Samsung Elec.",
                _ => "Unknown"
            };
        }

        private static bool TryGetLocalMac(string interfaceName, out string macAddress)
        {
            macAddress = string.Empty;
            try
            {
                var ni = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(x => x.Name.Equals(interfaceName, StringComparison.OrdinalIgnoreCase));
                var bytes = ni?.GetPhysicalAddress().GetAddressBytes();
                if (bytes == null || bytes.Length == 0)
                    return false;

                macAddress = string.Join("-", bytes.Select(b => b.ToString("X2")));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int GetIpValue(string ipAddress)
        {
            var parts = ipAddress.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 4)
                return int.MaxValue;

            var value = 0;
            foreach (var part in parts)
            {
                if (!int.TryParse(part, out var octet) || octet < 0 || octet > 255)
                    return int.MaxValue;

                value = (value << 8) + octet;
            }

            return value;
        }

        private static uint ToUInt32(IPAddress ipAddress)
        {
            var bytes = ipAddress.GetAddressBytes();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return BitConverter.ToUInt32(bytes, 0);
        }

        private static string ToIpString(uint ipValue)
        {
            var bytes = BitConverter.GetBytes(ipValue);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return new IPAddress(bytes).ToString();
        }

        private static Image? TryLoadAssetImage(string fileName)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    return null;

                using var source = Image.FromFile(path);
                return new Bitmap(source);
            }
            catch
            {
                return null;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _scanCts?.Cancel();
            _scanTimer.Stop();
            _scanTimer.Dispose();
            _scanCts?.Dispose();
            base.OnFormClosing(e);
        }

        private sealed class NetworkDeviceRow
        {
            public string IpAddress { get; init; } = string.Empty;
            public string Hostname { get; init; } = "-";
            public string MacAddress { get; init; } = "-";
            public string StatusText { get; init; } = "-";
            public string ResponseTimeText { get; init; } = "-";
            public string Vendor { get; init; } = "Unknown";
            public string LastSeen { get; init; } = "-";
        }

        private enum DeviceFilter
        {
            All,
            Online,
            Offline
        }
    }

    internal class RoundedPanel : Panel
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int Radius { get; set; } = 8;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color BorderColor { get; set; } = Color.Transparent;

        public RoundedPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var path = CreateRoundPath(rect, Radius);
            using var fill = new SolidBrush(BackColor);
            using var border = new Pen(BorderColor);
            e.Graphics.FillPath(fill, path);
            if (BorderColor != Color.Transparent)
                e.Graphics.DrawPath(border, path);
        }

        public static GraphicsPath CreateRoundPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(1, radius * 2);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class StatIconPanel : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color Accent { get; set; }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string IconName { get; set; } = string.Empty;

        public StatIconPanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var glow = new LinearGradientBrush(ClientRectangle, Color.FromArgb(255, Accent), ControlPaint.Dark(Accent), LinearGradientMode.ForwardDiagonal);
            e.Graphics.FillEllipse(glow, new Rectangle(0, 0, Width - 1, Height - 1));
            DrawIcon(e.Graphics, IconName, new Rectangle(15, 15, Width - 30, Height - 30), Color.White);
        }

        internal static void DrawIcon(Graphics g, string icon, Rectangle r, Color color)
        {
            using var pen = new Pen(color, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var brush = new SolidBrush(color);
            if (icon == "screen")
            {
                g.DrawRectangle(pen, r.X + 2, r.Y + 3, r.Width - 4, r.Height - 8);
                g.DrawLine(pen, r.X + r.Width / 2, r.Bottom - 5, r.X + r.Width / 2, r.Bottom);
                g.DrawLine(pen, r.X + 6, r.Bottom, r.Right - 6, r.Bottom);
            }
            else if (icon == "wifi")
            {
                g.DrawArc(pen, r.X, r.Y + 2, r.Width, r.Height, 210, 120);
                g.DrawArc(pen, r.X + 5, r.Y + 8, r.Width - 10, r.Height - 6, 210, 120);
                g.FillEllipse(brush, r.X + r.Width / 2 - 3, r.Bottom - 2, 6, 6);
            }
            else if (icon == "clock")
            {
                g.DrawEllipse(pen, r);
                g.DrawLine(pen, r.X + r.Width / 2, r.Y + r.Height / 2, r.X + r.Width / 2, r.Y + 7);
                g.DrawLine(pen, r.X + r.Width / 2, r.Y + r.Height / 2, r.Right - 7, r.Y + r.Height / 2 + 5);
            }
            else
            {
                g.FillEllipse(brush, r.X + r.Width / 2 - 5, r.Y + r.Height / 2 - 5, 10, 10);
            }
        }
    }

    internal sealed class SmallIconPanel : Control
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string IconName { get; set; } = string.Empty;

        public SmallIconPanel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(BackColor);
            e.Graphics.FillRectangle(bg, ClientRectangle);
            StatIconPanel.DrawIcon(e.Graphics, IconName == "screen" ? "screen" : "dot", new Rectangle(7, 7, 14, 14), ForeColor);
        }
    }

    internal sealed class SearchGlyph : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(ForeColor, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawEllipse(pen, 2, 2, 10, 10);
            e.Graphics.DrawLine(pen, 11, 11, 17, 17);
        }
    }
}
