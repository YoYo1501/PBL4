using System.Diagnostics;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using NetworkAdmin.Client.Presentation;
using NetworkAdmin.Shared.Models;
using NetworkAdminTool.Models;

namespace NetworkAdminTool.Forms
{
    public class ScannerForm : Form
    {
        private readonly ScannerPresenter _presenter;

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
        private DateTime _lastProgressUiUpdateUtc = DateTime.MinValue;

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

        internal ScannerForm(ScannerPresenter presenter)
        {
            _presenter = presenter;

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
            MakeFilterCard(scannedCard, ScannerDeviceFilter.All);
            MakeFilterCard(onlineCard, ScannerDeviceFilter.Online);
            MakeFilterCard(offlineCard, ScannerDeviceFilter.Offline);

            cardsPanel.Controls.Add(scannedCard, 0, 0);
            cardsPanel.Controls.Add(onlineCard, 1, 0);
            cardsPanel.Controls.Add(offlineCard, 2, 0);
            cardsPanel.Controls.Add(BuildStatCard("Thời gian quét", "", _lblDurationValue, Purple, "clock"), 3, 0);
            return cardsPanel;
        }

        private void MakeFilterCard(Control control, ScannerDeviceFilter filter)
        {
            control.Cursor = Cursors.Hand;
            control.Click += (_, _) =>
            {
                _presenter.SetFilter(filter);
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
            var interfaces = _presenter.GetAvailableInterfaces();
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

            var details = _presenter.GetInterfaceDetails(selected);
            if (!string.IsNullOrWhiteSpace(details.SubnetRange) && !_cboSubnet.Items.Contains(details.SubnetRange))
                _cboSubnet.Items.Insert(0, details.SubnetRange);

            _cboSubnet.Text = details.SubnetRange;
            _lblAdapterValue.Text = details.AdapterDescription;
            _lblIpValue.Text = details.IpAddress;
            _lblGatewayValue.Text = details.Gateway;
            _lblMaskValue.Text = details.SubnetMask;
            _lblMacValue.Text = details.MacAddress;
        }

        private async void BtnStartScan_Click(object? sender, EventArgs e)
        {
            if (_isScanning)
            {
                _scanCts?.Cancel();
                return;
            }

            var subnet = _presenter.ToCidrText(_cboSubnet.Text.Trim());
            if (!_presenter.TryValidateScanSubnet(subnet, out var validationMessage))
            {
                MessageBox.Show(validationMessage, "Th\u00f4ng b\u00e1o", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _scanCts = new CancellationTokenSource();
            _isScanning = true;
            _presenter.BeginScan(subnet);
            _lastProgressUiUpdateUtc = DateTime.MinValue;
            SetScanProgress(0, _presenter.TotalHosts, 0, string.Empty);
            _btnStartScan.Text = "D\u1eebng";
            _scanStopwatch.Restart();
            _scanTimer.Start();
            _lblDurationValue.Text = "00:00:00";
            _lblResultTitle.Text = "K\u1ebft qu\u1ea3 qu\u00e9t (\u0111ang qu\u00e9t...)";

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
                _lblOfflineValue.Text = _presenter.KnownOfflineCount.ToString();
            });

            try
            {
                await _presenter.ScanAsync(subnet, selectedInterface?.Name, _scanCts.Token, progress);
                ApplyFilter();
                UpdateStats();
                SetScanProgress(_presenter.TotalHosts, _presenter.TotalHosts, _presenter.OnlineCount, string.Empty);
            }
            catch (OperationCanceledException)
            {
                _presenter.LogCancelled(subnet);
                _lblResultTitle.Text = $"K\u1ebft qu\u1ea3 qu\u00e9t (\u0111\u00e3 d\u1eebng, {_presenter.OnlineCount} thi\u1ebft b\u1ecb online)";
            }
            catch (Exception ex)
            {
                _presenter.LogFailure(ex.Message);
                MessageBox.Show($"Qu\u00e9t m\u1ea1ng th\u1ea5t b\u1ea1i: {ex.Message}", "L\u1ed7i", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _scanStopwatch.Stop();
                _scanTimer.Stop();
                _lblDurationValue.Text = _scanStopwatch.Elapsed.ToString(@"hh\:mm\:ss");
                _btnStartScan.Text = "Qu\u00e9t";
                _scanCts?.Dispose();
                _scanCts = null;
                _isScanning = false;
                _lblProgressStatus.Text = _scanProgress.Value >= _scanProgress.Maximum
                    ? $"Ho\u00e0n t\u1ea5t: {_presenter.OnlineCount} online, {_presenter.OfflineCount} offline trong {_scanStopwatch.Elapsed:mm\\:ss}"
                    : "S\u1eb5n s\u00e0ng qu\u00e9t";
            }
        }

        private void ApplyFilter()
        {
            var result = _presenter.FilterRows(_txtSearch.Text);
            _gridResults.DataSource = result.Rows.ToList();
            _lblResultTitle.Text = result.Title;
        }

        private void UpdateStats()
        {
            _lblScannedValue.Text = _presenter.ScannedCount.ToString();
            _lblOnlineValue.Text = _presenter.OnlineCount.ToString();
            _lblOfflineValue.Text = _presenter.OfflineCount.ToString();
        }

        private bool LoadLastScanState()
        {
            if (!_presenter.LoadLastScanState())
                return false;

            ApplyFilter();
            UpdateStats();
            _lblDurationValue.Text = "00:00:00";
            _lblProgressStatus.Text = $"K\u1ebft qu\u1ea3 g\u1ea7n nh\u1ea5t: {_presenter.LastScanAt:HH:mm:ss}";
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
