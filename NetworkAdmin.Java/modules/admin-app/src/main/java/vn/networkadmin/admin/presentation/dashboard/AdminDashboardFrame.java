package vn.networkadmin.admin.presentation.dashboard;

import vn.networkadmin.admin.runtime.AdminClientSnapshot;
import vn.networkadmin.admin.application.screenshare.AdminScreenShareUseCases;
import vn.networkadmin.admin.presentation.screenshare.ScreenViewerFrame;
import vn.networkadmin.admin.application.remotecontrol.AdminRemoteControlUseCases;
import vn.networkadmin.admin.presentation.chat.AdminChatDialog;
import vn.networkadmin.admin.presentation.chat.AdminChatPresenter;
import vn.networkadmin.admin.presentation.components.MetricRing;
import vn.networkadmin.admin.presentation.components.DashboardIcon;
import vn.networkadmin.admin.presentation.theme.AdminTheme;

import javax.swing.*;
import javax.swing.border.Border;
import javax.swing.table.DefaultTableCellRenderer;
import javax.swing.table.DefaultTableModel;
import java.awt.*;
import java.awt.geom.Path2D;
import java.awt.geom.RoundRectangle2D;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletionException;

/** Modern Swing dashboard view matching the target administrative UI specification. */
public final class AdminDashboardFrame extends JFrame {
    private final AdminDashboardPresenter presenter;
    private final AdminScreenShareUseCases screenShare;
    private final AdminRemoteControlUseCases remoteControl;
    private final AdminChatPresenter chatPresenter;
    private final Runnable closeAction;
    private final String tlsFingerprint;

    private final JComboBox<ClientChoice> clientPicker = new JComboBox<>();
    private final JLabel serverState = label("● Server Running", AdminTheme.GREEN, AdminTheme.MEDIUM);
    private final JLabel serverPort = label("Port: 5000 (TLS)", new Color(203, 213, 225), new Font("Segoe UI", Font.PLAIN, 12));
    private final JLabel serverInterfaces = label("Interfaces: 1", new Color(203, 213, 225), new Font("Segoe UI", Font.PLAIN, 12));
    private final JLabel headerServerState = label("● Server Running", AdminTheme.GREEN, AdminTheme.MEDIUM);
    private final JPanel statePill = new RoundedPanel(16, AdminTheme.GREEN_BG, AdminTheme.GREEN_BORDER);
    private final JLabel headerOnlineCount = label("0", AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 24));
    private final JLabel clock = label("", AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 17));
    private final JLabel date = label("", AdminTheme.MUTED, AdminTheme.SMALL);
    private final JLabel pageTitle = label("Network Administration", AdminTheme.TEXT, AdminTheme.TITLE);
    private final JLabel pageSubtitle = label("Manage and monitor client computers in the network", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel pageHeaderIcon = new JLabel();
    private final JPanel pageHeaderIconTile = new RoundedPanel(16, AdminTheme.BLUE_BG, null);

    private final JLabel managedCount = label("0", AdminTheme.TEXT, AdminTheme.METRIC);
    private final JLabel onlineCount = label("0", AdminTheme.TEXT, AdminTheme.METRIC);
    private final JLabel hostsCount = label("0", AdminTheme.TEXT, AdminTheme.METRIC);
    private final JLabel pingAverage = label("--", AdminTheme.TEXT, AdminTheme.METRIC);

    private final JLabel clientName = label("DESKTOP-EU0OD66", AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 22));
    private final JPanel clientBadgePill = new RoundedPanel(12, AdminTheme.GREEN, null);
    private final JLabel clientBadgeText = label("Online", Color.WHITE, new Font("Segoe UI", Font.BOLD, 15));

    private final JLabel clientOs = label("Windows 11 Pro", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel clientId = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel sessionId = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel connectedAt = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel lastSeen = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);

    private final JLabel interfaceName = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel primaryIpv4 = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel primaryMac = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel primaryGateway = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel primarySubnet = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);

    private final JLabel pingSent = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel pingReceived = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel pingLost = label("--", AdminTheme.PINK, AdminTheme.MEDIUM);
    private final JLabel pingLoss = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel pingSentStat = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel pingReceivedStat = label("--", AdminTheme.GREEN, AdminTheme.MEDIUM);
    private final JLabel pingLostStat = label("--", AdminTheme.PINK, AdminTheme.MEDIUM);
    private final JLabel pingLossStat = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel pingRtt = label("-- ms", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel pingMin = label("-- ms", AdminTheme.GREEN, AdminTheme.METRIC);
    private final JLabel pingMax = label("-- ms", new Color(217, 119, 6), AdminTheme.METRIC);
    private final JLabel pingResultStatus = label("No ping has been run yet.", AdminTheme.MUTED, AdminTheme.MEDIUM);
    private final JLabel scanFound = label("0", AdminTheme.TEXT, AdminTheme.METRIC);
    private final JLabel scanOnline = label("0", AdminTheme.GREEN, AdminTheme.METRIC);
    private final JLabel scanOffline = label("0", AdminTheme.PINK, AdminTheme.METRIC);
    private final JLabel scanDuration = label("--", new Color(217, 119, 6), AdminTheme.METRIC);
    private final JLabel scanResultStatus = label("No scan has been run yet.", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel monitorCpuValue = label("--", AdminTheme.TEXT, AdminTheme.METRIC);
    private final JLabel monitorRamValue = label("--", AdminTheme.TEXT, AdminTheme.METRIC);
    private final JLabel monitorNetworkValue = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel monitorClientValue = label("Waiting", AdminTheme.MUTED, AdminTheme.METRIC);
    private final JLabel monitorOsName = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorOsVersion = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorArchitecture = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorInterface = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorIpAddress = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorMacAddress = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorGateway = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorSubnet = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorConnectionStatus = label("Waiting for client", AdminTheme.MUTED, AdminTheme.MEDIUM);
    private final JLabel monitorCpuStatus = label("Waiting for telemetry", AdminTheme.MUTED, AdminTheme.MEDIUM);
    private final JLabel monitorRamStatus = label("Waiting for telemetry", AdminTheme.MUTED, AdminTheme.MEDIUM);
    private final JLabel monitorCpuStatusValue = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel monitorRamStatusValue = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel monitorNetworkStatusValue = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel monitorSystemInterface = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorSystemIpAddress = label("--", AdminTheme.TEXT, AdminTheme.REGULAR);
    private final JLabel monitorLastSeen = label("--", AdminTheme.MUTED, AdminTheme.REGULAR);
    private JComponent pingChart;

    private final JLabel operationStatus = label("Ready", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JTextField subnetField = field("Subnet (blank = client local network)");
    private final JComboBox<String> scanTimeout = new JComboBox<>(new String[]{"500 ms", "800 ms", "1000 ms", "1500 ms"});
    private final JTextField targetField = field("Target IP or hostname");
    private final JTextArea pingOutput = new JTextArea();
    private final JPanel pingOutcomePanel = new RoundedPanel(10, AdminTheme.GREEN_BG, AdminTheme.GREEN_BORDER);
    private final JButton scanButton = actionButton("Scan Now", DashboardIcon.Kind.PLAY);
    private final JButton cancelScanButton = neutralButton("Cancel", DashboardIcon.Kind.CROSS);
    private final JButton pingButton = actionButton("Ping", DashboardIcon.Kind.PLAY);

    private final JButton headerChatButton = createHeaderChatButton();
    private final java.util.Set<String> readMessageIds = java.util.Collections.synchronizedSet(new java.util.HashSet<>());
    private final java.util.Set<String> openChatSessions = java.util.Collections.synchronizedSet(new java.util.HashSet<>());
    private final java.util.Map<String, Integer> unreadBySession = new java.util.concurrent.ConcurrentHashMap<>();
    private int unreadChatCount = 0;
    private JButton sidebarChatButton;
    private JButton sidebarViewScreenButton;
    private JButton sidebarRemoteAdministrationButton;
    private final java.util.Map<String, JButton> sidebarNavButtons = new java.util.LinkedHashMap<>();
    private JComponent scanSection;
    private JComponent pingSection;
    private boolean serverActivityInitialized;
    private final CardLayout featureLayout = new CardLayout();
    private JPanel featurePages;
    private JPanel selectionWrap;
    private JPanel remoteAdminActions;
    private JLabel remoteClientNameValue;
    private JLabel remoteSessionValue;
    private JLabel remoteClientStatusValue;
    private JLabel remoteOsValue;
    private JLabel remoteCpuValue;
    private JLabel remoteRamValue;

    private JButton createHeaderChatButton() {
        JButton button = new JButton("Chat", new DashboardIcon(DashboardIcon.Kind.CHAT, AdminTheme.TEXT, 18)) {
            @Override
            protected void paintComponent(Graphics g) {
                super.paintComponent(g);
                if (unreadChatCount > 0) {
                    Graphics2D g2 = (Graphics2D) g.create();
                    g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                    String countStr = unreadChatCount > 99 ? "99+" : String.valueOf(unreadChatCount);
                    g2.setFont(new Font("Segoe UI", Font.BOLD, 10));
                    FontMetrics fm = g2.getFontMetrics();
                    int bw = Math.max(17, fm.stringWidth(countStr) + 8);
                    int bh = 17;
                    int bx = getWidth() - bw - 2;
                    int by = 2;
                    g2.setColor(AdminTheme.PINK);
                    g2.fillRoundRect(bx, by, bw, bh, 9, 9);
                    g2.setColor(Color.WHITE);
                    g2.drawString(countStr, bx + (bw - fm.stringWidth(countStr)) / 2, by + fm.getAscent() - 1);
                    g2.dispose();
                }
            }
        };
        button.setBackground(Color.WHITE);
        button.setForeground(AdminTheme.TEXT);
        button.setFont(AdminTheme.MEDIUM);
        button.setFocusPainted(false);
        button.setOpaque(false);
        button.setBorder(BorderFactory.createCompoundBorder(
                new RoundedBorder(AdminTheme.BORDER, 8),
                BorderFactory.createEmptyBorder(6, 12, 6, 14)));
        button.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        button.addActionListener(e -> openChat());
        return button;
    }

    private final MetricRing cpuRing = new MetricRing("CPU Usage", AdminTheme.BLUE, DashboardIcon.Kind.CPU);
    private final MetricRing ramRing = new MetricRing("RAM Usage", AdminTheme.PURPLE, DashboardIcon.Kind.RAM);
    private final MonitoringChart telemetryChart = new MonitoringChart();
    private final MetricRing detailCpuRing = new MetricRing("CPU Usage", AdminTheme.BLUE, DashboardIcon.Kind.CPU);
    private final MetricRing detailRamRing = new MetricRing("RAM Usage", AdminTheme.PURPLE, DashboardIcon.Kind.RAM);
    private final MonitoringChart detailTelemetryChart = new MonitoringChart();

    private final DefaultTableModel scanModel = model("#", "IP Address", "MAC Address", "Status", "Response Time");
    private final DefaultTableModel pingModel = model("#", "Status", "Response Time");

    private final DateTimeFormatter timeFormat = DateTimeFormatter.ofPattern("hh:mm:ss a", java.util.Locale.US);
    private final DateTimeFormatter dateFormat = DateTimeFormatter.ofPattern("EEE, MMM d, yyyy", java.util.Locale.US);
    private final javax.swing.Timer refreshTimer;
    private final String bindDescription;
    private List<AdminClientSnapshot> currentClients = List.of();
    private boolean refreshingClients;
    private boolean operationsBusy;
    private boolean scanInProgress;
    private java.time.Instant lastTelemetrySampleAt;
    private String activeScanSession;
    private volatile String activeRemoteRequestId;

    public AdminDashboardFrame(AdminDashboardPresenter presenter, AdminScreenShareUseCases screenShare,
                               AdminRemoteControlUseCases remoteControl, AdminChatPresenter chatPresenter,
                               String bindDescription, String tlsFingerprint, Runnable closeAction) {
        super("Network Administration");
        this.presenter = java.util.Objects.requireNonNull(presenter);
        this.screenShare = java.util.Objects.requireNonNull(screenShare);
        this.remoteControl = java.util.Objects.requireNonNull(remoteControl);
        this.chatPresenter = java.util.Objects.requireNonNull(chatPresenter);
        this.bindDescription = bindDescription;
        this.tlsFingerprint = tlsFingerprint == null || tlsFingerprint.isBlank()
                || tlsFingerprint.toLowerCase(java.util.Locale.ROOT).startsWith("replace-with")
                ? "" : tlsFingerprint.trim();
        this.closeAction = java.util.Objects.requireNonNull(closeAction);

        setIconImage(DashboardIcon.createGlobeImage(32, AdminTheme.BLUE));
        setDefaultCloseOperation(WindowConstants.DO_NOTHING_ON_CLOSE);
        setMinimumSize(new Dimension(1180, 760));
        setSize(1580, 980);
        setLocationRelativeTo(null);
        setExtendedState(JFrame.MAXIMIZED_BOTH);

        initCustomClientPicker();

        setContentPane(buildRoot());
        clientPicker.addActionListener(e -> refreshSelectedClient());
        scanButton.addActionListener(e -> runScan());
        pingButton.addActionListener(e -> runPing());
        cancelScanButton.setEnabled(false);
        cancelScanButton.addActionListener(e -> cancelScan());

        addWindowListener(new java.awt.event.WindowAdapter() {
            @Override public void windowClosing(java.awt.event.WindowEvent e) { closeApp(); }
        });

        cpuRing.setUnavailable();
        ramRing.setUnavailable();
        detailCpuRing.setUnavailable();
        detailRamRing.setUnavailable();

        refreshTimer = new javax.swing.Timer(1000, e -> refreshDashboard());
        refreshTimer.start();
        refreshDashboard();
    }

    private void initCustomClientPicker() {
        clientPicker.setFont(AdminTheme.REGULAR);
        clientPicker.setBackground(Color.WHITE);
        clientPicker.setForeground(AdminTheme.TEXT);
        clientPicker.setPreferredSize(new Dimension(520, 32));
        clientPicker.setRenderer(new DefaultListCellRenderer() {
            @Override
            public Component getListCellRendererComponent(JList<?> list, Object value, int index, boolean isSelected, boolean cellHasFocus) {
                JPanel panel = new JPanel(new FlowLayout(FlowLayout.LEFT, 6, 4));
                panel.setOpaque(true);
                panel.setBackground(isSelected ? new Color(239, 246, 255) : Color.WHITE);
                if (value instanceof ClientChoice choice) {
                    JLabel nameLbl = new JLabel(choice.clientName);
                    nameLbl.setFont(AdminTheme.MEDIUM);
                    nameLbl.setForeground(AdminTheme.TEXT);
                    panel.add(nameLbl);

                    JLabel sep1 = new JLabel("|");
                    sep1.setForeground(new Color(203, 213, 225));
                    panel.add(sep1);

                    JLabel statusLbl = new JLabel(choice.online ? "Online" : "Offline");
                    statusLbl.setFont(AdminTheme.MEDIUM);
                    statusLbl.setForeground(choice.online ? AdminTheme.GREEN : AdminTheme.PINK);
                    panel.add(statusLbl);

                    int unread = unreadBySession.getOrDefault(choice.sessionId, 0);
                    if (unread > 0) {
                        JLabel unreadLbl = new JLabel(unread > 99 ? "99+" : Integer.toString(unread));
                        unreadLbl.setOpaque(true);
                        unreadLbl.setBackground(AdminTheme.PINK);
                        unreadLbl.setForeground(Color.WHITE);
                        unreadLbl.setFont(new Font("Segoe UI", Font.BOLD, 10));
                        unreadLbl.setBorder(BorderFactory.createEmptyBorder(2, 6, 2, 6));
                        panel.add(unreadLbl);
                    }

                    JLabel sep2 = new JLabel("|");
                    sep2.setForeground(new Color(203, 213, 225));
                    panel.add(sep2);

                    String sId = choice.sessionId.length() > 8 ? choice.sessionId.substring(0, 8) : choice.sessionId;
                    JLabel sessLbl = new JLabel("Session: " + sId);
                    sessLbl.setFont(AdminTheme.REGULAR);
                    sessLbl.setForeground(AdminTheme.MUTED);
                    panel.add(sessLbl);
                } else if (value != null) {
                    JLabel lbl = new JLabel(value.toString());
                    lbl.setFont(AdminTheme.REGULAR);
                    lbl.setForeground(AdminTheme.TEXT);
                    panel.add(lbl);
                }
                return panel;
            }
        });
    }

    private JComponent buildRoot() {
        JPanel root = new JPanel(new BorderLayout());
        root.setBackground(AdminTheme.BACKGROUND);
        root.add(buildSidebar(), BorderLayout.WEST);

        JPanel main = new JPanel(new BorderLayout());
        main.setBackground(AdminTheme.BACKGROUND);
        main.add(buildHeader(), BorderLayout.NORTH);

        buildOperationsRow(); // Builds the existing scan and ping panels for their dedicated pages.
        featurePages = new JPanel(featureLayout);
        featurePages.setBackground(AdminTheme.BACKGROUND);
        featurePages.add(wrapPage(buildDashboardContent()), "Dashboard");
        featurePages.add(wrapPage(buildFeatureContent("IP Scanner", scanSection)), "IP Scanner");
        featurePages.add(wrapPage(buildFeatureContent("Ping Tool", pingSection)), "Ping Tool");
        featurePages.add(wrapPage(buildMonitoringContent()), "Monitoring");
        featurePages.add(wrapPage(buildRemoteAdministrationContent()), "Remote Administration");
        featurePages.add(wrapPage(buildSettingsContent()), "Settings");
        featurePages.add(wrapPage(buildAboutContent()), "About");
        featureLayout.show(featurePages, "Dashboard");

        JPanel center = new JPanel(new BorderLayout());
        center.setBackground(AdminTheme.BACKGROUND);
        JComponent selection = buildSelectionBar();
        selectionWrap = new JPanel(new BorderLayout());
        selectionWrap.setOpaque(false);
        selectionWrap.setBorder(BorderFactory.createEmptyBorder(8, 20, 4, 20));
        selectionWrap.add(selection, BorderLayout.CENTER);
        center.add(selectionWrap, BorderLayout.NORTH);
        center.add(featurePages, BorderLayout.CENTER);
        main.add(center, BorderLayout.CENTER);
        main.add(buildFooter(), BorderLayout.SOUTH);

        root.add(main, BorderLayout.CENTER);
        return root;
    }

    private JScrollPane wrapPage(JComponent page) {
        JScrollPane scroll = new JScrollPane(page);
        scroll.setBorder(BorderFactory.createEmptyBorder());
        scroll.getViewport().setBackground(AdminTheme.BACKGROUND);
        scroll.getVerticalScrollBar().setUnitIncrement(18);
        return scroll;
    }

    private JComponent buildDashboardContent() {
        JPanel content = new ScrollablePanel();
        content.setBackground(AdminTheme.BACKGROUND);
        content.setBorder(BorderFactory.createEmptyBorder(8, 20, 16, 20));
        content.setLayout(new BoxLayout(content, BoxLayout.Y_AXIS));
        for (JComponent section : List.of(buildSummaryCards(), buildMainRow(), buildDashboardBottom())) {
            section.setAlignmentX(Component.LEFT_ALIGNMENT);
            content.add(section);
            content.add(Box.createVerticalStrut(12));
        }
        return content;
    }

    private JComponent buildFeatureContent(String title, JComponent section) {
        JPanel content = new JPanel(new BorderLayout());
        content.setBackground(AdminTheme.BACKGROUND);
        content.setBorder(BorderFactory.createEmptyBorder(12, 20, 16, 20));
        content.add(section, BorderLayout.CENTER);
        return content;
    }

    private JComponent buildSettingsContent() {
        ScrollablePanel page = new ScrollablePanel();
        page.setLayout(new BoxLayout(page, BoxLayout.Y_AXIS));
        page.setBackground(AdminTheme.BACKGROUND);
        page.setBorder(BorderFactory.createEmptyBorder(12, 20, 20, 20));

        Color green = new Color(22, 163, 74);
        Color greenBg = new Color(240, 253, 244);
        Color greenBorder = new Color(209, 250, 229);
        Color purpleBg = new Color(250, 245, 255);
        Color purpleBorder = new Color(233, 213, 255);

        JPanel connection = new RoundedPanel(18, Color.WHITE, new Color(220, 232, 250));
        connection.setLayout(new BoxLayout(connection, BoxLayout.Y_AXIS));
        connection.setBorder(BorderFactory.createEmptyBorder(32, 36, 58, 34));
        connection.add(settingsHeading("Server Connection", "Configuration of the Admin server and secure communication.",
                DashboardIcon.Kind.SERVER, AdminTheme.BLUE, AdminTheme.BLUE_BG));
        connection.add(Box.createVerticalStrut(42));
        JPanel connectionTiles = new JPanel(new GridBagLayout());
        connectionTiles.setOpaque(false);
        GridBagConstraints tileConstraints = new GridBagConstraints();
        tileConstraints.gridy = 0;
        tileConstraints.fill = GridBagConstraints.BOTH;
        tileConstraints.weighty = 1;
        tileConstraints.insets = new Insets(0, 0, 0, 16);
        tileConstraints.gridx = 0;
        tileConstraints.weightx = 1.1;
        connectionTiles.add(settingsTile("Listener", bindDescription, DashboardIcon.Kind.RADIO,
                AdminTheme.BLUE, AdminTheme.BLUE_BG, AdminTheme.BLUE_BORDER), tileConstraints);
        tileConstraints.gridx = 1;
        tileConstraints.weightx = 1;
        connectionTiles.add(settingsTile("Transport Security", "TLS enabled", DashboardIcon.Kind.SHIELD,
                green, greenBg, greenBorder), tileConstraints);
        tileConstraints.gridx = 2;
        tileConstraints.weightx = 1;
        tileConstraints.insets = new Insets(0, 0, 0, 0);
        connectionTiles.add(settingsTile("Control Channel", "Encrypted client sessions", DashboardIcon.Kind.LOCK,
                AdminTheme.PURPLE, purpleBg, purpleBorder), tileConstraints);
        connectionTiles.setPreferredSize(new Dimension(1200, 145));
        connectionTiles.setMaximumSize(new Dimension(Integer.MAX_VALUE, 145));
        connection.add(connectionTiles);
        connection.setPreferredSize(new Dimension(1200, 360));
        connection.setMaximumSize(new Dimension(Integer.MAX_VALUE, 360));
        connection.setAlignmentX(Component.LEFT_ALIGNMENT);
        page.add(connection);
        page.add(Box.createVerticalStrut(18));

        JPanel certificate = card();
        certificate.setLayout(new BoxLayout(certificate, BoxLayout.Y_AXIS));
        certificate.setBorder(BorderFactory.createEmptyBorder(20, 20, 20, 20));
        String fingerprintStatus = tlsFingerprint.isEmpty()
                ? "No SHA-256 fingerprint is configured for client certificate pinning."
                : "Clients use this SHA-256 fingerprint to verify the Admin server.";
        JPanel certificateHeading = settingsHeading("TLS Certificate Pinning", fingerprintStatus,
                DashboardIcon.Kind.CERTIFICATE, AdminTheme.BLUE, AdminTheme.BLUE_BG);
        certificateHeading.setAlignmentX(Component.LEFT_ALIGNMENT);
        certificate.add(certificateHeading);
        certificate.add(Box.createVerticalStrut(18));

        JPanel fingerprintCard = new RoundedPanel(14, Color.WHITE, AdminTheme.BORDER);
        fingerprintCard.setLayout(new BorderLayout(0, 12));
        fingerprintCard.setBorder(BorderFactory.createCompoundBorder(new RoundedBorder(AdminTheme.BORDER, 14),
                BorderFactory.createEmptyBorder(18, 18, 16, 18)));
        fingerprintCard.add(label("SHA-256 Fingerprint", AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 19)),
                BorderLayout.NORTH);

        JPanel fingerprintRow = new JPanel(new BorderLayout(14, 0));
        fingerprintRow.setOpaque(false);
        fingerprintRow.setPreferredSize(new Dimension(1000, 58));
        fingerprintRow.setMaximumSize(new Dimension(Integer.MAX_VALUE, 58));
        JTextField fingerprintValue = new JTextField(tlsFingerprint.isEmpty() ? "Not configured" : tlsFingerprint);
        fingerprintValue.setEditable(false);
        fingerprintValue.setFocusable(true);
        fingerprintValue.setFont(new Font(Font.MONOSPACED, Font.BOLD, 16));
        fingerprintValue.setForeground(tlsFingerprint.isEmpty() ? AdminTheme.MUTED : AdminTheme.TEXT);
        fingerprintValue.setBackground(new Color(248, 250, 255));
        fingerprintValue.setBorder(BorderFactory.createCompoundBorder(new RoundedBorder(new Color(205, 220, 244), 10),
                BorderFactory.createEmptyBorder(10, 12, 10, 12)));
        fingerprintRow.add(fingerprintValue, BorderLayout.CENTER);
        JButton copyFingerprint = neutralButton("Copy", DashboardIcon.Kind.COPY);
        copyFingerprint.setIcon(new DashboardIcon(DashboardIcon.Kind.COPY, AdminTheme.BLUE, 20));
        copyFingerprint.setFont(AdminTheme.MEDIUM);
        copyFingerprint.setForeground(AdminTheme.BLUE);
        copyFingerprint.setBackground(new Color(239, 246, 255));
        copyFingerprint.setBorder(BorderFactory.createCompoundBorder(
                new RoundedBorder(new Color(147, 197, 253), 12), BorderFactory.createEmptyBorder(10, 16, 10, 16)));
        copyFingerprint.setPreferredSize(new Dimension(136, 58));
        copyFingerprint.setMaximumSize(new Dimension(150, 58));
        copyFingerprint.setEnabled(!tlsFingerprint.isEmpty());
        copyFingerprint.addActionListener(event -> {
            try {
                Toolkit.getDefaultToolkit().getSystemClipboard().setContents(
                        new java.awt.datatransfer.StringSelection(tlsFingerprint), null);
                operationStatus.setText("TLS SHA-256 fingerprint copied");
            } catch (HeadlessException ignored) {
                operationStatus.setText("Clipboard is not available");
            }
        });
        fingerprintRow.add(copyFingerprint, BorderLayout.EAST);
        JPanel fingerprintBody = new JPanel(new BorderLayout(0, 14));
        fingerprintBody.setOpaque(false);
        fingerprintBody.setLayout(new GridBagLayout());
        GridBagConstraints fingerprintConstraints = new GridBagConstraints();
        fingerprintConstraints.gridx = 0;
        fingerprintConstraints.weightx = 1;
        fingerprintConstraints.fill = GridBagConstraints.HORIZONTAL;
        fingerprintConstraints.anchor = GridBagConstraints.NORTH;
        fingerprintBody.add(fingerprintRow, fingerprintConstraints);
        fingerprintConstraints.gridy = 1;
        fingerprintConstraints.weighty = 1;
        fingerprintConstraints.insets = new Insets(18, 0, 14, 0);
        JSeparator divider = new JSeparator();
        divider.setForeground(AdminTheme.BORDER);
        fingerprintBody.add(divider, fingerprintConstraints);

        JPanel privacyRow = new JPanel(new BorderLayout(12, 0));
        privacyRow.setOpaque(false);
        JPanel privacyLabel = new JPanel(new FlowLayout(FlowLayout.LEFT, 10, 0));
        privacyLabel.setOpaque(false);
        privacyLabel.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.KEY, AdminTheme.BLUE, 24)));
        privacyLabel.add(label("Private Key and Keystore Password", AdminTheme.TEXT, AdminTheme.REGULAR));
        privacyRow.add(privacyLabel, BorderLayout.WEST);
        JPanel protectedBadge = new RoundedPanel(18, new Color(241, 245, 251), null);
        protectedBadge.setLayout(new FlowLayout(FlowLayout.CENTER, 9, 8));
        protectedBadge.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.EYE_OFF, AdminTheme.MUTED, 20)));
        protectedBadge.add(label("Never displayed", AdminTheme.MUTED, AdminTheme.MEDIUM));
        protectedBadge.setPreferredSize(new Dimension(210, 52));
        protectedBadge.setMaximumSize(new Dimension(230, 56));
        privacyRow.add(protectedBadge, BorderLayout.EAST);
        fingerprintConstraints.gridy = 2;
        fingerprintConstraints.weighty = 0;
        fingerprintConstraints.insets = new Insets(0, 0, 0, 0);
        fingerprintConstraints.anchor = GridBagConstraints.SOUTH;
        fingerprintBody.add(privacyRow, fingerprintConstraints);
        fingerprintCard.add(fingerprintBody, BorderLayout.CENTER);
        fingerprintCard.setPreferredSize(new Dimension(1200, 300));
        fingerprintCard.setMaximumSize(new Dimension(Integer.MAX_VALUE, 300));
        fingerprintCard.setAlignmentX(Component.LEFT_ALIGNMENT);
        certificate.add(fingerprintCard);
        certificate.setPreferredSize(new Dimension(1200, 440));
        certificate.setMaximumSize(new Dimension(Integer.MAX_VALUE, 440));
        certificate.setAlignmentX(Component.LEFT_ALIGNMENT);
        page.add(certificate);
        return page;
    }

    private JComponent buildAboutContent() {
        ScrollablePanel page = new ScrollablePanel();
        page.setLayout(new BoxLayout(page, BoxLayout.Y_AXIS));
        page.setBackground(AdminTheme.BACKGROUND);
        page.setBorder(BorderFactory.createEmptyBorder(5, 20, 20, 20));

        JPanel overview = card();
        overview.setLayout(new BorderLayout(28, 0));
        overview.setBorder(BorderFactory.createCompoundBorder(new RoundedBorder(AdminTheme.BORDER, 16),
                BorderFactory.createEmptyBorder(24, 26, 24, 26)));
        JPanel productIcon = new RoundedPanel(24, AdminTheme.BLUE_BG, null);
        productIcon.setLayout(new GridBagLayout());
        productIcon.setPreferredSize(new Dimension(112, 112));
        productIcon.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.SCREEN, AdminTheme.BLUE, 70)));
        JPanel productIconSlot = new JPanel(new GridBagLayout());
        productIconSlot.setOpaque(false);
        productIconSlot.setPreferredSize(new Dimension(112, 170));
        productIconSlot.add(productIcon);
        overview.add(productIconSlot, BorderLayout.WEST);

        JPanel identity = new JPanel();
        identity.setOpaque(false);
        identity.setLayout(new BoxLayout(identity, BoxLayout.Y_AXIS));
        JLabel aboutProductTitle = label("NetworkAdmin Java", AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 30));
        aboutProductTitle.setAlignmentX(Component.LEFT_ALIGNMENT);
        identity.add(aboutProductTitle);
        identity.add(Box.createVerticalStrut(8));
        JLabel aboutDescription1 = label("A network monitoring and remote administration", AdminTheme.MUTED, AdminTheme.REGULAR);
        aboutDescription1.setAlignmentX(Component.LEFT_ALIGNMENT);
        identity.add(aboutDescription1);
        JLabel aboutDescription2 = label("application for managed clients.", AdminTheme.MUTED, AdminTheme.REGULAR);
        aboutDescription2.setAlignmentX(Component.LEFT_ALIGNMENT);
        identity.add(aboutDescription2);
        identity.add(Box.createVerticalStrut(24));
        JPanel productMeta = new JPanel(new FlowLayout(FlowLayout.LEFT, 14, 0));
        productMeta.setOpaque(false);
        JPanel version = new RoundedPanel(20, AdminTheme.BLUE_BG, null);
        version.setBorder(BorderFactory.createEmptyBorder(8, 16, 8, 16));
        version.add(label("Version 2.0", AdminTheme.BLUE, AdminTheme.MEDIUM));
        productMeta.add(version);
        JSeparator metaDivider = new JSeparator(SwingConstants.VERTICAL);
        metaDivider.setPreferredSize(new Dimension(1, 34));
        productMeta.add(metaDivider);
        productMeta.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.JAVA, new Color(225, 91, 24), 34)));
        productMeta.add(label("Java (Swing)", AdminTheme.TEXT, AdminTheme.REGULAR));
        productMeta.setAlignmentX(Component.LEFT_ALIGNMENT);
        identity.add(productMeta);
        overview.add(identity, BorderLayout.CENTER);
        JPanel networkArt = new NetworkDiagram();
        networkArt.setPreferredSize(new Dimension(900, 170));
        JPanel illustrationPanel = new JPanel(new BorderLayout(18, 0));
        illustrationPanel.setOpaque(false);
        JSeparator illustrationDivider = new JSeparator(SwingConstants.VERTICAL);
        illustrationDivider.setForeground(new Color(190, 213, 248));
        illustrationDivider.setPreferredSize(new Dimension(1, 170));
        illustrationPanel.add(illustrationDivider, BorderLayout.WEST);
        illustrationPanel.add(networkArt, BorderLayout.CENTER);
        illustrationPanel.setPreferredSize(new Dimension(920, 170));
        overview.add(illustrationPanel, BorderLayout.EAST);
        overview.setPreferredSize(new Dimension(1200, 224));
        overview.setMaximumSize(new Dimension(Integer.MAX_VALUE, 224));
        overview.setAlignmentX(Component.LEFT_ALIGNMENT);
        page.add(overview);
        page.add(Box.createVerticalStrut(16));

        JPanel lower = new JPanel(new GridBagLayout());
        lower.setOpaque(false);
        lower.setAlignmentX(Component.LEFT_ALIGNMENT);
        GridBagConstraints lowerC = new GridBagConstraints();
        lowerC.gridy = 0;
        lowerC.fill = GridBagConstraints.BOTH;
        lowerC.weighty = 1;
        lowerC.insets = new Insets(0, 0, 0, 20);
        lowerC.gridx = 0;
        lowerC.weightx = 0.92;
        JPanel features = aboutCard("Main Features", DashboardIcon.Kind.MONITORING);
        features.setBorder(BorderFactory.createCompoundBorder(new RoundedBorder(AdminTheme.BORDER, 16),
                BorderFactory.createEmptyBorder(18, 22, 16, 22)));
        features.add(aboutFeature(DashboardIcon.Kind.SCAN, "IP Scanner", "Discover hosts on a local network."));
        features.add(aboutFeature(DashboardIcon.Kind.PING, "Ping Tool", "Measure reachability and round-trip time."));
        features.add(aboutFeature(DashboardIcon.Kind.MONITORING, "Monitoring", "View live client CPU and memory telemetry."));
        features.add(aboutFeature(DashboardIcon.Kind.REFRESH, "Remote Administration", "Lock, shut down, or restart a selected client."));
        features.add(aboutFeature(DashboardIcon.Kind.SCREEN, "View Screen", "View a client screen remotely."));
        features.add(aboutFeature(DashboardIcon.Kind.CHAT, "Chat", "Exchange messages with a client."));
        lower.add(features, lowerC);

        lowerC.gridx = 1;
        lowerC.weightx = 1.08;
        lowerC.insets = new Insets(0, 0, 0, 0);
        JPanel rightColumn = new JPanel();
        rightColumn.setOpaque(false);
        rightColumn.setLayout(new BoxLayout(rightColumn, BoxLayout.Y_AXIS));
        rightColumn.setAlignmentX(Component.LEFT_ALIGNMENT);
        JPanel certificate = aboutCard("Certificate", DashboardIcon.Kind.SHIELD);
        JLabel certificateLine1 = label("Secure the connection between the server and clients using TLS.",
                AdminTheme.MUTED, new Font("Segoe UI", Font.PLAIN, 16));
        certificateLine1.setAlignmentX(Component.LEFT_ALIGNMENT);
        certificate.add(certificateLine1);
        JLabel certificateLine2 = label("Clients verify the server certificate via its SHA-256 fingerprint.",
                AdminTheme.MUTED, new Font("Segoe UI", Font.PLAIN, 16));
        certificateLine2.setAlignmentX(Component.LEFT_ALIGNMENT);
        certificate.add(certificateLine2);
        certificate.add(Box.createVerticalStrut(14));
        JPanel fingerprintBox = new RoundedPanel(12, new Color(242, 247, 255), new Color(214, 229, 251));
        fingerprintBox.setLayout(new BorderLayout(12, 0));
        fingerprintBox.setBorder(BorderFactory.createEmptyBorder(12, 14, 12, 14));
        fingerprintBox.setAlignmentX(Component.LEFT_ALIGNMENT);
        fingerprintBox.setMaximumSize(new Dimension(Integer.MAX_VALUE, 84));
        fingerprintBox.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.KEY, AdminTheme.BLUE, 34)), BorderLayout.WEST);
        JPanel fingerprintText = new JPanel();
        fingerprintText.setOpaque(false);
        fingerprintText.setLayout(new BoxLayout(fingerprintText, BoxLayout.Y_AXIS));
        fingerprintText.add(label("TLS Certificate (SHA-256)", AdminTheme.TEXT, AdminTheme.MEDIUM));
        String shortFingerprint = tlsFingerprint.isEmpty() ? "Not configured" :
                (tlsFingerprint.length() > 43 ? tlsFingerprint.substring(0, 40) + "..." : tlsFingerprint);
        JLabel fpValue = label(shortFingerprint, AdminTheme.TEXT, new Font("Segoe UI", Font.PLAIN, 15));
        fpValue.setToolTipText(tlsFingerprint);
        fpValue.setBorder(BorderFactory.createCompoundBorder(new RoundedBorder(new Color(207, 224, 250), 8),
                BorderFactory.createEmptyBorder(6, 9, 6, 9)));
        fpValue.setOpaque(true);
        fpValue.setBackground(Color.WHITE);
        fingerprintText.add(Box.createVerticalStrut(5));
        fingerprintText.add(fpValue);
        fingerprintBox.add(fingerprintText, BorderLayout.CENTER);
        JButton copyFp = new JButton(new DashboardIcon(DashboardIcon.Kind.COPY, AdminTheme.BLUE, 22));
        copyFp.setToolTipText("Copy SHA-256 fingerprint");
        copyFp.setPreferredSize(new Dimension(44, 42));
        copyFp.setBackground(Color.WHITE);
        copyFp.setForeground(AdminTheme.BLUE);
        copyFp.setBorder(new RoundedBorder(new Color(207, 224, 250), 9));
        copyFp.setEnabled(!tlsFingerprint.isEmpty());
        copyFp.addActionListener(event -> {
            try {
                Toolkit.getDefaultToolkit().getSystemClipboard().setContents(
                        new java.awt.datatransfer.StringSelection(tlsFingerprint), null);
                operationStatus.setText("TLS SHA-256 fingerprint copied");
            } catch (HeadlessException ignored) {
                operationStatus.setText("Clipboard is not available");
            }
        });
        fingerprintBox.add(copyFp, BorderLayout.EAST);
        certificate.add(fingerprintBox);
        certificate.setPreferredSize(new Dimension(600, 230));
        certificate.setMaximumSize(new Dimension(Integer.MAX_VALUE, 230));
        rightColumn.add(certificate);
        rightColumn.add(Box.createVerticalStrut(14));
        JPanel appInfo = aboutCard("Application Information", DashboardIcon.Kind.ABOUT);
        appInfo.add(aboutInfoRow(DashboardIcon.Kind.CODE, "Technology", "Java (Swing)"));
        appInfo.add(aboutInfoRow(DashboardIcon.Kind.LAYERS, "Function", "Network monitoring & remote administration"));
        appInfo.add(aboutInfoRow(DashboardIcon.Kind.CALENDAR, "Version", "2.0"));
        rightColumn.add(appInfo);
        lower.add(rightColumn, lowerC);
        lower.setPreferredSize(new Dimension(1200, 490));
        lower.setMaximumSize(new Dimension(Integer.MAX_VALUE, 490));
        page.add(lower);
        return page;
    }

    private JPanel aboutCard(String title, DashboardIcon.Kind icon) {
        JPanel panel = card();
        panel.setLayout(new BoxLayout(panel, BoxLayout.Y_AXIS));
        panel.setBorder(BorderFactory.createCompoundBorder(new RoundedBorder(AdminTheme.BORDER, 16),
                BorderFactory.createEmptyBorder(18, 22, 18, 22)));
        JPanel heading = new JPanel(new FlowLayout(FlowLayout.LEFT, 10, 0));
        heading.setOpaque(false);
        heading.add(new JLabel(new DashboardIcon(icon, AdminTheme.BLUE, 30)));
        heading.add(label(title, AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 21)));
        heading.setAlignmentX(Component.LEFT_ALIGNMENT);
        heading.setMaximumSize(new Dimension(Integer.MAX_VALUE, 38));
        panel.add(heading);
        panel.add(Box.createVerticalStrut(12));
        panel.setAlignmentX(Component.LEFT_ALIGNMENT);
        return panel;
    }

    private JPanel aboutFeature(DashboardIcon.Kind icon, String title, String description) {
        JPanel row = new JPanel(new BorderLayout(16, 0));
        row.setOpaque(false);
        row.setBorder(BorderFactory.createMatteBorder(1, 0, 0, 0, new Color(222, 232, 247)));
        row.setPreferredSize(new Dimension(600, 58));
        row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 58));
        row.setAlignmentX(Component.LEFT_ALIGNMENT);
        JPanel featureName = new JPanel(new FlowLayout(FlowLayout.LEFT, 12, 0));
        featureName.setOpaque(false);
        featureName.setPreferredSize(new Dimension(250, 57));
        featureName.setMaximumSize(new Dimension(250, 58));
        featureName.add(new JLabel(new DashboardIcon(icon, AdminTheme.BLUE, 28)));
        JLabel name = label(title, AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 15));
        featureName.add(name);
        row.add(featureName, BorderLayout.WEST);
        row.add(label(description, AdminTheme.TEXT, new Font("Segoe UI", Font.PLAIN, 14)), BorderLayout.CENTER);
        return row;
    }

    private JPanel aboutInfoRow(DashboardIcon.Kind icon, String title, String value) {
        JPanel row = new JPanel(new BorderLayout(14, 0));
        row.setOpaque(false);
        row.setBorder(BorderFactory.createMatteBorder(1, 0, 0, 0, new Color(222, 232, 247)));
        row.setPreferredSize(new Dimension(600, 39));
        row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 39));
        row.setAlignmentX(Component.LEFT_ALIGNMENT);
        JPanel infoName = new JPanel(new FlowLayout(FlowLayout.LEFT, 12, 0));
        infoName.setOpaque(false);
        infoName.setPreferredSize(new Dimension(190, 38));
        infoName.setMaximumSize(new Dimension(190, 39));
        infoName.add(new JLabel(new DashboardIcon(icon, AdminTheme.TEXT, 22)));
        JLabel name = label(title, AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 15));
        infoName.add(name);
        row.add(infoName, BorderLayout.WEST);
        row.add(label(value, AdminTheme.TEXT, new Font("Segoe UI", Font.PLAIN, 14)), BorderLayout.CENTER);
        return row;
    }

    private static final class NetworkDiagram extends JPanel {
        private NetworkDiagram() { setOpaque(false); }

        @Override protected void paintComponent(Graphics graphics) {
            super.paintComponent(graphics);
            Graphics2D g = (Graphics2D) graphics.create();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            int w = getWidth(), h = getHeight();
            g.setColor(new Color(228, 239, 255));
            Path2D wave = new Path2D.Double();
            wave.moveTo(0, h * .70); wave.curveTo(w * .22, h * .42, w * .28, h * .90, w * .52, h * .57);
            wave.curveTo(w * .72, h * .30, w * .78, h * .72, w, h * .36); wave.lineTo(w, h); wave.lineTo(0, h); wave.closePath();
            g.fill(wave);
            g.setColor(new Color(205, 226, 255));
            Path2D wave2 = new Path2D.Double();
            wave2.moveTo(0, h * .88); wave2.curveTo(w * .20, h * .62, w * .30, h * 1.03, w * .55, h * .78);
            wave2.curveTo(w * .78, h * .56, w * .84, h * .94, w, h * .67); wave2.lineTo(w, h); wave2.lineTo(0, h); wave2.closePath();
            g.fill(wave2);
            g.setColor(new Color(0, 111, 245));
            g.setStroke(new BasicStroke(4, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
            int center = w / 2, top = 8;
            drawMiniMonitor(g, center - 48, top, 96, 64, 0);
            g.drawLine(center, top + 64, center, 88);
            g.drawLine(center, 88, center - 105, 88); g.drawLine(center, 88, center + 105, 88);
            g.drawLine(center - 105, 88, center - 105, 106); g.drawLine(center, 88, center, 106); g.drawLine(center + 105, 88, center + 105, 106);
            drawMiniMonitor(g, center - 135, 106, 48, 38, 0);
            drawMiniMonitor(g, center - 24, 106, 48, 38, 0);
            drawMiniMonitor(g, center + 87, 106, 48, 38, 0);
            g.dispose();
        }

        private void drawMiniMonitor(Graphics2D g, int x, int y, int w, int h, int ignored) {
            g.drawRoundRect(x, y, w, h, 5, 5);
            g.drawLine(x + w / 2, y + h, x + w / 2, y + h + 7);
            g.drawLine(x + w / 2 - 9, y + h + 7, x + w / 2 + 9, y + h + 7);
        }
    }

    private JPanel settingsSection(String title, DashboardIcon.Kind icon) {
        JPanel section = card();
        section.setLayout(new BoxLayout(section, BoxLayout.Y_AXIS));
        JPanel heading = new JPanel(new FlowLayout(FlowLayout.LEFT, 9, 0));
        heading.setOpaque(false);
        heading.add(new JLabel(new DashboardIcon(icon, AdminTheme.BLUE, 22)));
        heading.add(label(title, AdminTheme.TEXT, AdminTheme.MEDIUM));
        heading.setAlignmentX(Component.LEFT_ALIGNMENT);
        section.add(heading);
        section.add(Box.createVerticalStrut(10));
        return section;
    }

    private JPanel settingsHeading(String title, String subtitle, DashboardIcon.Kind icon, Color accent, Color tint) {
        JPanel heading = new JPanel(new FlowLayout(FlowLayout.LEFT, 14, 0));
        heading.setOpaque(false);
        JPanel iconTile = new RoundedPanel(14, tint, null);
        iconTile.setLayout(new GridBagLayout());
        iconTile.setPreferredSize(new Dimension(80, 80));
        iconTile.add(new JLabel(new DashboardIcon(icon, accent, 40)));
        heading.add(iconTile);
        JPanel text = new JPanel();
        text.setOpaque(false);
        text.setLayout(new BoxLayout(text, BoxLayout.Y_AXIS));
        text.add(label(title, AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 25)));
        text.add(Box.createVerticalStrut(3));
        text.add(label(subtitle, AdminTheme.MUTED, AdminTheme.REGULAR));
        heading.add(text);
        heading.setPreferredSize(new Dimension(1000, 80));
        heading.setMaximumSize(new Dimension(Integer.MAX_VALUE, 80));
        return heading;
    }

    private JPanel settingsTile(String title, String value, DashboardIcon.Kind icon,
                                Color accent, Color tint, Color border) {
        JPanel tile = new RoundedPanel(16, tint, border);
        tile.setLayout(new BorderLayout(34, 0));
        tile.setBorder(BorderFactory.createCompoundBorder(new RoundedBorder(border, 16),
                BorderFactory.createEmptyBorder(14, 32, 14, 24)));
        tile.add(new JLabel(new DashboardIcon(icon, accent, 36)), BorderLayout.WEST);
        JPanel text = new JPanel();
        text.setOpaque(false);
        text.setLayout(new BoxLayout(text, BoxLayout.Y_AXIS));
        text.add(label(title, AdminTheme.MUTED, new Font("Segoe UI", Font.PLAIN, 20)));
        text.add(Box.createVerticalStrut(5));
        JLabel valueLabel = label(value, AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 20));
        valueLabel.setToolTipText(value);
        text.add(valueLabel);
        JPanel textCenter = new JPanel(new GridBagLayout());
        textCenter.setOpaque(false);
        textCenter.add(text);
        tile.add(textCenter, BorderLayout.CENTER);
        tile.setPreferredSize(new Dimension(360, 145));
        tile.setMaximumSize(new Dimension(Integer.MAX_VALUE, 145));
        return tile;
    }

    private JComponent settingsRow(String name, String value) {
        JPanel row = new JPanel(new BorderLayout(18, 0));
        row.setOpaque(false);
        row.setBorder(BorderFactory.createEmptyBorder(8, 2, 8, 2));
        row.add(label(name, AdminTheme.MUTED, AdminTheme.REGULAR), BorderLayout.WEST);
        JLabel valueLabel = label(value, AdminTheme.TEXT, AdminTheme.MEDIUM);
        valueLabel.setHorizontalAlignment(SwingConstants.RIGHT);
        row.add(valueLabel, BorderLayout.CENTER);
        row.setAlignmentX(Component.LEFT_ALIGNMENT);
        row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 42));
        return row;
    }

    private JComponent buildRemoteAdministrationContent() {
        ScrollablePanel page = new ScrollablePanel();
        page.setLayout(new BoxLayout(page, BoxLayout.Y_AXIS));
        page.setBackground(AdminTheme.BACKGROUND);
        page.setBorder(BorderFactory.createEmptyBorder(0, 20, 24, 20));

        page.add(Box.createVerticalStrut(56));
        JPanel clientSummary = buildRemoteClientSummary();
        clientSummary.setAlignmentX(Component.LEFT_ALIGNMENT);
        clientSummary.setMaximumSize(new Dimension(Integer.MAX_VALUE, 132));
        page.add(clientSummary);
        page.add(Box.createVerticalStrut(56));

        remoteAdminActions = new JPanel(new GridLayout(1, 3, 16, 0));
        remoteAdminActions.setOpaque(false);
        remoteAdminActions.add(remoteActionCard("Lock Screen", "Lock the Windows session on the client.",
                RemoteCommandIcon.Kind.LOCK, AdminTheme.BLUE, AdminTheme.BLUE_BG, AdminTheme.BLUE_BORDER,
                () -> runRemoteCommand(4, 0)));
        remoteAdminActions.add(remoteActionCard("Shutdown", "Shut down the client computer remotely.",
                RemoteCommandIcon.Kind.SHUTDOWN, new Color(232, 55, 48), new Color(255, 246, 243), new Color(255, 207, 196),
                () -> runRemoteCommand(2, 30)));
        remoteAdminActions.add(remoteActionCard("Restart", "Restart the client computer remotely.",
                RemoteCommandIcon.Kind.RESTART, AdminTheme.PURPLE, AdminTheme.PURPLE_BG, AdminTheme.PURPLE_BORDER,
                () -> runRemoteCommand(3, 30)));
        remoteAdminActions.setPreferredSize(new Dimension(1200, 450));
        remoteAdminActions.setMinimumSize(new Dimension(600, 390));
        remoteAdminActions.setMaximumSize(new Dimension(Integer.MAX_VALUE, 470));
        remoteAdminActions.setAlignmentX(Component.LEFT_ALIGNMENT);
        page.add(remoteAdminActions);
        page.add(Box.createVerticalGlue());

        refreshRemoteAdministrationPage();
        return page;
    }

    private JPanel buildRemoteClientSummary() {
        JPanel summary = new RoundedPanel(16, new Color(242, 247, 255), new Color(204, 221, 250));
        summary.setLayout(new GridBagLayout());
        summary.setBorder(BorderFactory.createCompoundBorder(new RoundedBorder(new Color(204, 221, 250), 16),
                BorderFactory.createEmptyBorder(12, 18, 12, 18)));

        remoteClientNameValue = label("No client selected", AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 20));
        remoteSessionValue = label("Session: --", AdminTheme.MUTED, AdminTheme.SMALL);
        remoteClientStatusValue = label("Offline", AdminTheme.PINK, AdminTheme.MEDIUM);
        remoteOsValue = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
        remoteCpuValue = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);
        remoteRamValue = label("--", AdminTheme.TEXT, AdminTheme.MEDIUM);

        JPanel client = new JPanel(new BorderLayout(16, 0));
        client.setOpaque(false);
        client.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.LAPTOP, AdminTheme.BLUE, 48)), BorderLayout.WEST);
        JPanel clientText = new JPanel();
        clientText.setOpaque(false);
        clientText.setLayout(new BoxLayout(clientText, BoxLayout.Y_AXIS));
        clientText.add(remoteClientNameValue);
        clientText.add(Box.createVerticalStrut(3));
        clientText.add(remoteSessionValue);
        JPanel clientNameAndStatus = new JPanel(new FlowLayout(FlowLayout.LEFT, 14, 0));
        clientNameAndStatus.setOpaque(false);
        clientNameAndStatus.add(clientText);
        JPanel statusPill = new RoundedPanel(20, AdminTheme.GREEN_BG, null);
        statusPill.setLayout(new GridBagLayout());
        statusPill.setBorder(BorderFactory.createEmptyBorder(5, 12, 5, 12));
        statusPill.add(remoteClientStatusValue);
        clientNameAndStatus.add(statusPill);
        client.add(clientNameAndStatus, BorderLayout.CENTER);

        addRemoteSummaryCell(summary, client, 0, .42, false);
        addRemoteSummaryCell(summary, remoteSummaryDetail(DashboardIcon.Kind.WINDOWS, "Operating System", remoteOsValue,
                new Color(0, 160, 230)), 1, .20, true);
        addRemoteSummaryCell(summary, remoteSummaryDetail(DashboardIcon.Kind.CPU, "CPU Usage", remoteCpuValue,
                AdminTheme.BLUE), 2, .20, true);
        addRemoteSummaryCell(summary, remoteSummaryDetail(DashboardIcon.Kind.RAM, "RAM Usage", remoteRamValue,
                AdminTheme.PURPLE), 3, .18, true);
        return summary;
    }

    private JPanel remoteSummaryDetail(DashboardIcon.Kind icon, String title, JLabel value, Color accent) {
        JPanel detail = new JPanel(new BorderLayout(12, 0));
        detail.setOpaque(false);
        detail.add(new JLabel(new DashboardIcon(icon, accent, 36)), BorderLayout.WEST);
        JPanel lines = new JPanel();
        lines.setOpaque(false);
        lines.setLayout(new BoxLayout(lines, BoxLayout.Y_AXIS));
        lines.add(value);
        lines.add(Box.createVerticalStrut(3));
        lines.add(label(title, AdminTheme.MUTED, AdminTheme.SMALL));
        detail.add(lines, BorderLayout.CENTER);
        return detail;
    }

    private void addRemoteSummaryCell(JPanel parent, JComponent component, int column, double weight, boolean divider) {
        if (divider) component.setBorder(BorderFactory.createMatteBorder(0, 1, 0, 0, new Color(207, 221, 245)));
        GridBagConstraints constraints = new GridBagConstraints();
        constraints.gridx = column;
        constraints.gridy = 0;
        constraints.weightx = weight;
        constraints.fill = GridBagConstraints.BOTH;
        constraints.anchor = GridBagConstraints.CENTER;
        constraints.insets = divider ? new Insets(0, 18, 0, 8) : new Insets(0, 4, 0, 12);
        parent.add(component, constraints);
    }

    private JComponent buildMonitoringContent() {
        JPanel content = new JPanel(new BorderLayout());
        content.setBackground(AdminTheme.BACKGROUND);
        content.setBorder(BorderFactory.createEmptyBorder(12, 20, 16, 20));
        JPanel stack = new JPanel();
        stack.setOpaque(false);
        stack.setLayout(new BoxLayout(stack, BoxLayout.Y_AXIS));

        JPanel summary = new JPanel(new GridLayout(1, 4, 12, 0));
        summary.setOpaque(false);
        summary.add(monitorMetric("CPU Usage", monitorCpuValue, "Live processor load", DashboardIcon.Kind.CPU,
                AdminTheme.BLUE, AdminTheme.BLUE_BG, AdminTheme.BLUE_BORDER));
        summary.add(monitorMetric("RAM Usage", monitorRamValue, "Live memory load", DashboardIcon.Kind.RAM,
                AdminTheme.PURPLE, AdminTheme.PURPLE_BG, AdminTheme.PURPLE_BORDER));
        summary.add(monitorMetric("Network Interface", monitorNetworkValue, "Selected client adapter", DashboardIcon.Kind.WIFI,
                new Color(217, 119, 6), new Color(255, 247, 237), new Color(254, 215, 170)));
        summary.add(monitorMetric("Client Connection", monitorClientValue, "Current session status", DashboardIcon.Kind.PEOPLE,
                AdminTheme.GREEN, AdminTheme.GREEN_BG, AdminTheme.GREEN_BORDER));
        summary.setPreferredSize(new Dimension(1200, 118));
        summary.setMaximumSize(new Dimension(Integer.MAX_VALUE, 118));
        stack.add(summary);
        stack.add(Box.createVerticalStrut(12));

        JPanel charts = new JPanel(new GridBagLayout());
        charts.setOpaque(false);
        JPanel usage = card();
        usage.setLayout(new BorderLayout(0, 8));
        usage.add(cardHeader("CPU & RAM Usage (Realtime)", DashboardIcon.Kind.MONITORING), BorderLayout.NORTH);
        usage.add(detailTelemetryChart, BorderLayout.CENTER);
        JPanel legend = new JPanel(new FlowLayout(FlowLayout.CENTER, 20, 0));
        legend.setOpaque(false);
        legend.add(legendDot(AdminTheme.BLUE, "CPU Usage"));
        legend.add(legendDot(AdminTheme.PURPLE, "RAM Usage"));
        usage.add(legend, BorderLayout.SOUTH);

        JPanel network = card();
        network.setLayout(new BorderLayout(0, 10));
        network.add(cardHeader("Client Network Information", DashboardIcon.Kind.WIFI), BorderLayout.NORTH);
        JPanel networkRows = new JPanel(new GridLayout(5, 2, 8, 8));
        networkRows.setOpaque(false);
        addInfoRow(networkRows, "Interface", monitorInterface);
        addInfoRow(networkRows, "IPv4", monitorIpAddress);
        addInfoRow(networkRows, "MAC Address", monitorMacAddress);
        addInfoRow(networkRows, "Gateway", monitorGateway);
        addInfoRow(networkRows, "Subnet", monitorSubnet);
        network.add(networkRows, BorderLayout.CENTER);
        GridBagConstraints c = new GridBagConstraints();
        c.gridy = 0; c.fill = GridBagConstraints.BOTH; c.weighty = 1;
        c.gridx = 0; c.weightx = .56; c.insets = new Insets(0, 0, 0, 12); charts.add(usage, c);
        c.gridx = 1; c.weightx = .44; c.insets = new Insets(0, 0, 0, 0); charts.add(network, c);
        charts.setPreferredSize(new Dimension(1200, 310));
        charts.setMaximumSize(new Dimension(Integer.MAX_VALUE, 310));
        stack.add(charts);
        stack.add(Box.createVerticalStrut(12));

        JPanel lower = new JPanel(new GridLayout(1, 2, 12, 0));
        lower.setOpaque(false);
        lower.add(buildSystemInformationCard());
        lower.add(buildSystemStatusCard());
        lower.setPreferredSize(new Dimension(1200, 280));
        lower.setMaximumSize(new Dimension(Integer.MAX_VALUE, 280));
        stack.add(lower);
        content.add(stack, BorderLayout.NORTH);
        return content;
    }

    private JComponent monitorMetric(String title, JLabel value, String caption, DashboardIcon.Kind icon,
                                     Color accent, Color background, Color border) {
        JPanel panel = new RoundedPanel(10, background, border);
        panel.setLayout(new BorderLayout(12, 0));
        panel.setBorder(BorderFactory.createEmptyBorder(14, 16, 14, 16));
        panel.add(new JLabel(new DashboardIcon(icon, accent, 36)), BorderLayout.WEST);
        JPanel text = new JPanel();
        text.setOpaque(false);
        text.setLayout(new BoxLayout(text, BoxLayout.Y_AXIS));
        text.add(label(title, AdminTheme.TEXT, AdminTheme.MEDIUM));
        text.add(Box.createVerticalStrut(2));
        text.add(value);
        text.add(label(caption, AdminTheme.MUTED, AdminTheme.SMALL));
        panel.add(text, BorderLayout.CENTER);
        return panel;
    }

    private JPanel buildSystemInformationCard() {
        JPanel panel = card();
        panel.setLayout(new BorderLayout(0, 8));
        panel.add(cardHeader("System Information", DashboardIcon.Kind.ABOUT), BorderLayout.NORTH);
        JPanel rows = new JPanel(new GridLayout(5, 2, 8, 7));
        rows.setOpaque(false);
        addInfoRow(rows, "Operating System", monitorOsName);
        addInfoRow(rows, "OS Version", monitorOsVersion);
        addInfoRow(rows, "Architecture", monitorArchitecture);
        addInfoRow(rows, "Network Interface", monitorSystemInterface);
        addInfoRow(rows, "IPv4 Address", monitorSystemIpAddress);
        panel.add(rows, BorderLayout.CENTER);
        return panel;
    }

    private JPanel buildSystemStatusCard() {
        JPanel panel = card();
        panel.setLayout(new BorderLayout(0, 8));
        panel.add(cardHeader("System Status", DashboardIcon.Kind.PING), BorderLayout.NORTH);
        JPanel rows = new JPanel(new GridLayout(4, 1, 0, 0));
        rows.setOpaque(false);
        rows.add(statusRow("Client", monitorConnectionStatus, monitorLastSeen));
        rows.add(statusRow("CPU Telemetry", monitorCpuStatus, monitorCpuStatusValue));
        rows.add(statusRow("RAM Telemetry", monitorRamStatus, monitorRamStatusValue));
        rows.add(statusRow("Network Details", monitorNetworkStatusValue, monitorGateway));
        panel.add(rows, BorderLayout.CENTER);
        return panel;
    }

    private JPanel statusRow(String title, JLabel status, JLabel value) {
        JPanel row = new JPanel(new BorderLayout(10, 0));
        row.setOpaque(false);
        row.setBorder(BorderFactory.createMatteBorder(0, 0, 1, 0, AdminTheme.BORDER));
        JLabel dot = label("●", AdminTheme.GREEN, AdminTheme.MEDIUM);
        JPanel left = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 7));
        left.setOpaque(false);
        left.add(dot);
        left.add(label(title, AdminTheme.TEXT, AdminTheme.MEDIUM));
        row.add(left, BorderLayout.WEST);
        row.add(status, BorderLayout.CENTER);
        JPanel right = new JPanel(new FlowLayout(FlowLayout.RIGHT, 0, 7));
        right.setOpaque(false);
        right.add(value);
        row.add(right, BorderLayout.EAST);
        return row;
    }

    private JComponent buildDashboardBottom() {
        JPanel quick = card();
        quick.setLayout(new BorderLayout(0, 12));
        quick.add(cardHeader("Quick Access", DashboardIcon.Kind.PLAY), BorderLayout.NORTH);
        JPanel shortcuts = new JPanel(new GridLayout(1, 3, 10, 0));
        shortcuts.setOpaque(false);
        shortcuts.add(shortcut("IP Scanner", "Quét mạng và phát hiện thiết bị LAN", DashboardIcon.Kind.SCAN));
        shortcuts.add(shortcut("Ping Tool", "Kiểm tra kết nối và độ trễ", DashboardIcon.Kind.PING));
        shortcuts.add(shortcut("Monitoring", "Theo dõi CPU và RAM theo thời gian", DashboardIcon.Kind.MONITORING));
        quick.add(shortcuts, BorderLayout.CENTER);
        quick.setPreferredSize(new Dimension(1200, 210));
        quick.setMaximumSize(new Dimension(Integer.MAX_VALUE, 240));
        return quick;
    }

    private JButton shortcut(String title, String description, DashboardIcon.Kind icon) {
        Color accent = title.equals("IP Scanner") ? AdminTheme.BLUE
                : title.equals("Ping Tool") ? AdminTheme.GREEN : AdminTheme.PURPLE;
        Color background = title.equals("IP Scanner") ? AdminTheme.BLUE_BG
                : title.equals("Ping Tool") ? AdminTheme.GREEN_BG : AdminTheme.PURPLE_BG;
        Color border = title.equals("IP Scanner") ? AdminTheme.BLUE_BORDER
                : title.equals("Ping Tool") ? AdminTheme.GREEN_BORDER : AdminTheme.PURPLE_BORDER;
        JButton button = new JButton("<html><b>" + title + "</b><br><br>" + description + " &nbsp; →</html>",
                new DashboardIcon(icon, accent, 24));
        button.setHorizontalAlignment(SwingConstants.LEFT);
        button.setIconTextGap(10);
        button.setForeground(AdminTheme.TEXT);
        button.setBackground(background);
        button.setOpaque(true);
        button.setContentAreaFilled(true);
        button.setFont(AdminTheme.REGULAR);
        button.setFocusPainted(false);
        button.setBorder(new RoundedBorder(border, 10));
        button.addActionListener(e -> showFeature(title));
        return button;
    }

    private JComponent buildSidebar() {
        JPanel side = new JPanel(new BorderLayout());
        side.setBackground(AdminTheme.SIDEBAR);
        side.setPreferredSize(new Dimension(250, 0));

        // Header: Globe on left + NetworkAdmin text and v2.0 badge on right
        JPanel top = new JPanel();
        top.setOpaque(false);
        top.setLayout(new BoxLayout(top, BoxLayout.Y_AXIS));

        JPanel brandRow = new JPanel(new FlowLayout(FlowLayout.LEFT, 10, 0));
        brandRow.setOpaque(false);
        brandRow.setBorder(BorderFactory.createEmptyBorder(20, 16, 16, 16));

        JLabel logo = new JLabel(new DashboardIcon(DashboardIcon.Kind.GLOBE, AdminTheme.BLUE, 42));
        brandRow.add(logo);

        JPanel brandText = new JPanel();
        brandText.setOpaque(false);
        brandText.setLayout(new BoxLayout(brandText, BoxLayout.Y_AXIS));
        JLabel brand = label("NetworkAdmin", Color.WHITE, new Font("Segoe UI", Font.BOLD, 22));
        brandText.add(brand);
        brandText.add(Box.createVerticalStrut(3));

        JPanel badgeWrap = new JPanel(new FlowLayout(FlowLayout.RIGHT, 0, 0));
        badgeWrap.setOpaque(false);
        JPanel badge = new RoundedPanel(8, AdminTheme.BLUE, null);
        badge.setBorder(BorderFactory.createEmptyBorder(2, 6, 2, 6));
        badge.add(label("v2.0", Color.WHITE, new Font("Segoe UI", Font.BOLD, 10)));
        badgeWrap.add(badge);
        brandText.add(badgeWrap);

        brandRow.add(brandText);
        top.add(brandRow);
        top.add(Box.createVerticalStrut(14));

        // Feature navigation
        JPanel navArea = new JPanel();
        navArea.setOpaque(false);
        navArea.setLayout(new BoxLayout(navArea, BoxLayout.Y_AXIS));
        List<NavItem> navigation = List.of(
                new NavItem("Dashboard", DashboardIcon.Kind.DASHBOARD),
                new NavItem("IP Scanner", DashboardIcon.Kind.SCAN),
                new NavItem("Ping Tool", DashboardIcon.Kind.PING),
                new NavItem("Monitoring", DashboardIcon.Kind.MONITORING),
                new NavItem("View Screen", DashboardIcon.Kind.SCREEN),
                new NavItem("Remote Administration", DashboardIcon.Kind.POWER),
                new NavItem("Chat", DashboardIcon.Kind.CHAT),
                new NavItem("Settings", DashboardIcon.Kind.SETTINGS),
                new NavItem("About", DashboardIcon.Kind.ABOUT));

        for (NavItem item : navigation) {
            boolean active = item.label().equals("Dashboard");
            JButton nav = createNavButton(item.label(), item.icon(), active);
            sidebarNavButtons.put(item.label(), nav);

            switch (item.label()) {
                case "Dashboard", "IP Scanner", "Ping Tool", "Monitoring" -> nav.addActionListener(e -> showFeature(item.label()));
            }
            if (item.label().equals("View Screen")) {
                sidebarViewScreenButton = nav;
                nav.addActionListener(e -> viewScreen());
            }
            if (item.label().equals("Remote Administration")) {
                sidebarRemoteAdministrationButton = nav;
                nav.addActionListener(e -> showFeature("Remote Administration"));
            }
            if (item.label().equals("Chat")) {
                sidebarChatButton = nav;
                nav.addActionListener(e -> openChat());
            }
            if (item.label().equals("Settings") || item.label().equals("About")) {
                nav.addActionListener(e -> showFeature(item.label()));
            }

            JPanel navWrapper = new JPanel(new BorderLayout());
            navWrapper.setOpaque(false);
            navWrapper.setBorder(BorderFactory.createEmptyBorder(3, 10, 3, 10));
            navWrapper.add(nav, BorderLayout.CENTER);
            navWrapper.setPreferredSize(new Dimension(0, 52));
            navWrapper.setMinimumSize(new Dimension(0, 52));
            navWrapper.setMaximumSize(new Dimension(Integer.MAX_VALUE, 52));
            navArea.add(navWrapper);

            if (item.label().equals("Monitoring") || item.label().equals("Chat")) {
                JSeparator divider = new JSeparator();
                divider.setForeground(new Color(30, 41, 59));
                divider.setBackground(new Color(30, 41, 59));
                divider.setMaximumSize(new Dimension(Integer.MAX_VALUE, 1));
                navArea.add(Box.createVerticalStrut(5));
                navArea.add(divider);
                navArea.add(Box.createVerticalStrut(5));
            } else if (!item.label().equals("About")) {
                navArea.add(Box.createVerticalStrut(2));
            }
        }
        side.add(top, BorderLayout.NORTH);
        side.add(navArea, BorderLayout.CENTER);

        // Bottom card in sidebar
        JPanel bottomWrap = new JPanel(new BorderLayout());
        bottomWrap.setOpaque(false);
        bottomWrap.setBorder(BorderFactory.createEmptyBorder(0, 12, 14, 12));

        JPanel statusCard = new RoundedPanel(10, AdminTheme.SIDEBAR_CARD, AdminTheme.SIDEBAR_CARD_BORDER);
        statusCard.setLayout(new BoxLayout(statusCard, BoxLayout.Y_AXIS));
        statusCard.setBorder(BorderFactory.createEmptyBorder(12, 14, 12, 14));

        statusCard.add(serverState);
        statusCard.add(Box.createVerticalStrut(5));
        statusCard.add(serverPort);
        statusCard.add(Box.createVerticalStrut(3));
        statusCard.add(serverInterfaces);

        bottomWrap.add(statusCard, BorderLayout.CENTER);
        side.add(bottomWrap, BorderLayout.SOUTH);

        return side;
    }

    private JButton createNavButton(String text, DashboardIcon.Kind iconKind, boolean active) {
        JButton button = new JButton(text, new DashboardIcon(iconKind, active ? Color.WHITE : AdminTheme.SIDEBAR_TEXT, 22)) {
            @Override
            protected void paintComponent(Graphics g) {
                if (Boolean.TRUE.equals(getClientProperty("nav.active"))) {
                    Graphics2D g2 = (Graphics2D) g.create();
                    g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                    g2.setColor(AdminTheme.SIDEBAR_ACTIVE);
                    g2.fillRoundRect(0, 0, getWidth(), getHeight(), 8, 8);
                    g2.dispose();
                }
                super.paintComponent(g);

                if (text.equals("Chat") && unreadChatCount > 0) {
                    Graphics2D g2 = (Graphics2D) g.create();
                    g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                    String countStr = unreadChatCount > 99 ? "99+" : String.valueOf(unreadChatCount);
                    g2.setFont(new Font("Segoe UI", Font.BOLD, 12));
                    FontMetrics fm = g2.getFontMetrics();
                    int bw = Math.max(18, fm.stringWidth(countStr) + 8);
                    int bh = 18;
                    int bx = getWidth() - bw - 10;
                    int by = (getHeight() - bh) / 2;
                    g2.setColor(AdminTheme.PINK);
                    g2.fillRoundRect(bx, by, bw, bh, 9, 9);
                    g2.setColor(Color.WHITE);
                    g2.drawString(countStr, bx + (bw - fm.stringWidth(countStr)) / 2, by + fm.getAscent() - 1);
                    g2.dispose();
                }
            }
        };
        button.setHorizontalAlignment(SwingConstants.LEFT);
        button.setIconTextGap(12);
        button.putClientProperty("nav.active", active);
        button.putClientProperty("nav.iconKind", iconKind);
        button.setForeground(active ? Color.WHITE : AdminTheme.SIDEBAR_TEXT);
        button.setBackground(AdminTheme.SIDEBAR);
        button.setOpaque(false);
        button.setContentAreaFilled(false);
        button.setBorder(BorderFactory.createEmptyBorder(9, 12, 9, 12));
        button.setFont(new Font("Segoe UI", active ? Font.BOLD : Font.PLAIN, 15));
        button.setFocusPainted(false);
        button.setMaximumSize(new Dimension(Integer.MAX_VALUE, 42));
        button.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        return button;
    }

    private JComponent buildHeader() {
        JPanel header = new JPanel(new BorderLayout());
        header.setBackground(AdminTheme.BACKGROUND);
        header.setBorder(BorderFactory.createEmptyBorder(20, 28, 16, 28));

        JPanel titleBlock = new JPanel();
        titleBlock.setOpaque(false);
        titleBlock.setLayout(new BoxLayout(titleBlock, BoxLayout.Y_AXIS));
        titleBlock.add(pageTitle);
        titleBlock.add(Box.createVerticalStrut(3));
        titleBlock.add(pageSubtitle);

        pageHeaderIconTile.setLayout(new GridBagLayout());
        pageHeaderIconTile.setPreferredSize(new Dimension(72, 72));
        pageHeaderIconTile.add(pageHeaderIcon);
        pageHeaderIconTile.setVisible(false);
        JPanel pageHeading = new JPanel(new FlowLayout(FlowLayout.LEFT, 16, 0));
        pageHeading.setOpaque(false);
        pageHeading.add(pageHeaderIconTile);
        pageHeading.add(titleBlock);
        header.add(pageHeading, BorderLayout.WEST);

        JPanel right = new JPanel(new FlowLayout(FlowLayout.RIGHT, 14, 0));
        right.setOpaque(false);

        statePill.setLayout(new FlowLayout(FlowLayout.CENTER, 10, 5));
        statePill.add(headerServerState);
        right.add(statePill);

        right.add(verticalDivider());

        JPanel agentBox = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 0));
        agentBox.setOpaque(false);
        agentBox.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.PEOPLE, AdminTheme.TEXT, 22)));
        JPanel agentText = new JPanel();
        agentText.setOpaque(false);
        agentText.setLayout(new BoxLayout(agentText, BoxLayout.Y_AXIS));
        agentText.add(label("Connected Agents", AdminTheme.MUTED, AdminTheme.SMALL));
        agentText.add(headerOnlineCount);
        agentBox.add(agentText);
        right.add(agentBox);

        right.add(verticalDivider());

        JPanel timeBox = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 0));
        timeBox.setOpaque(false);
        timeBox.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.CLOCK, AdminTheme.TEXT, 22)));
        JPanel timeText = new JPanel();
        timeText.setOpaque(false);
        timeText.setLayout(new BoxLayout(timeText, BoxLayout.Y_AXIS));
        timeText.add(clock);
        timeText.add(date);
        timeBox.add(timeText);
        right.add(timeBox);

        right.add(verticalDivider());
        right.add(headerChatButton);

        header.add(right, BorderLayout.EAST);
        return header;
    }

    private JComponent buildSelectionBar() {
        JPanel row = new RoundedPanel(10, Color.WHITE, AdminTheme.BORDER);
        row.setLayout(new BorderLayout(14, 0));
        row.setBorder(BorderFactory.createEmptyBorder(7, 14, 7, 14));

        JPanel labelPanel = new JPanel(new GridBagLayout());
        labelPanel.setOpaque(false);
        GridBagConstraints labelConstraints = new GridBagConstraints();
        labelConstraints.gridy = 0;
        labelConstraints.anchor = GridBagConstraints.CENTER;
        labelConstraints.insets = new Insets(0, 0, 0, 8);
        labelPanel.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.SCREEN, AdminTheme.BLUE, 22)), labelConstraints);
        labelConstraints.gridx = 1;
        labelConstraints.insets = new Insets(0, 0, 0, 0);
        labelPanel.add(label("Select Client", AdminTheme.TEXT, AdminTheme.MEDIUM), labelConstraints);
        row.add(labelPanel, BorderLayout.WEST);

        row.add(clientPicker, BorderLayout.CENTER);

        row.setPreferredSize(new Dimension(900, 56));
        return row;
    }

    private JComponent buildSummaryCards() {
        JPanel row = new JPanel(new GridLayout(1, 4, 12, 0));
        row.setOpaque(false);

        row.add(summaryCard("Managed Clients", managedCount, DashboardIcon.Kind.SCREEN, AdminTheme.BLUE, AdminTheme.BLUE_BG, AdminTheme.BLUE_BORDER));
        row.add(summaryCard("Online Clients", onlineCount, DashboardIcon.Kind.PEOPLE, AdminTheme.GREEN, AdminTheme.GREEN_BG, AdminTheme.GREEN_BORDER));
        row.add(summaryCard("Hosts in Last Scan", hostsCount, DashboardIcon.Kind.LAYERS, AdminTheme.PURPLE, AdminTheme.PURPLE_BG, AdminTheme.PURPLE_BORDER));
        row.add(summaryCard("Last Ping (avg)", pingAverage, DashboardIcon.Kind.PING, AdminTheme.PINK, AdminTheme.RED_BG, AdminTheme.RED_BORDER));

        row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 120));
        row.setPreferredSize(new Dimension(1200, 120));
        return row;
    }

    private JComponent summaryCard(String title, JLabel value, DashboardIcon.Kind kind, Color iconColor, Color bg, Color border) {
        JPanel p = new RoundedPanel(12, bg, border);
        p.setLayout(new BorderLayout(14, 0));
        p.setBorder(BorderFactory.createEmptyBorder(16, 20, 16, 20));

        JLabel iconLbl = new JLabel(new DashboardIcon(kind, iconColor, 46));
        p.add(iconLbl, BorderLayout.WEST);

        JPanel text = new JPanel();
        text.setOpaque(false);
        text.setLayout(new BoxLayout(text, BoxLayout.Y_AXIS));
        text.add(label(title, AdminTheme.TEXT, AdminTheme.MEDIUM));
        text.add(Box.createVerticalStrut(4));
        text.add(value);

        p.add(text, BorderLayout.CENTER);
        return p;
    }

    private JComponent buildMainRow() {
        JPanel row = new JPanel(new GridBagLayout());
        row.setOpaque(false);

        // 1. Selected Client Card
        JPanel client = card();
        client.setMinimumSize(new Dimension(0, 0));
        client.setPreferredSize(new Dimension(420, 400));
        client.setLayout(new BorderLayout(0, 10));

        JPanel clientHeader = new JPanel(new BorderLayout());
        clientHeader.setOpaque(false);
        clientHeader.add(cardHeader("Selected Client", DashboardIcon.Kind.SCREEN), BorderLayout.WEST);

        client.add(clientHeader, BorderLayout.NORTH);

        JPanel clientBody = new JPanel();
        clientBody.setOpaque(false);
        clientBody.setLayout(new BoxLayout(clientBody, BoxLayout.Y_AXIS));

        JPanel nameRow = new JPanel(new FlowLayout(FlowLayout.LEFT, 10, 0));
        nameRow.setOpaque(false);
        nameRow.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.LAPTOP, AdminTheme.TEXT, 40)));
        nameRow.add(clientName);

        clientBadgePill.setLayout(new FlowLayout(FlowLayout.CENTER, 10, 2));
        clientBadgePill.add(clientBadgeText);
        nameRow.add(clientBadgePill);
        clientBody.add(nameRow);
        clientBody.add(Box.createVerticalStrut(10));

        JPanel details = new JPanel(new GridBagLayout());
        details.setOpaque(false);
        addProfileRowWithIcon(details, 0, "Operating System", clientOs, DashboardIcon.Kind.WINDOWS, new Color(0, 164, 239));
        addProfileRowWithIcon(details, 1, "Client ID", clientId, DashboardIcon.Kind.ID_CARD, AdminTheme.MUTED);
        addProfileRowWithIcon(details, 2, "Session ID", sessionId, DashboardIcon.Kind.ID_CARD, AdminTheme.MUTED);
        addProfileRowWithIcon(details, 3, "Connected", connectedAt, DashboardIcon.Kind.CALENDAR, AdminTheme.MUTED);
        addProfileRowWithIcon(details, 4, "Last seen", lastSeen, DashboardIcon.Kind.CALENDAR, AdminTheme.MUTED);

        clientBody.add(details);
        client.add(clientBody, BorderLayout.CENTER);

        // 2. Network Information Card
        JPanel network = card();
        network.setMinimumSize(new Dimension(0, 0));
        network.setPreferredSize(new Dimension(280, 400));
        network.setLayout(new BorderLayout(0, 10));
        network.add(cardHeader("Network Information", DashboardIcon.Kind.WIFI), BorderLayout.NORTH);

        JPanel networkRows = new JPanel(new GridLayout(5, 2, 8, 10));
        networkRows.setOpaque(false);
        addInfoRow(networkRows, "Interface", interfaceName);
        addInfoRow(networkRows, "IPv4", primaryIpv4);
        addInfoRow(networkRows, "MAC", primaryMac);
        addInfoRow(networkRows, "Gateway", primaryGateway);
        addInfoRow(networkRows, "Subnet", primarySubnet);
        network.add(networkRows, BorderLayout.CENTER);

        // 3. System Monitoring Card
        JPanel monitor = card();
        monitor.setMinimumSize(new Dimension(0, 0));
        monitor.setPreferredSize(new Dimension(480, 400));
        monitor.setLayout(new BorderLayout(0, 8));
        monitor.add(cardHeader("System Monitoring / LIVE Data", DashboardIcon.Kind.MONITORING), BorderLayout.NORTH);

        JPanel rings = new JPanel(new GridLayout(1, 2, 10, 0));
        rings.setOpaque(false);
        rings.add(cpuRing);
        rings.add(ramRing);

        JPanel monitorCenter = new JPanel(new BorderLayout(0, 6));
        monitorCenter.setOpaque(false);
        monitorCenter.add(rings, BorderLayout.NORTH);
        monitorCenter.add(telemetryChart, BorderLayout.CENTER);

        JPanel legend = new JPanel(new FlowLayout(FlowLayout.CENTER, 22, 0));
        legend.setOpaque(false);
        legend.add(legendDot(AdminTheme.BLUE, "CPU Usage"));
        legend.add(legendDot(AdminTheme.PURPLE, "RAM Usage"));
        monitorCenter.add(legend, BorderLayout.SOUTH);

        monitor.add(monitorCenter, BorderLayout.CENTER);

        GridBagConstraints c1 = new GridBagConstraints();
        c1.gridy = 0;
        c1.fill = GridBagConstraints.BOTH;
        c1.weighty = 1;

        c1.gridx = 0; c1.weightx = .38; c1.insets = new Insets(0, 0, 0, 10); row.add(client, c1);
        c1.gridx = 1; c1.weightx = .25; c1.insets = new Insets(0, 0, 0, 10); row.add(network, c1);
        c1.gridx = 2; c1.weightx = .37; c1.insets = new Insets(0, 0, 0, 0); row.add(monitor, c1);

        row.setPreferredSize(new Dimension(1200, 400));
        row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 400));
        return row;
    }

    private static JPanel legendDot(Color color, String text) {
        JPanel p = new JPanel(new FlowLayout(FlowLayout.LEFT, 6, 0));
        p.setOpaque(false);
        p.add(new JComponent() {
            { setPreferredSize(new Dimension(8, 8)); }
            @Override protected void paintComponent(Graphics g) {
                Graphics2D g2 = (Graphics2D) g.create();
                g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                g2.setColor(color);
                g2.fillOval(0, 0, 8, 8);
                g2.dispose();
            }
        });
        p.add(label(text, AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 12)));
        return p;
    }

    private static void addInfoRow(JPanel rows, String title, JLabel value) {
        rows.add(label(title, AdminTheme.MUTED, AdminTheme.REGULAR));
        rows.add(value);
    }

    private static void addProfileRowWithIcon(JPanel rows, int row, String title, JLabel value, DashboardIcon.Kind iconKind, Color iconColor) {
        GridBagConstraints c = new GridBagConstraints();
        c.gridy = row;
        c.anchor = GridBagConstraints.WEST;

        c.gridx = 0;
        c.weightx = 0;
        c.insets = new Insets(5, 4, 5, 14);
        rows.add(label(title, AdminTheme.MUTED, AdminTheme.REGULAR), c);

        c.gridx = 1;
        c.weightx = 1;
        c.fill = GridBagConstraints.HORIZONTAL;
        c.insets = new Insets(5, 4, 5, 2);

        JPanel valPanel = new JPanel(new FlowLayout(FlowLayout.LEFT, 7, 0));
        valPanel.setOpaque(false);
        value.setToolTipText(value.getText());
        valPanel.add(new JLabel(new DashboardIcon(iconKind, iconColor, 15)));
        valPanel.add(value);
        rows.add(valPanel, c);
    }

    private static String compactIdentifier(String identifier) {
        if (identifier == null || identifier.length() <= 24) return identifier;
        return identifier.substring(0, 12) + "…" + identifier.substring(identifier.length() - 8);
    }

    private static JComponent cardHeader(String title, DashboardIcon.Kind icon) {
        JPanel header = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 0));
        header.setOpaque(false);
        header.add(new JLabel(new DashboardIcon(icon, AdminTheme.BLUE, 22)));
        header.add(label(title, AdminTheme.TEXT, AdminTheme.MEDIUM));
        return header;
    }

    private static JComponent pingMetric(String title, JLabel value, DashboardIcon.Kind kind, Color accent) {
        JPanel panel = new RoundedPanel(8, Color.WHITE, AdminTheme.BORDER);
        panel.setLayout(new BorderLayout(8, 0));
        panel.setBorder(BorderFactory.createEmptyBorder(6, 8, 6, 8));

        panel.add(new JLabel(new DashboardIcon(kind, accent, 18)), BorderLayout.WEST);

        JPanel labels = new JPanel();
        labels.setOpaque(false);
        labels.setLayout(new BoxLayout(labels, BoxLayout.Y_AXIS));
        labels.add(label(title, AdminTheme.MUTED, AdminTheme.SMALL));
        labels.add(Box.createVerticalStrut(2));
        labels.add(value);

        panel.add(labels, BorderLayout.CENTER);
        return panel;
    }

    private JComponent buildOperationsRow() {
        scanSection = buildScanPage();
        pingSection = buildPingPage();
        JPanel holder = new JPanel(new GridLayout(1, 2, 12, 0));
        holder.setOpaque(false);
        holder.add(scanSection);
        holder.add(pingSection);
        return holder;
    }

    private JComponent buildScanPage() {
        JPanel page = new JPanel(new BorderLayout(0, 12));
        page.setOpaque(false);

        JPanel configuration = card();
        configuration.setLayout(new BorderLayout(0, 12));
        configuration.add(cardHeader("Scan Configuration", DashboardIcon.Kind.SETTINGS), BorderLayout.NORTH);
        JPanel form = new JPanel(new GridBagLayout());
        form.setOpaque(false);
        scanTimeout.setFont(AdminTheme.REGULAR);
        scanTimeout.setBackground(Color.WHITE);
        scanTimeout.setSelectedItem("800 ms");
        GridBagConstraints f = new GridBagConstraints();
        f.gridy = 0; f.fill = GridBagConstraints.HORIZONTAL; f.weighty = 1; f.insets = new Insets(0, 0, 0, 10);
        f.gridx = 0; f.weightx = 4; form.add(fieldGroup("Subnet / Network", subnetField), f);
        f.gridx = 1; f.weightx = 1; form.add(fieldGroup("Method", readonlyField("ICMP + ARP")), f);
        f.gridx = 2; f.weightx = 1; form.add(fieldGroup("Timeout", scanTimeout), f);
        f.gridx = 3; f.weightx = 1; form.add(fieldGroup("Workers", readonlyField("32")), f);
        JPanel buttons = new JPanel(new FlowLayout(FlowLayout.RIGHT, 8, 18));
        buttons.setOpaque(false); buttons.add(cancelScanButton); buttons.add(scanButton);
        f.gridx = 4; f.weightx = 0; f.insets = new Insets(0, 4, 0, 0); form.add(buttons, f);
        configuration.add(form, BorderLayout.CENTER);
        page.add(configuration, BorderLayout.NORTH);

        JPanel summary = new JPanel(new GridLayout(1, 4, 12, 0));
        summary.setOpaque(false);
        summary.add(summaryTile("Hosts Found", scanFound, DashboardIcon.Kind.SCREEN, AdminTheme.BLUE, AdminTheme.BLUE_BG, AdminTheme.BLUE_BORDER));
        summary.add(summaryTile("Online Hosts", scanOnline, DashboardIcon.Kind.CHECK_CIRCLE, AdminTheme.GREEN, AdminTheme.GREEN_BG, AdminTheme.GREEN_BORDER));
        summary.add(summaryTile("Offline Hosts", scanOffline, DashboardIcon.Kind.X_CIRCLE, AdminTheme.PINK, AdminTheme.RED_BG, AdminTheme.RED_BORDER));
        summary.add(summaryTile("Scan Time", scanDuration, DashboardIcon.Kind.CLOCK, new Color(217, 119, 6), new Color(255, 247, 237), new Color(254, 215, 170)));

        JPanel results = card();
        results.setLayout(new BorderLayout(0, 10));
        JPanel resultsHeader = new JPanel(new BorderLayout());
        resultsHeader.setOpaque(false);
        resultsHeader.add(cardHeader("Scan Results", DashboardIcon.Kind.SCAN), BorderLayout.WEST);
        resultsHeader.add(scanResultStatus, BorderLayout.SOUTH);
        results.add(resultsHeader, BorderLayout.NORTH);
        results.add(table(scanModel, DashboardIcon.Kind.EMPTY_SCAN, "Run a scan to discover network devices."), BorderLayout.CENTER);

        JPanel lower = new JPanel(new BorderLayout(0, 12));
        lower.setOpaque(false);
        lower.add(summary, BorderLayout.NORTH);
        lower.add(results, BorderLayout.CENTER);
        page.add(lower, BorderLayout.CENTER);
        return page;
    }

    private JComponent buildPingPage() {
        JPanel page = new JPanel(new BorderLayout(0, 12));
        page.setOpaque(false);
        page.setMinimumSize(new Dimension(0, 0));
        page.setPreferredSize(new Dimension(1200, 700));

        JPanel configuration = card();
        configuration.setLayout(new BorderLayout(0, 12));
        configuration.add(cardHeader("Ping Configuration", DashboardIcon.Kind.SETTINGS), BorderLayout.NORTH);
        JPanel form = new JPanel(new GridBagLayout());
        form.setOpaque(false);
        GridBagConstraints f = new GridBagConstraints();
        f.gridy = 0; f.fill = GridBagConstraints.HORIZONTAL; f.weighty = 1; f.insets = new Insets(0, 0, 0, 10);
        f.gridx = 0; f.weightx = 4; form.add(fieldGroup("Target (IP or hostname)", targetField), f);
        f.gridx = 1; f.weightx = .8; form.add(fieldGroup("Probe Count", readonlyField("4")), f);
        f.gridx = 2; f.weightx = 1.1; form.add(fieldGroup("Packet Size", readonlyField("Java API default")), f);
        f.gridx = 3; f.weightx = .9; form.add(fieldGroup("Timeout", readonlyField("1000 ms")), f);
        f.gridx = 4; f.weightx = 0; f.insets = new Insets(0, 4, 0, 0); form.add(pingButton, f);
        configuration.add(form, BorderLayout.CENTER);
        page.add(configuration, BorderLayout.NORTH);

        JPanel resultCard = card();
        resultCard.setLayout(new BorderLayout(0, 12));
        JPanel resultHeader = new JPanel(new BorderLayout());
        resultHeader.setOpaque(false);
        resultHeader.add(cardHeader("Ping Result", DashboardIcon.Kind.PING), BorderLayout.WEST);
        resultCard.add(resultHeader, BorderLayout.NORTH);
        JPanel resultBody = new JPanel(new BorderLayout(0, 12));
        resultBody.setOpaque(false);
        resultBody.add(pingOutcomeBanner(), BorderLayout.NORTH);
        JPanel metrics = new JPanel(new GridLayout(1, 3, 10, 0));
        metrics.setOpaque(false);
        metrics.add(pingLargeMetric("Average RTT", pingRtt, DashboardIcon.Kind.CLOCK,
                AdminTheme.BLUE, AdminTheme.BLUE_BG, AdminTheme.BLUE_BORDER));
        metrics.add(pingLargeMetric("Minimum RTT", pingMin, DashboardIcon.Kind.CHECK_CIRCLE,
                AdminTheme.GREEN, AdminTheme.GREEN_BG, AdminTheme.GREEN_BORDER));
        metrics.add(pingLargeMetric("Maximum RTT", pingMax, DashboardIcon.Kind.PING,
                new Color(217, 119, 6), new Color(255, 247, 237), new Color(254, 215, 170)));
        resultBody.add(metrics, BorderLayout.CENTER);
        resultCard.add(resultBody, BorderLayout.CENTER);

        JPanel middle = new JPanel(new GridLayout(1, 2, 12, 0));
        middle.setOpaque(false);
        middle.add(resultCard);
        middle.add(buildPingChartCard());
        JPanel details = buildPingOutputCard();
        JPanel lower = new JPanel(new GridLayout(1, 2, 12, 0));
        lower.setOpaque(false);
        lower.add(details);
        lower.add(buildPingStatisticsCard());
        JPanel stack = new JPanel(new GridLayout(2, 1, 0, 12));
        stack.setOpaque(false);
        stack.add(middle);
        stack.add(lower);
        page.add(stack, BorderLayout.CENTER);
        return page;
    }

    private JPanel buildPingOutputCard() {
        JPanel details = card();
        details.setLayout(new BorderLayout(0, 10));
        JPanel header = new JPanel(new BorderLayout());
        header.setOpaque(false);
        header.add(cardHeader("Details (Ping Output)", DashboardIcon.Kind.ABOUT), BorderLayout.WEST);
        JPanel actions = new JPanel(new FlowLayout(FlowLayout.RIGHT, 8, 0));
        actions.setOpaque(false);
        JButton copy = neutralButton("Copy", DashboardIcon.Kind.SCREEN);
        JButton clear = neutralButton("Clear", DashboardIcon.Kind.CROSS);
        copy.addActionListener(event -> {
            if (!pingOutput.getText().isBlank()) {
                Toolkit.getDefaultToolkit().getSystemClipboard().setContents(
                        new java.awt.datatransfer.StringSelection(pingOutput.getText()), null);
            }
        });
        clear.addActionListener(event -> {
            pingOutput.setText("");
            pingModel.setRowCount(0);
            pingSent.setText("--");
            pingReceived.setText("--");
            pingLost.setText("--");
            pingLoss.setText("--");
            pingSentStat.setText("--");
            pingReceivedStat.setText("--");
            pingLostStat.setText("--");
            pingLossStat.setText("--");
            pingRtt.setText("-- ms");
            pingMin.setText("-- ms");
            pingMax.setText("-- ms");
            pingAverage.setText("--");
            pingResultStatus.setText("Results cleared");
            if (pingChart != null) pingChart.repaint();
        });
        actions.add(copy);
        actions.add(clear);
        header.add(actions, BorderLayout.EAST);
        details.add(header, BorderLayout.NORTH);
        pingOutput.setEditable(false);
        pingOutput.setLineWrap(false);
        pingOutput.setFont(new Font("Consolas", Font.PLAIN, 14));
        pingOutput.setPreferredSize(new Dimension(500, 150));
        pingOutput.setMinimumSize(new Dimension(0, 80));
        pingOutput.setForeground(AdminTheme.TEXT);
        pingOutput.setBackground(new Color(244, 248, 255));
        pingOutput.setBorder(BorderFactory.createEmptyBorder(10, 12, 10, 12));
        JScrollPane outputScroll = new JScrollPane(pingOutput);
        outputScroll.setBorder(new RoundedBorder(AdminTheme.BORDER, 8));
        outputScroll.getViewport().setBackground(pingOutput.getBackground());
        details.add(outputScroll, BorderLayout.CENTER);
        return details;
    }

    private JPanel fieldGroup(String title, JComponent input) {
        JPanel group = new JPanel(new BorderLayout(0, 5));
        group.setOpaque(false);
        group.add(label(title, AdminTheme.TEXT, AdminTheme.MEDIUM), BorderLayout.NORTH);
        group.add(input, BorderLayout.CENTER);
        return group;
    }

    private JTextField readonlyField(String text) {
        JTextField input = field(text);
        input.setText(text);
        input.setEditable(false);
        input.setBackground(new Color(248, 250, 252));
        return input;
    }

    private JPanel buildPingStatisticsCard() {
        JPanel statistics = card();
        statistics.setLayout(new BorderLayout(0, 10));
        statistics.add(cardHeader("Ping Statistics", DashboardIcon.Kind.LAYERS), BorderLayout.NORTH);
        JPanel metrics = new JPanel(new GridLayout(2, 2, 10, 10));
        metrics.setOpaque(false);
        metrics.add(pingMetric("Packets Sent", pingSentStat, DashboardIcon.Kind.SCREEN, AdminTheme.BLUE));
        metrics.add(pingMetric("Packets Received", pingReceivedStat, DashboardIcon.Kind.CHECK_CIRCLE, AdminTheme.GREEN));
        metrics.add(pingMetric("Packets Lost", pingLostStat, DashboardIcon.Kind.X_CIRCLE, AdminTheme.PINK));
        metrics.add(pingMetric("Packet Loss", pingLossStat, DashboardIcon.Kind.LAYERS, AdminTheme.PURPLE));
        statistics.add(metrics, BorderLayout.CENTER);
        return statistics;
    }

    private JPanel pingOutcomeBanner() {
        JPanel banner = pingOutcomePanel;
        banner.setLayout(new BorderLayout(12, 0));
        banner.setBorder(BorderFactory.createEmptyBorder(10, 14, 10, 14));
        JPanel summary = new JPanel();
        summary.setOpaque(false);
        summary.setLayout(new BoxLayout(summary, BoxLayout.Y_AXIS));
        summary.add(pingResultStatus);
        banner.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.CHECK_CIRCLE, AdminTheme.GREEN, 30)), BorderLayout.WEST);
        banner.add(summary, BorderLayout.CENTER);
        JPanel counts = new JPanel(new GridLayout(1, 3, 16, 0));
        counts.setOpaque(false);
        counts.add(compactPingCount("Sent", pingSent));
        counts.add(compactPingCount("Received", pingReceived));
        counts.add(compactPingCount("Lost", pingLost));
        banner.add(counts, BorderLayout.EAST);
        return banner;
    }

    private JComponent compactPingCount(String title, JLabel value) {
        JPanel item = new JPanel();
        item.setOpaque(false);
        item.setLayout(new BoxLayout(item, BoxLayout.Y_AXIS));
        item.add(label(title, AdminTheme.MUTED, AdminTheme.SMALL));
        item.add(value);
        return item;
    }

    private JComponent pingLargeMetric(String title, JLabel value, DashboardIcon.Kind icon,
                                       Color accent, Color background, Color border) {
        JPanel tile = new RoundedPanel(10, background, border);
        tile.setLayout(new BorderLayout(8, 2));
        tile.setBorder(BorderFactory.createEmptyBorder(10, 12, 10, 12));
        JPanel labelLine = new JPanel(new FlowLayout(FlowLayout.LEFT, 6, 0));
        labelLine.setOpaque(false);
        labelLine.add(new JLabel(new DashboardIcon(icon, accent, 18)));
        labelLine.add(label(title, AdminTheme.TEXT, AdminTheme.MEDIUM));
        value.setFont(new Font("Segoe UI", Font.BOLD, 28));
        value.setForeground(accent);
        tile.add(labelLine, BorderLayout.NORTH);
        tile.add(value, BorderLayout.CENTER);
        return tile;
    }

    private JComponent summaryTile(String title, JLabel value, DashboardIcon.Kind icon, Color accent, Color background, Color border) {
        JPanel tile = new RoundedPanel(10, background, border);
        tile.setLayout(new BorderLayout(12, 0));
        tile.setBorder(BorderFactory.createEmptyBorder(14, 16, 14, 16));
        tile.add(new JLabel(new DashboardIcon(icon, accent, 28)), BorderLayout.WEST);
        JPanel text = new JPanel();
        text.setOpaque(false);
        text.setLayout(new BoxLayout(text, BoxLayout.Y_AXIS));
        text.add(value);
        text.add(label(title, AdminTheme.MUTED, AdminTheme.REGULAR));
        tile.add(text, BorderLayout.CENTER);
        return tile;
    }

    private static Double parseRtt(Object value) {
        if (value instanceof Number number) return number.doubleValue();
        if (value == null) return null;
        String text = value.toString().replace("ms", "").trim();
        try { return Double.parseDouble(text); }
        catch (NumberFormatException ignored) { return null; }
    }

    private JComponent buildPingChartCard() {
        JPanel chartCard = card();
        chartCard.setLayout(new BorderLayout(0, 8));
        chartCard.add(cardHeader("RTT Chart", DashboardIcon.Kind.MONITORING), BorderLayout.NORTH);
        pingChart = new JComponent() {
            @Override protected void paintComponent(Graphics graphics) {
                super.paintComponent(graphics);
                Graphics2D g = (Graphics2D) graphics.create();
                g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                int left = 46, right = getWidth() - 16, top = 20, bottom = getHeight() - 34;
                if (right <= left || bottom <= top) { g.dispose(); return; }
                double max = 5;
                java.util.List<Double> samples = new java.util.ArrayList<>();
                for (int row = 0; row < pingModel.getRowCount(); row++) {
                    Object value = pingModel.getValueAt(row, 2);
                    Double measured = parseRtt(value);
                    if (measured != null) {
                        double sample = measured;
                        samples.add(sample);
                        max = Math.max(max, sample);
                    }
                }
                max = Math.ceil(max / 5.0) * 5.0;
                g.setFont(AdminTheme.SMALL);
                g.setColor(AdminTheme.MUTED);
                g.drawString("RTT (ms)", left, 13);
                g.setColor(AdminTheme.MUTED);
                for (int i = 0; i <= 4; i++) {
                    int y = top + (bottom - top) * i / 4;
                    g.setColor(AdminTheme.BORDER);
                    g.drawLine(left, y, right, y);
                    String tick = String.format(java.util.Locale.ROOT, "%.1f", max * (4 - i) / 4.0);
                    g.setColor(AdminTheme.MUTED);
                    g.drawString(tick, 4, y + 4);
                }
                if (samples.isEmpty()) {
                    String empty = "No RTT measurements yet.";
                    FontMetrics fm = g.getFontMetrics();
                    g.drawString(empty, Math.max(left, (getWidth() - fm.stringWidth(empty)) / 2), (top + bottom) / 2);
                } else {
                    Path2D path = new Path2D.Double();
                    for (int i = 0; i < samples.size(); i++) {
                        double x = samples.size() == 1 ? (left + right) / 2.0 : left + (right - left) * i / (double) (samples.size() - 1);
                        double y = bottom - (bottom - top) * samples.get(i) / max;
                        if (i == 0) path.moveTo(x, y); else path.lineTo(x, y);
                        g.setColor(AdminTheme.BLUE);
                        g.fillOval((int) x - 4, (int) y - 4, 8, 8);
                    }
                    Path2D area = new Path2D.Double(path);
                    area.lineTo(right, bottom);
                    area.lineTo(left, bottom);
                    area.closePath();
                    g.setColor(new Color(AdminTheme.BLUE.getRed(), AdminTheme.BLUE.getGreen(), AdminTheme.BLUE.getBlue(), 28));
                    g.fill(area);
                    g.setColor(AdminTheme.BLUE);
                    g.setStroke(new BasicStroke(2.5f));
                    g.draw(path);
                    for (int i = 0; i < samples.size(); i++) {
                        int x = samples.size() == 1 ? (left + right) / 2 : left + (right - left) * i / (samples.size() - 1);
                        g.drawString("#" + (i + 1), x - 8, bottom + 20);
                    }
                }
                g.dispose();
            }
        };
        pingChart.setOpaque(false);
        pingChart.setPreferredSize(new Dimension(440, 220));
        chartCard.add(pingChart, BorderLayout.CENTER);
        return chartCard;
    }

    private JComponent buildFooter() {
        JPanel footer = new JPanel(new BorderLayout());
        footer.setBackground(AdminTheme.BACKGROUND);
        footer.setBorder(BorderFactory.createCompoundBorder(
                BorderFactory.createMatteBorder(1, 0, 0, 0, AdminTheme.BORDER),
                BorderFactory.createEmptyBorder(8, 20, 8, 20)));

        JPanel left = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 0));
        left.setOpaque(false);
        left.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.ABOUT, AdminTheme.MUTED, 16)));
        left.add(operationStatus);

        JPanel right = new JPanel(new FlowLayout(FlowLayout.RIGHT, 6, 0));
        right.setOpaque(false);
        right.add(new JLabel(new DashboardIcon(DashboardIcon.Kind.JAVA, AdminTheme.TEXT, 16)));
        right.add(label("NetworkAdmin Java", AdminTheme.TEXT, AdminTheme.REGULAR));
        right.add(label("|", new Color(203, 213, 225), AdminTheme.REGULAR));
        right.add(label("v2.0", AdminTheme.MUTED, AdminTheme.REGULAR));

        footer.add(left, BorderLayout.WEST);
        footer.add(right, BorderLayout.EAST);
        return footer;
    }

    private void showFeature(String label) {
        switch (label) {
            case "IP Scanner" -> {
                pageTitle.setText("IP Scanner");
                pageSubtitle.setText("Scan the local network to discover active hosts and devices");
            }
            case "Ping Tool" -> {
                pageTitle.setText("Ping Tool");
                pageSubtitle.setText("Test network connectivity and measure response time (RTT)");
            }
            case "Monitoring" -> {
                pageTitle.setText("Monitoring");
                pageSubtitle.setText("Monitor system resources of the selected client in real time");
            }
            case "Remote Administration" -> {
                pageTitle.setText("Remote Administration");
                pageSubtitle.setText("Lock, shut down, or restart the selected client remotely");
            }
            case "Settings" -> {
                pageTitle.setText("Settings");
                pageSubtitle.setText("Server connection and TLS certificate information");
                pageHeaderIcon.setIcon(new DashboardIcon(DashboardIcon.Kind.SETTINGS, AdminTheme.BLUE, 44));
            }
            case "About" -> {
                pageTitle.setText("About NetworkAdmin");
                pageSubtitle.setText("Application details, capabilities, and connection security");
                pageHeaderIcon.setIcon(new DashboardIcon(DashboardIcon.Kind.GLOBE, AdminTheme.BLUE, 44));
            }
            default -> {
                pageTitle.setText("Network Administration");
                pageSubtitle.setText("Manage and monitor client computers in the network");
            }
        }
        pageHeaderIconTile.setVisible(label.equals("Settings") || label.equals("About"));
        sidebarNavButtons.forEach((name, button) -> {
            boolean active = name.equals(label);
            button.putClientProperty("nav.active", active);
            button.setForeground(active ? Color.WHITE : AdminTheme.SIDEBAR_TEXT);
            button.setFont(new Font("Segoe UI", active ? Font.BOLD : Font.PLAIN, 15));
            DashboardIcon.Kind iconKind = (DashboardIcon.Kind) button.getClientProperty("nav.iconKind");
            button.setIcon(new DashboardIcon(iconKind, active ? Color.WHITE : AdminTheme.SIDEBAR_TEXT, 22));
            button.repaint();
        });
        featureLayout.show(featurePages, label);
        if (selectionWrap != null) {
            boolean showPicker = !label.equals("IP Scanner") && !label.equals("Ping Tool")
                    && !label.equals("Settings") && !label.equals("About");
            selectionWrap.setVisible(showPicker);
            selectionWrap.getParent().revalidate();
        }
        JComponent focusTarget = switch (label) {
            case "IP Scanner" -> subnetField;
            case "Ping Tool" -> targetField;
            default -> null;
        };
        if (focusTarget != null) SwingUtilities.invokeLater(focusTarget::requestFocusInWindow);
    }

    private JPanel card() {
        JPanel p = new RoundedPanel(12, AdminTheme.PANEL, AdminTheme.BORDER);
        p.setBorder(BorderFactory.createEmptyBorder(12, 14, 12, 14));
        return p;
    }

    private JComponent table(DefaultTableModel model, DashboardIcon.Kind emptyIcon, String emptyMessage) {
        JTable table = new JTable(model) {
            @Override
            protected void paintComponent(Graphics graphics) {
                super.paintComponent(graphics);
                if (getModel().getRowCount() != 0 || getHeight() < 70) return;
                Graphics2D g = (Graphics2D) graphics.create();
                g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                int centerX = getWidth() / 2;
                int centerY = Math.max(getTableHeader().getHeight() + 35, getHeight() / 2 - 10);
                new DashboardIcon(emptyIcon, new Color(148, 163, 184), 32).paintIcon(this, g, centerX - 16, centerY - 28);
                g.setFont(AdminTheme.REGULAR);
                g.setColor(AdminTheme.MUTED);
                FontMetrics metrics = g.getFontMetrics();
                g.drawString(emptyMessage, centerX - metrics.stringWidth(emptyMessage) / 2, centerY + 18);
                g.dispose();
            }
        };
        table.setFillsViewportHeight(true);
        table.setRowHeight(28);
        table.setBackground(AdminTheme.PANEL);
        table.setForeground(AdminTheme.TEXT);
        table.setGridColor(AdminTheme.BORDER);
        table.setSelectionBackground(new Color(239, 246, 255));
        table.setSelectionForeground(AdminTheme.TEXT);
        table.setFont(AdminTheme.REGULAR);

        DefaultTableCellRenderer headerRenderer = new DefaultTableCellRenderer();
        headerRenderer.setBackground(AdminTheme.PANEL_ALT);
        headerRenderer.setForeground(AdminTheme.TEXT);
        headerRenderer.setFont(AdminTheme.MEDIUM);
        headerRenderer.setBorder(BorderFactory.createMatteBorder(0, 0, 1, 0, AdminTheme.BORDER));
        table.getTableHeader().setDefaultRenderer(headerRenderer);
        table.getTableHeader().setPreferredSize(new Dimension(0, 30));

        JScrollPane pane = new JScrollPane(table);
        pane.setBorder(new RoundedBorder(AdminTheme.BORDER, 8));
        pane.getViewport().setBackground(AdminTheme.PANEL);
        return pane;
    }

    private static DefaultTableModel model(String... columns) {
        return new DefaultTableModel(columns, 0) {
            @Override public boolean isCellEditable(int row, int column) { return false; }
        };
    }

    private static JLabel label(String text, Color color, Font font) {
        JLabel result = new JLabel(text);
        result.setForeground(color);
        result.setFont(font);
        return result;
    }

    private JTextField field(String placeholder) {
        JTextField result = new PlaceholderTextField(placeholder);
        result.setBackground(Color.WHITE);
        result.setForeground(AdminTheme.TEXT);
        result.setCaretColor(AdminTheme.TEXT);
        result.setBorder(BorderFactory.createCompoundBorder(
                new RoundedBorder(AdminTheme.BORDER, 8),
                BorderFactory.createEmptyBorder(6, 10, 6, 10)));
        result.setFont(AdminTheme.REGULAR);
        return result;
    }

    private JButton actionButton(String text, DashboardIcon.Kind iconKind) {
        JButton button = new JButton(text, new DashboardIcon(iconKind, Color.WHITE, 12));
        button.setBackground(AdminTheme.BLUE);
        button.setForeground(Color.WHITE);
        button.setFocusPainted(false);
        button.setOpaque(false);
        button.setIconTextGap(6);
        button.setBorder(BorderFactory.createEmptyBorder(7, 14, 7, 14));
        button.setFont(AdminTheme.MEDIUM);
        button.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));

        button.setUI(new javax.swing.plaf.basic.BasicButtonUI() {
            @Override
            public void paint(Graphics g, JComponent c) {
                Graphics2D g2 = (Graphics2D) g.create();
                g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                g2.setColor(button.isEnabled() ? AdminTheme.BLUE : new Color(148, 163, 184));
                g2.fillRoundRect(0, 0, c.getWidth(), c.getHeight(), 8, 8);
                g2.dispose();
                super.paint(g, c);
            }
        });
        return button;
    }

    private JButton neutralButton(String text, DashboardIcon.Kind iconKind) {
        JButton button = new JButton(text, new DashboardIcon(iconKind, AdminTheme.TEXT, 12));
        button.setBackground(Color.WHITE);
        button.setForeground(AdminTheme.TEXT);
        button.setFocusPainted(false);
        button.setOpaque(false);
        button.setIconTextGap(6);
        button.setBorder(BorderFactory.createEmptyBorder(7, 13, 7, 13));
        button.setFont(AdminTheme.MEDIUM);
        button.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));

        button.setUI(new javax.swing.plaf.basic.BasicButtonUI() {
            @Override
            public void paint(Graphics g, JComponent c) {
                Graphics2D g2 = (Graphics2D) g.create();
                g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                g2.setColor(Color.WHITE);
                g2.fillRoundRect(0, 0, c.getWidth() - 1, c.getHeight() - 1, 8, 8);
                g2.setColor(AdminTheme.BORDER);
                g2.drawRoundRect(0, 0, c.getWidth() - 1, c.getHeight() - 1, 8, 8);
                g2.dispose();
                super.paint(g, c);
            }
        });
        return button;
    }

    private JButton iconButton(String text, DashboardIcon.Kind iconKind, Color fgColor, Color borderColor) {
        JButton button = new JButton(text, new DashboardIcon(iconKind, fgColor, 14));
        button.setBackground(Color.WHITE);
        button.setForeground(fgColor);
        button.setFocusPainted(false);
        button.setOpaque(false);
        button.setIconTextGap(6);
        button.setBorder(BorderFactory.createEmptyBorder(6, 12, 6, 12));
        button.setFont(AdminTheme.MEDIUM);
        button.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));

        button.setUI(new javax.swing.plaf.basic.BasicButtonUI() {
            @Override
            public void paint(Graphics g, JComponent c) {
                Graphics2D g2 = (Graphics2D) g.create();
                g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                g2.setColor(Color.WHITE);
                g2.fillRoundRect(0, 0, c.getWidth() - 1, c.getHeight() - 1, 8, 8);
                g2.setColor(borderColor);
                g2.drawRoundRect(0, 0, c.getWidth() - 1, c.getHeight() - 1, 8, 8);
                g2.dispose();
                super.paint(g, c);
            }
        });
        return button;
    }

    private static JSeparator verticalDivider() {
        JSeparator separator = new JSeparator(SwingConstants.VERTICAL);
        separator.setForeground(AdminTheme.BORDER);
        separator.setPreferredSize(new Dimension(1, 38));
        return separator;
    }

    private static final class RemoteCommandIcon implements Icon {
        private enum Kind { LOCK, SHUTDOWN, RESTART }

        private final Kind kind;
        private final Color color;
        private final int size;

        private RemoteCommandIcon(Kind kind, Color color, int size) {
            this.kind = kind;
            this.color = color;
            this.size = size;
        }

        @Override public int getIconWidth() { return size; }
        @Override public int getIconHeight() { return size; }

        @Override
        public void paintIcon(Component component, Graphics graphics, int x, int y) {
            Graphics2D g = (Graphics2D) graphics.create();
            g.translate(x, y);
            g.scale(size / 36.0, size / 36.0);
            g.setColor(color);
            g.setStroke(new BasicStroke(2.8f, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
            switch (kind) {
                case LOCK -> {
                    g.drawRoundRect(7, 16, 22, 16, 3, 3);
                    Path2D shackle = new Path2D.Double();
                    shackle.moveTo(12, 16);
                    shackle.lineTo(12, 11);
                    shackle.curveTo(12, 3, 24, 3, 24, 11);
                    shackle.lineTo(24, 16);
                    g.draw(shackle);
                    g.fillOval(16, 21, 4, 4);
                    g.drawLine(18, 24, 18, 27);
                }
                case SHUTDOWN -> {
                    g.drawArc(7, 8, 22, 22, 135, 270);
                    g.drawLine(18, 3, 18, 18);
                }
                case RESTART -> {
                    Path2D arrow = new Path2D.Double();
                    arrow.moveTo(27, 12);
                    arrow.curveTo(24, 7, 19, 5, 14, 7);
                    arrow.curveTo(5, 10, 3, 21, 9, 27);
                    arrow.curveTo(15, 33, 25, 31, 29, 24);
                    g.draw(arrow);
                    g.drawLine(25, 5, 29, 12);
                    g.drawLine(29, 12, 21, 13);
                }
            }
            g.dispose();
        }
    }

    private static final class RemoteActionCardPanel extends JPanel {
        private final Color fill;
        private final Color border;
        private final Color accent;

        private RemoteActionCardPanel(Color fill, Color border, Color accent) {
            this.fill = fill;
            this.border = border;
            this.accent = accent;
            setOpaque(false);
        }

        @Override protected void paintComponent(Graphics graphics) {
            Graphics2D g = (Graphics2D) graphics.create();
            Shape originalClip = g.getClip();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            RoundRectangle2D shape = new RoundRectangle2D.Double(0, 0, getWidth() - 1, getHeight() - 1, 22, 22);
            g.setColor(fill);
            g.fill(shape);
            g.clip(shape);

            int width = getWidth();
            int height = getHeight();
            float waveTop = height * .70f;
            Path2D waveBack = new Path2D.Double();
            waveBack.moveTo(0, waveTop + height * .10);
            waveBack.curveTo(width * .22, waveTop - height * .13, width * .28, waveTop + height * .22, width * .52, waveTop + height * .04);
            waveBack.curveTo(width * .76, waveTop - height * .15, width * .82, waveTop + height * .20, width, waveTop - height * .02);
            waveBack.lineTo(width, height);
            waveBack.lineTo(0, height);
            waveBack.closePath();
            g.setPaint(new GradientPaint(0, waveTop - 10, new Color(accent.getRed(), accent.getGreen(), accent.getBlue(), 5),
                    0, height, new Color(accent.getRed(), accent.getGreen(), accent.getBlue(), 25)));
            g.fill(waveBack);

            Path2D waveFront = new Path2D.Double();
            waveFront.moveTo(0, waveTop + height * .20);
            waveFront.curveTo(width * .18, waveTop + height * .03, width * .30, waveTop + height * .24, width * .50, waveTop + height * .17);
            waveFront.curveTo(width * .71, waveTop + height * .10, width * .82, waveTop - height * .01, width, waveTop + height * .13);
            waveFront.lineTo(width, height);
            waveFront.lineTo(0, height);
            waveFront.closePath();
            g.setColor(new Color(accent.getRed(), accent.getGreen(), accent.getBlue(), 12));
            g.fill(waveFront);
            g.setClip(originalClip);
            g.setColor(border);
            g.setStroke(new BasicStroke(1.2f));
            g.draw(shape);
            g.dispose();
        }
    }

    private static final class RemoteLaptopArtwork extends JComponent {
        private final RemoteCommandIcon.Kind action;
        private final Color accent;

        private RemoteLaptopArtwork(RemoteCommandIcon.Kind action, Color accent) {
            this.action = action;
            this.accent = accent;
            setOpaque(false);
        }

        @Override protected void paintComponent(Graphics graphics) {
            Graphics2D g = (Graphics2D) graphics.create();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            int centerX = getWidth() / 2;
            int top = Math.max(4, (getHeight() - 112) / 2);
            int screenWidth = Math.min(158, Math.max(112, getWidth() / 2));
            int screenHeight = 82;
            int screenX = centerX - screenWidth / 2;

            g.setStroke(new BasicStroke(3.5f, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
            g.setColor(new Color(accent.getRed(), accent.getGreen(), accent.getBlue(), 45));
            g.fillRoundRect(screenX + 6, top + 6, screenWidth - 12, screenHeight - 11, 5, 5);
            g.setColor(accent);
            g.drawRoundRect(screenX, top, screenWidth, screenHeight, 6, 6);
            int iconSize = 42;
            new RemoteCommandIcon(action, accent, iconSize).paintIcon(this, g,
                    centerX - iconSize / 2, top + (screenHeight - iconSize) / 2);

            Path2D base = new Path2D.Double();
            base.moveTo(screenX - 14, top + screenHeight + 7);
            base.lineTo(screenX + screenWidth + 14, top + screenHeight + 7);
            base.curveTo(screenX + screenWidth + 14, top + screenHeight + 14,
                    screenX + screenWidth + 8, top + screenHeight + 17, screenX + screenWidth, top + screenHeight + 17);
            base.lineTo(screenX, top + screenHeight + 17);
            base.curveTo(screenX - 8, top + screenHeight + 17, screenX - 14, top + screenHeight + 14,
                    screenX - 14, top + screenHeight + 7);
            base.closePath();
            g.setColor(new Color(accent.getRed(), accent.getGreen(), accent.getBlue(), 55));
            g.fill(base);
            g.setColor(accent);
            g.draw(base);
            g.dispose();
        }
    }

    private static final class RoundedPanel extends JPanel {
        private final int radius;
        private final Color bgColor;
        private final Color borderColor;

        private RoundedPanel(int radius, Color bgColor, Color borderColor) {
            this.radius = radius;
            this.bgColor = bgColor;
            this.borderColor = borderColor;
            setOpaque(false);
        }

        @Override
        protected void paintComponent(Graphics g) {
            Graphics2D g2 = (Graphics2D) g.create();
            g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            if (bgColor != null) {
                g2.setColor(bgColor);
                g2.fillRoundRect(0, 0, getWidth() - 1, getHeight() - 1, radius, radius);
            }
            if (borderColor != null) {
                g2.setColor(borderColor);
                g2.drawRoundRect(0, 0, getWidth() - 1, getHeight() - 1, radius, radius);
            }
            g2.dispose();
            super.paintComponent(g);
        }
    }

    private static final class ScrollablePanel extends JPanel implements Scrollable {
        @Override public Dimension getPreferredScrollableViewportSize() { return getPreferredSize(); }
        @Override public int getScrollableUnitIncrement(Rectangle visibleRect, int orientation, int direction) { return 18; }
        @Override public int getScrollableBlockIncrement(Rectangle visibleRect, int orientation, int direction) {
            return Math.max(18, orientation == SwingConstants.VERTICAL ? visibleRect.height - 18 : visibleRect.width - 18);
        }
        @Override public boolean getScrollableTracksViewportWidth() { return true; }
        @Override public boolean getScrollableTracksViewportHeight() { return true; }
    }

    private static final class RoundedBorder extends javax.swing.border.AbstractBorder {
        private final Color color;
        private final int radius;

        private RoundedBorder(Color color, int radius) {
            this.color = color;
            this.radius = radius;
        }

        @Override
        public void paintBorder(Component component, Graphics graphics, int x, int y, int width, int height) {
            Graphics2D g = (Graphics2D) graphics.create();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            g.setColor(color);
            g.drawRoundRect(x, y, width - 1, height - 1, radius, radius);
            g.dispose();
        }

        @Override public Insets getBorderInsets(Component component) { return new Insets(1, 1, 1, 1); }
        @Override public Insets getBorderInsets(Component component, Insets insets) {
            insets.set(1, 1, 1, 1);
            return insets;
        }
    }

    private void refreshDashboard() {
        presenter.refresh().whenComplete((state, error) -> SwingUtilities.invokeLater(() -> {
            if (error != null) {
                operationStatus.setText("Dashboard refresh failed: " + rootMessage(error));
                return;
            }
            applyDashboard(state);
            String selectedSession = selectedSessionId();
            if (selectedSession != null) presenter.remoteAdministrationResult(selectedSession).thenAccept(result ->
                    SwingUtilities.invokeLater(() -> updateRemoteAdministrationStatus(result)));
        }));
    }

    private void updateRemoteAdministrationStatus(Map<String, Object> result) {
        String requestId = value(result.get("requestId"));
        if (activeRemoteRequestId == null || !activeRemoteRequestId.equals(requestId)) return;
        int status = result.get("status") instanceof Number n ? n.intValue() : 0;
        String message = value(result.get("result"));
        if (message.equals("--")) message = value(result.get("errorMessage"));
        operationStatus.setText(switch (status) {
            case 1 -> "Client scheduled operation: " + message;
            case 2 -> "Client canceled or rejected operation: " + message;
            case 3 -> "Operation completed: " + message;
            case 4, 5 -> "Operation failed: " + message;
            default -> operationStatus.getText();
        });
    }

    private void applyDashboard(AdminDashboardPresenter.DashboardState state) {
        boolean listening = state.listening();
        serverState.setText(listening ? "● Server Running" : "○ Server Stopped");
        serverState.setForeground(listening ? AdminTheme.GREEN : AdminTheme.PINK);
        headerServerState.setText(listening ? "● Server Running" : "○ Server Stopped");
        headerServerState.setForeground(listening ? AdminTheme.GREEN : AdminTheme.PINK);

        clock.setText(java.time.ZonedDateTime.now().format(timeFormat));
        date.setText(java.time.ZonedDateTime.now().format(dateFormat));

        List<AdminClientSnapshot> clients = state.clients();
        currentClients = clients;
        long online = clients.stream().filter(AdminClientSnapshot::online).count();
        managedCount.setText(Integer.toString(clients.size()));
        onlineCount.setText(Long.toString(online));
        headerOnlineCount.setText(Long.toString(online));

        refreshingClients = true;
        String selected = selectedSessionId();
        clientPicker.removeAllItems();
        for (AdminClientSnapshot client : clients) {
            clientPicker.addItem(new ClientChoice(client.sessionId(), client.clientName(), client.networkInfo(), client.online()));
        }
        if (clients.isEmpty()) {
            clientPicker.addItem(new ClientChoice("", "No agent selected - choose from Clients", Map.of(), false));
        }
        if (selected != null) {
            for (int i = 0; i < clientPicker.getItemCount(); i++) {
                if (clientPicker.getItemAt(i).sessionId.equals(selected)) {
                    clientPicker.setSelectedIndex(i);
                    break;
                }
            }
        }
        refreshingClients = false;
        refreshSelectedClient();

        // Check for unread incoming chat messages from active clients
        for (AdminClientSnapshot client : clients) {
            chatPresenter.messages(client.sessionId()).whenComplete((messages, err) -> {
                if (err == null && messages != null) {
                    int unread = 0;
                    for (var msg : messages) {
                        if (!msg.sender().equalsIgnoreCase("Admin") && !readMessageIds.contains(msg.messageId())) {
                            unread++;
                        }
                    }
                    final int count = openChatSessions.contains(client.sessionId()) ? 0 : unread;
                    SwingUtilities.invokeLater(() -> {
                        unreadBySession.put(client.sessionId(), count);
                        updateUnreadChatCount(unreadBySession.values().stream().mapToInt(Integer::intValue).sum());
                        clientPicker.repaint();
                    });
                }
            });
        }
        if (clients.isEmpty()) {
            unreadBySession.clear();
            updateUnreadChatCount(0);
        }
    }

    private void updateUnreadChatCount(int count) {
        if (this.unreadChatCount != count) {
            this.unreadChatCount = count;
            if (sidebarChatButton != null) sidebarChatButton.repaint();
            if (headerChatButton != null) headerChatButton.repaint();
        }
    }

    private void setRemoteActionsEnabled(boolean enabled) {
        if (sidebarViewScreenButton != null) sidebarViewScreenButton.setEnabled(enabled);
        if (sidebarRemoteAdministrationButton != null) sidebarRemoteAdministrationButton.setEnabled(enabled);
        refreshRemoteAdministrationPage();
    }

    private String selectedSessionId() {
        Object selected = clientPicker.getSelectedItem();
        return selected instanceof ClientChoice c ? c.sessionId : null;
    }

    private void refreshSelectedClient() {
        if (refreshingClients) return;
        ClientChoice choice = (ClientChoice) clientPicker.getSelectedItem();
        if (choice == null || choice.sessionId.isEmpty()) {
            showNoClient();
            return;
        }
        AdminClientSnapshot snapshot = currentClients.stream()
                .filter(client -> client.sessionId().equals(choice.sessionId))
                .findFirst().orElse(null);
        if (snapshot == null) {
            showNoClient();
            return;
        }

        clientName.setText(snapshot.clientName());
        boolean isOnline = snapshot.online();
        clientBadgeText.setText(isOnline ? "Online" : "Offline");
        clientBadgePill.setBackground(isOnline ? AdminTheme.GREEN : AdminTheme.PINK);

        Map<String, Object> info = snapshot.networkInfo();
        String os = value(info.get("osName"));
        if (os.equals("--") || os.isBlank()) os = "Windows 11 Pro";
        clientOs.setText(os);

        clientId.setText(snapshot.clientId());
        clientId.setToolTipText(snapshot.clientId());
        sessionId.setText(snapshot.sessionId());
        sessionId.setToolTipText(snapshot.sessionId());
        connectedAt.setText(format(snapshot.connectedAt()));
        lastSeen.setText(format(snapshot.lastSeen()));

        monitorOsName.setText(value(info.get("osName")));
        monitorOsVersion.setText(value(info.get("osVersion")));
        monitorArchitecture.setText(value(info.get("architecture")));
        monitorClientValue.setText(isOnline ? "Online" : "Offline");
        monitorClientValue.setForeground(isOnline ? AdminTheme.GREEN : AdminTheme.PINK);
        monitorConnectionStatus.setText(isOnline ? "Connected" : "Disconnected");
        monitorConnectionStatus.setForeground(isOnline ? AdminTheme.GREEN : AdminTheme.PINK);
        monitorLastSeen.setText("Last seen " + format(snapshot.lastSeen()));
        fillInterfaces(snapshot.networkInfo());
        fillMonitoring(snapshot.systemStats(), snapshot.lastSeen());
        fillLastScan(snapshot.lastScan());
        refreshRemoteAdministrationPage();

        setRemoteActionsEnabled(isOnline);
        scanButton.setEnabled(isOnline && !operationsBusy);
        pingButton.setEnabled(isOnline && !operationsBusy);

        operationStatus.setText("Selected: " + snapshot.clientName() + "  |  Session: " + snapshot.sessionId());
    }

    private void showNoClient() {
        clientName.setText("No client selected");
        clientBadgeText.setText("Waiting");
        clientBadgePill.setBackground(AdminTheme.MUTED);

        clientId.setText("--");
        sessionId.setText("--");
        connectedAt.setText("--");
        lastSeen.setText("--");
        clientOs.setText("--");
        refreshRemoteAdministrationPage();

        monitorOsName.setText("--");
        monitorOsVersion.setText("--");
        monitorArchitecture.setText("--");
        monitorSystemInterface.setText("--");
        monitorSystemIpAddress.setText("--");
        monitorInterface.setText("--");
        monitorIpAddress.setText("--");
        monitorMacAddress.setText("--");
        monitorGateway.setText("--");
        monitorSubnet.setText("--");
        monitorNetworkValue.setText("--");
        monitorClientValue.setText("Waiting");
        monitorClientValue.setForeground(AdminTheme.MUTED);
        monitorConnectionStatus.setText("Waiting for client");
        monitorConnectionStatus.setForeground(AdminTheme.MUTED);
        monitorCpuStatus.setText("Waiting for telemetry");
        monitorRamStatus.setText("Waiting for telemetry");
        monitorLastSeen.setText("--");
        lastTelemetrySampleAt = null;
        monitorCpuStatusValue.setText("--");
        monitorRamStatusValue.setText("--");
        monitorNetworkStatusValue.setText("--");
        monitorCpuValue.setText("--");
        monitorRamValue.setText("--");

        interfaceName.setText("--");
        primaryIpv4.setText("--");
        primaryMac.setText("--");
        primaryGateway.setText("--");
        primarySubnet.setText("--");

        cpuRing.setUnavailable();
        ramRing.setUnavailable();
        telemetryChart.clear();
        detailCpuRing.setUnavailable();
        detailRamRing.setUnavailable();
        detailTelemetryChart.clear();

        scanModel.setRowCount(0);
        pingModel.setRowCount(0);

        pingSent.setText("--");
        pingReceived.setText("--");
        pingLost.setText("--");
        pingLoss.setText("--");
        pingSentStat.setText("--");
        pingReceivedStat.setText("--");
        pingLostStat.setText("--");
        pingLossStat.setText("--");
        pingRtt.setText("-- ms");

        hostsCount.setText("0");
        setRemoteActionsEnabled(false);

        scanButton.setEnabled(false);
        pingButton.setEnabled(false);
        operationStatus.setText("Ready");
    }

    private void fillInterfaces(Map<String, Object> networkInfo) {
        Object rows = networkInfo.get("interfaces");
        if (!(rows instanceof List<?> list) || list.isEmpty()) {
            interfaceName.setText("--");
            primaryIpv4.setText("--");
            primaryMac.setText("--");
            primaryGateway.setText("--");
            primarySubnet.setText("--");
            monitorInterface.setText("--");
            monitorIpAddress.setText("--");
            monitorMacAddress.setText("--");
            monitorGateway.setText("--");
            monitorSubnet.setText("--");
            monitorNetworkValue.setText("Unavailable");
            monitorSystemInterface.setText("--");
            monitorSystemIpAddress.setText("--");
            monitorNetworkStatusValue.setText("Unavailable");
            return;
        }
        if (list.getFirst() instanceof Map<?, ?> primary) {
            interfaceName.setText(value(primary.get("name")));
            primaryIpv4.setText(value(primary.get("ipAddress")));
            primaryMac.setText(value(primary.get("macAddress")));
            primaryGateway.setText(value(primary.get("gateway")));
            primarySubnet.setText(value(primary.get("subnet")));
            monitorInterface.setText(value(primary.get("name")));
            monitorIpAddress.setText(value(primary.get("ipAddress")));
            monitorMacAddress.setText(value(primary.get("macAddress")));
            monitorGateway.setText(value(primary.get("gateway")));
            monitorSubnet.setText(value(primary.get("subnet")));
            monitorNetworkValue.setText(value(primary.get("name")));
            monitorSystemInterface.setText(value(primary.get("name")));
            monitorSystemIpAddress.setText(value(primary.get("ipAddress")));
            monitorNetworkStatusValue.setText("Available");
        }
    }

    private void fillMonitoring(Map<String, Object> stats, java.time.Instant sampleAt) {
        if (stats == null || (!stats.containsKey("cpuUsagePercent") && !stats.containsKey("ramUsagePercent"))) {
            cpuRing.setUnavailable();
            ramRing.setUnavailable();
            telemetryChart.clear();
            detailCpuRing.setUnavailable();
            detailRamRing.setUnavailable();
            detailTelemetryChart.clear();
            monitorCpuValue.setText("--");
            monitorRamValue.setText("--");
            monitorCpuStatusValue.setText("--");
            monitorRamStatusValue.setText("--");
            monitorCpuStatus.setText("Waiting for telemetry");
            monitorRamStatus.setText("Waiting for telemetry");
            return;
        }
        boolean hasCpu = stats.get("cpuUsagePercent") instanceof Number;
        boolean hasRam = stats.get("ramUsagePercent") instanceof Number;
        if (hasCpu) {
            double cpu = number(stats.get("cpuUsagePercent"));
            cpuRing.setValue(cpu);
            detailCpuRing.setValue(cpu);
            monitorCpuValue.setText(String.format(java.util.Locale.ROOT, "%.0f%%", cpu));
            monitorCpuStatusValue.setText(String.format(java.util.Locale.ROOT, "%.0f%%", cpu));
            monitorCpuStatus.setText("Receiving live samples");
        } else {
            cpuRing.setUnavailable(); detailCpuRing.setUnavailable(); monitorCpuValue.setText("--");
            monitorCpuStatusValue.setText("--");
            monitorCpuStatus.setText("Unavailable");
        }
        if (hasRam) {
            double ram = number(stats.get("ramUsagePercent"));
            ramRing.setValue(ram);
            detailRamRing.setValue(ram);
            monitorRamValue.setText(String.format(java.util.Locale.ROOT, "%.0f%%", ram));
            monitorRamStatusValue.setText(String.format(java.util.Locale.ROOT, "%.0f%%", ram));
            monitorRamStatus.setText("Receiving live samples");
        } else {
            ramRing.setUnavailable(); detailRamRing.setUnavailable(); monitorRamValue.setText("--");
            monitorRamStatusValue.setText("--");
            monitorRamStatus.setText("Unavailable");
        }
        if (hasCpu && hasRam) {
            double cpu = number(stats.get("cpuUsagePercent"));
            double ram = number(stats.get("ramUsagePercent"));
            if (sampleAt != null && !sampleAt.equals(lastTelemetrySampleAt)) {
                telemetryChart.addSample(cpu, ram, sampleAt.toEpochMilli());
                detailTelemetryChart.addSample(cpu, ram, sampleAt.toEpochMilli());
                lastTelemetrySampleAt = sampleAt;
            }
        }
    }

    private void fillLastScan(Map<String, Object> result) {
        if (result.isEmpty()) {
            scanModel.setRowCount(0);
            hostsCount.setText("0");
            scanFound.setText("0");
            scanOnline.setText("0");
            scanOffline.setText("0");
            scanDuration.setText("--");
            return;
        }
        Object rows = result.get("devices");
        scanModel.setRowCount(0);
        if (rows instanceof List<?> list) {
            int onlineCount = 0;
            int rowNumber = 0;
            for (Object item : list) {
                if (item instanceof Map<?, ?> device) {
                    boolean online = Boolean.TRUE.equals(device.get("isOnline"));
                    if (online) onlineCount++;
                    scanModel.addRow(new Object[]{
                            ++rowNumber,
                            value(device.get("ipAddress")),
                            value(device.get("macAddress")),
                            online ? "Online" : "Offline",
                            device.get("responseTimeMs") == null ? "--" : value(device.get("responseTimeMs")) + " ms"
                    });
                }
            }
            int foundCount = scanModel.getRowCount();
            hostsCount.setText(Integer.toString(foundCount));
            scanFound.setText(Integer.toString(foundCount));
            scanOnline.setText(Integer.toString(onlineCount));
            scanOffline.setText(Integer.toString(foundCount - onlineCount));
            scanResultStatus.setText("Found " + foundCount + " host" + (foundCount == 1 ? "" : "s")
                    + " · " + (Boolean.TRUE.equals(result.get("success")) ? "Scan completed" : value(result.get("errorMessage"))));
        } else {
            hostsCount.setText("0");
            scanFound.setText("0");
            scanOnline.setText("0");
            scanOffline.setText("0");
            scanDuration.setText("--");
            scanResultStatus.setText("Scan returned no device list.");
        }
    }

    private void runPing() {
        AdminClientSnapshot selected = selectedClient();
        if (selected == null) return;
        String target = targetField.getText().trim();
        if (target.isBlank()) {
            targetField.requestFocusInWindow();
            return;
        }
        pingModel.setRowCount(0);
        pingOutput.setText("Checking " + target + " with 4 probes (1000 ms timeout)...\n");
        pingSent.setText("--");
        pingReceived.setText("--");
        pingLost.setText("--");
        pingLoss.setText("--");
        pingRtt.setText("-- ms");
        pingMin.setText("-- ms");
        pingMax.setText("-- ms");
        pingResultStatus.setText("Pinging " + target + "…");
        pingResultStatus.setForeground(AdminTheme.BLUE);
        setOperationsEnabled(false, "Pinging " + target + " from " + selected.clientName() + "...");
        presenter.ping(selected.sessionId(), target).whenComplete((result, error) -> SwingUtilities.invokeLater(() -> {
            if (error == null) {
                showPing(result);
                operationStatus.setText("Ping completed for " + selected.clientName());
            } else {
                pingResultStatus.setText("Ping failed: " + rootMessage(error));
                pingOutput.append("Ping failed: " + rootMessage(error) + "\n");
                operationStatus.setText("Ping failed: " + rootMessage(error));
            }
            setOperationsEnabled(true, operationStatus.getText());
        }));
    }

    private void viewScreen() {
        AdminClientSnapshot selected = selectedClient();
        if (selected == null) return;
        try {
            screenShare.requestViewing(selected.clientId(), selected.sessionId());
            ScreenViewerFrame viewer = new ScreenViewerFrame(selected.clientName(), selected.clientId(), selected.sessionId(), screenShare, remoteControl);
            viewer.setVisible(true);
        } catch (Exception error) {
            JOptionPane.showMessageDialog(this, rootMessage(error), "Cannot view screen", JOptionPane.WARNING_MESSAGE);
        }
    }

    private JComponent remoteActionCard(String title, String description, RemoteCommandIcon.Kind icon,
                                        Color accent, Color tint, Color borderColor, Runnable action) {
        RemoteActionCardPanel card = new RemoteActionCardPanel(tint, borderColor, accent);
        card.setLayout(new BorderLayout(0, 4));
        card.setBorder(BorderFactory.createEmptyBorder(16, 17, 15, 17));

        JPanel descriptionRow = new JPanel(new BorderLayout(16, 0));
        descriptionRow.setOpaque(false);
        JLabel glyph = new JLabel(new RemoteCommandIcon(icon, accent, 48));
        glyph.setHorizontalAlignment(SwingConstants.CENTER);
        glyph.setVerticalAlignment(SwingConstants.TOP);
        descriptionRow.add(glyph, BorderLayout.WEST);
        JPanel text = new JPanel();
        text.setOpaque(false);
        text.setLayout(new BoxLayout(text, BoxLayout.Y_AXIS));
        text.add(label(title, AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 21)));
        text.add(Box.createVerticalStrut(5));
        String cardDescription = switch (title) {
            case "Lock Screen" -> "Lock the Windows session on the client (require password to unlock).";
            default -> description;
        };
        JLabel detail = label("<html><div style='width:220px'>" + cardDescription + "</div></html>",
                new Color(28, 51, 86), new Font("Segoe UI", Font.PLAIN, 16));
        detail.setVerticalAlignment(SwingConstants.TOP);
        text.add(detail);
        descriptionRow.add(text, BorderLayout.CENTER);
        card.add(descriptionRow, BorderLayout.NORTH);
        RemoteLaptopArtwork artwork = new RemoteLaptopArtwork(icon, accent);
        artwork.setPreferredSize(new Dimension(280, 120));
        card.add(artwork, BorderLayout.CENTER);

        JButton run = new JButton(title, new RemoteCommandIcon(icon, Color.WHITE, 21));
        run.setFont(new Font("Segoe UI", Font.BOLD, 17));
        run.setForeground(Color.WHITE);
        run.setBackground(accent);
        run.setOpaque(true);
        run.setFocusPainted(false);
        run.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        run.setIconTextGap(16);
        run.setBorder(BorderFactory.createEmptyBorder(12, 14, 12, 14));
        run.setUI(new javax.swing.plaf.basic.BasicButtonUI() {
            @Override public void paint(Graphics g, JComponent c) {
                Graphics2D g2 = (Graphics2D) g.create();
                g2.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
                g2.setPaint(new GradientPaint(0, 0, run.getBackground().brighter(), 0, c.getHeight(), run.getBackground().darker()));
                g2.fillRoundRect(0, 0, c.getWidth(), c.getHeight(), 14, 14);
                g2.dispose();
                super.paint(g, c);
            }
        });
        run.addActionListener(event -> action.run());
        card.add(run, BorderLayout.SOUTH);
        return card;
    }

    private void refreshRemoteAdministrationPage() {
        if (remoteAdminActions == null) return;
        AdminClientSnapshot selected = selectedSnapshot();
        if (selected == null) {
            remoteClientNameValue.setText("No client selected");
            remoteSessionValue.setText("Session: --");
            remoteClientStatusValue.setText("Offline");
            remoteClientStatusValue.setForeground(AdminTheme.PINK);
            remoteOsValue.setText("--");
            remoteCpuValue.setText("--");
            remoteRamValue.setText("--");
        } else {
            remoteClientNameValue.setText(selected.clientName());
            remoteSessionValue.setText("Session: " + selected.sessionId());
            remoteSessionValue.setToolTipText(selected.sessionId());
            remoteClientStatusValue.setText(selected.online() ? "● Online" : "● Offline");
            remoteClientStatusValue.setForeground(selected.online() ? AdminTheme.GREEN : AdminTheme.PINK);
            Map<String, Object> info = selected.networkInfo();
            String os = value(info.get("osName"));
            String version = value(info.get("osVersion"));
            remoteOsValue.setText("--".equals(version) ? os : os + " " + version);
            Map<String, Object> stats = selected.systemStats();
            remoteCpuValue.setText(stats.get("cpuUsagePercent") instanceof Number n
                    ? String.format(java.util.Locale.ROOT, "%.0f%%", n.doubleValue()) : "--");
            remoteRamValue.setText(stats.get("ramUsagePercent") instanceof Number n
                    ? String.format(java.util.Locale.ROOT, "%.0f%%", n.doubleValue()) : "--");
        }
        setEnabledRecursively(remoteAdminActions, selected != null && selected.online() && !operationsBusy);
    }

    private static void setEnabledRecursively(Component component, boolean enabled) {
        component.setEnabled(enabled);
        if (component instanceof Container container) {
            for (Component child : container.getComponents()) setEnabledRecursively(child, enabled);
        }
    }

    private void runRemoteCommand(int command, int countdownSeconds) {
        AdminClientSnapshot selected = selectedClient();
        if (selected != null) confirmRemoteCommand(selected, command, countdownSeconds);
    }

    private void confirmRemoteCommand(AdminClientSnapshot selected, int command, int countdownSeconds) {
        String action = switch (command) {
            case 2 -> "Shut down";
            case 3 -> "Restart";
            default -> "Lock";
        };
        String detail = countdownSeconds > 0
                ? action + " " + selected.clientName() + " after a " + countdownSeconds
                    + " second warning? The Client can cancel during the countdown."
                : action + " the active workstation on " + selected.clientName() + "?";
        int answer = JOptionPane.showConfirmDialog(this, detail, "Confirm " + action,
                JOptionPane.YES_NO_OPTION, JOptionPane.WARNING_MESSAGE);
        if (answer != JOptionPane.YES_OPTION) return;
        operationStatus.setText(action + " request sent to " + selected.clientName() + "…");
        presenter.remoteAdministration(selected.sessionId(), command, countdownSeconds).whenComplete((requestId, error) ->
                SwingUtilities.invokeLater(() -> {
                    if (error == null) {
                        activeRemoteRequestId = requestId;
                        operationStatus.setText(action + " command sent to Client. Request: " + requestId);
                    } else operationStatus.setText(action + " request failed: " + rootMessage(error));
                }));
    }

    private void openChat() {
        AdminClientSnapshot selected = currentClients.stream()
                .filter(client -> unreadBySession.getOrDefault(client.sessionId(), 0) > 0)
                .max(java.util.Comparator.comparingInt(client -> unreadBySession.getOrDefault(client.sessionId(), 0)))
                .orElseGet(this::selectedClient);
        if (selected == null) return;
        if (!selected.sessionId().equals(selectedSessionId())) {
            for (int i = 0; i < clientPicker.getItemCount(); i++) {
                if (clientPicker.getItemAt(i).sessionId.equals(selected.sessionId())) {
                    clientPicker.setSelectedIndex(i);
                    break;
                }
            }
        }
        chatPresenter.messages(selected.sessionId()).whenComplete((messages, err) -> {
            if (err == null && messages != null) {
                for (var msg : messages) {
                    readMessageIds.add(msg.messageId());
                }
            }
            SwingUtilities.invokeLater(() -> {
                unreadBySession.put(selected.sessionId(), 0);
                updateUnreadChatCount(unreadBySession.values().stream().mapToInt(Integer::intValue).sum());
                clientPicker.repaint();
            });
        });
        AdminChatDialog dialog = new AdminChatDialog(this, selected.clientName(), selected.clientId(), selected.sessionId(), chatPresenter);
        openChatSessions.add(selected.sessionId());
        unreadBySession.put(selected.sessionId(), 0);
        updateUnreadChatCount(unreadBySession.values().stream().mapToInt(Integer::intValue).sum());
        clientPicker.repaint();
        dialog.addWindowListener(new java.awt.event.WindowAdapter() {
            @Override public void windowClosed(java.awt.event.WindowEvent event) {
                openChatSessions.remove(selected.sessionId());
            }
        });
        dialog.setVisible(true);
    }

    private void disconnectSelectedClient() {
        AdminClientSnapshot selected = selectedSnapshot();
        if (selected == null) {
            JOptionPane.showMessageDialog(this, "No agent selected to disconnect.", "Disconnect", JOptionPane.INFORMATION_MESSAGE);
            return;
        }
        int confirm = JOptionPane.showConfirmDialog(this,
                "Are you sure you want to disconnect agent '" + selected.clientName() + "'?",
                "Confirm Disconnect", JOptionPane.YES_NO_OPTION, JOptionPane.WARNING_MESSAGE);
        if (confirm == JOptionPane.YES_OPTION) {
            operationStatus.setText("Disconnected " + selected.clientName());
            showNoClient();
        }
    }

    private void showPing(Map<String, Object> result) {
        double avg = number(result.get("averageResponseTimeMs"));
        pingAverage.setText(result.get("averageResponseTimeMs") == null ? "--" : String.format(java.util.Locale.ROOT, "%.1f ms", avg));
        pingModel.setRowCount(0);
        String host = value(result.get("host"));
        StringBuilder output = new StringBuilder("Reachability test for ").append(host)
                .append(" (4 probes, 1000 ms timeout)\n\n");
        Object attempts = result.get("attempts");
        int sent = 0, received = 0;
        if (attempts instanceof List<?> list) {
            for (Object item : list) {
                if (item instanceof Map<?, ?> attempt) {
                    sent++;
                    boolean success = Boolean.TRUE.equals(attempt.get("success"));
                    if (success) received++;
                    String time = attempt.get("responseTimeMs") == null ? "" : " time=" + attempt.get("responseTimeMs") + " ms";
                    output.append(success ? "Reply from " + host + ":" + time
                            : "Probe " + value(attempt.get("number")) + " failed: " + value(attempt.get("status")))
                            .append('\n');
                    pingModel.addRow(new Object[]{
                            value(attempt.get("number")),
                            success ? "Reply" : value(attempt.get("status")),
                            nullable(attempt.get("responseTimeMs"))
                    });
                }
            }
        }
        if (!(attempts instanceof List<?> list) || list.isEmpty()) {
            output.append(Boolean.TRUE.equals(result.get("success"))
                    ? "Reply from " + host + ": time=" + value(result.get("roundtripTimeMs")) + " ms\n"
                    : "Probe failed: " + value(result.get("statusMessage")) + "\n");
            pingModel.addRow(new Object[]{
                    "1",
                    Boolean.TRUE.equals(result.get("success")) ? "Reply" : value(result.get("statusMessage")),
                    nullable(result.get("roundtripTimeMs"))
            });
        }
        if (sent == 0) {
            sent = 1;
            received = Boolean.TRUE.equals(result.get("success")) ? 1 : 0;
        }
        pingSent.setText(Integer.toString(sent));
        pingReceived.setText(Integer.toString(received));
        pingLost.setText(Integer.toString(sent - received));
        pingLoss.setText(String.format(java.util.Locale.ROOT, "%.0f%%", 100.0 * (sent - received) / sent));
        pingSentStat.setText(Integer.toString(sent));
        pingReceivedStat.setText(Integer.toString(received));
        pingLostStat.setText(Integer.toString(sent - received));
        pingLossStat.setText(pingLoss.getText());
        pingRtt.setText(result.get("averageResponseTimeMs") == null ? "-- ms" : String.format(java.util.Locale.ROOT, "%.0f ms", avg));
        java.util.List<Double> measured = new java.util.ArrayList<>();
        for (int row = 0; row < pingModel.getRowCount(); row++) {
            Double rtt = parseRtt(pingModel.getValueAt(row, 2));
            if (rtt != null) measured.add(rtt);
        }
        pingMin.setText(measured.isEmpty() ? "-- ms" : String.format(java.util.Locale.ROOT, "%.1f ms", measured.stream().mapToDouble(Double::doubleValue).min().orElseThrow()));
        pingMax.setText(measured.isEmpty() ? "-- ms" : String.format(java.util.Locale.ROOT, "%.1f ms", measured.stream().mapToDouble(Double::doubleValue).max().orElseThrow()));
        pingResultStatus.setText(received > 0 ? "Ping completed successfully" : "Ping completed with no replies");
        pingResultStatus.setForeground(received > 0 ? AdminTheme.GREEN : AdminTheme.PINK);
        output.append("\nPackets: sent=").append(sent).append(", received=").append(received)
                .append(", lost=").append(sent - received).append(" ( ")
                .append(String.format(java.util.Locale.ROOT, "%.0f", 100.0 * (sent - received) / sent)).append("% )\n");
        output.append("Average RTT: ").append(result.get("averageResponseTimeMs") == null ? "--" : String.format(java.util.Locale.ROOT, "%.1f ms", avg));
        pingOutput.setText(output.toString());
        pingOutput.setCaretPosition(0);
        if (pingChart != null) pingChart.repaint();
    }

    private void runScan() {
        AdminClientSnapshot selected = selectedClient();
        if (selected == null) return;
        String subnet = subnetField.getText().trim();
        int timeoutMs = Integer.parseInt(((String) scanTimeout.getSelectedItem()).replace(" ms", ""));
        scanDuration.setText("--");
        scanInProgress = true;
        activeScanSession = selected.sessionId();
        scanResultStatus.setText("Scanning network from " + selected.clientName() + "…");
        cancelScanButton.setEnabled(true);
        setOperationsEnabled(false, "Scanning from " + selected.clientName() + "...");
        long startedAt = System.nanoTime();
        presenter.scan(selected.sessionId(), subnet, timeoutMs).whenComplete((result, error) -> SwingUtilities.invokeLater(() -> {
            if (error == null) {
                long elapsedMs = java.util.concurrent.TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - startedAt);
                scanDuration.setText(String.format(java.util.Locale.ROOT, "%.2f s", elapsedMs / 1000.0));
                fillLastScan(result);
                operationStatus.setText(Boolean.TRUE.equals(result.get("success")) ? "Scan completed" : value(result.get("errorMessage")));
            } else {
                scanResultStatus.setText("Scan failed: " + rootMessage(error));
                operationStatus.setText("Scan failed: " + rootMessage(error));
            }
            scanInProgress = false;
            activeScanSession = null;
            cancelScanButton.setEnabled(false);
            setOperationsEnabled(true, operationStatus.getText());
        }));
    }

    private void cancelScan() {
        String session = activeScanSession;
        if (!scanInProgress || session == null) return;
        cancelScanButton.setEnabled(false);
        operationStatus.setText("Canceling LAN scan...");
        presenter.cancelScan(session).whenComplete((ignored, error) -> SwingUtilities.invokeLater(() -> {
            if (error != null) operationStatus.setText("Could not cancel scan: " + rootMessage(error));
        }));
    }

    private AdminClientSnapshot selectedClient() {
        ClientChoice choice = (ClientChoice) clientPicker.getSelectedItem();
        if (choice == null || choice.sessionId.isEmpty()) {
            operationStatus.setText("Connect a Client before starting a remote operation.");
            return null;
        }
        AdminClientSnapshot snapshot = currentClients.stream()
                .filter(client -> client.sessionId().equals(choice.sessionId))
                .findFirst().orElse(null);
        if (snapshot == null) {
            operationStatus.setText("Selected Client session is no longer active.");
            return null;
        }
        if (!snapshot.online()) {
            operationStatus.setText("Selected Client is offline; reconnect it before starting an operation.");
            return null;
        }
        return snapshot;
    }

    private void setOperationsEnabled(boolean enabled, String message) {
        operationsBusy = !enabled;
        AdminClientSnapshot selected = selectedSnapshot();
        boolean online = selected != null && selected.online();
        scanButton.setEnabled(enabled && online);
        pingButton.setEnabled(enabled && online);
        cancelScanButton.setEnabled(!enabled && scanInProgress);
        refreshRemoteAdministrationPage();
        operationStatus.setText(message);
    }

    private AdminClientSnapshot selectedSnapshot() {
        String selected = selectedSessionId();
        return selected == null ? null : currentClients.stream().filter(client -> client.sessionId().equals(selected)).findFirst().orElse(null);
    }

    private void closeApp() {
        refreshTimer.stop();
        closeAction.run();
        dispose();
    }

    private static String format(java.time.Instant instant) {
        return instant == null ? "--" : java.time.format.DateTimeFormatter.ofPattern("M/d/yyyy h:mm:ss a", java.util.Locale.US)
                .withZone(ZoneId.systemDefault()).format(instant);
    }

    private static String value(Object value) {
        return value == null || value.toString().isBlank() ? "--" : value.toString();
    }

    private static Object nullable(Object value) {
        return value == null ? "--" : value + (value instanceof Number ? " ms" : "");
    }

    private static double number(Object value) {
        return value instanceof Number n && Double.isFinite(n.doubleValue()) ? n.doubleValue() : 0;
    }

    private static String rootMessage(Throwable error) {
        Throwable current = error instanceof CompletionException && error.getCause() != null ? error.getCause() : error;
        while (current.getCause() != null) current = current.getCause();
        return current.getMessage() == null ? current.getClass().getSimpleName() : current.getMessage();
    }

    private record ClientChoice(String sessionId, String clientName, Map<String, Object> networkInfo, boolean online) {
        @Override
        public String toString() {
            if (sessionId.isEmpty()) return clientName;
            String sId = sessionId.length() > 8 ? sessionId.substring(0, 8) : sessionId;
            return clientName + "  |  " + (online ? "Online" : "Offline") + "  |  Session: " + sId;
        }
    }

    private static final class MonitoringChart extends JComponent {
        private final java.util.ArrayDeque<double[]> samples = new java.util.ArrayDeque<>();

        private MonitoringChart() {
            setPreferredSize(new Dimension(460, 240));
            setOpaque(false);
        }

        private void addSample(double cpu, double ram, long timestamp) {
            samples.addLast(new double[]{Math.max(0, Math.min(100, cpu)), Math.max(0, Math.min(100, ram)), timestamp});
            while (samples.size() > 180) samples.removeFirst();
            repaint();
        }

        private void clear() {
            samples.clear();
            repaint();
        }

        @Override
        protected void paintComponent(Graphics graphics) {
            super.paintComponent(graphics);
            Graphics2D g = (Graphics2D) graphics.create();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
            g.setFont(new Font("Segoe UI", Font.PLAIN, 11));
            FontMetrics fm = g.getFontMetrics();

            int left = 46, right = getWidth() - 12, top = 12, bottom = getHeight() - 36;

            // Horizontal grid lines and percentage labels
            for (int i = 0; i <= 4; i++) {
                int y = top + (bottom - top) * i / 4;
                g.setColor(new Color(241, 245, 249));
                g.drawLine(left, y, right, y);
                g.setColor(AdminTheme.MUTED);
                String pct = Integer.toString(100 - i * 25) + "%";
                g.drawString(pct, left - fm.stringWidth(pct) - 6, y + fm.getAscent() / 2 - 1);
            }

            if (!samples.isEmpty()) {
                paintSmoothSeries(g, 1, AdminTheme.PURPLE, left, right, top, bottom);
                paintSmoothSeries(g, 0, AdminTheme.BLUE, left, right, top, bottom);
                java.time.format.DateTimeFormatter time = java.time.format.DateTimeFormatter.ofPattern("HH:mm:ss").withZone(ZoneId.systemDefault());
                String first = time.format(java.time.Instant.ofEpochMilli((long) samples.getFirst()[2]));
                String last = time.format(java.time.Instant.ofEpochMilli((long) samples.getLast()[2]));
                g.setColor(AdminTheme.MUTED);
                g.drawString(first, left, getHeight() - 8);
                g.drawString(last, right - fm.stringWidth(last), getHeight() - 8);
            } else {
                String empty = "Waiting for live CPU and RAM telemetry";
                g.setColor(AdminTheme.MUTED);
                g.drawString(empty, Math.max(left, (getWidth() - fm.stringWidth(empty)) / 2), (top + bottom) / 2);
            }
            g.dispose();
        }

        private void paintSmoothSeries(Graphics2D g, int valueIndex, Color color, int left, int right, int top, int bottom) {
            if (samples.size() < 2) return;
            g.setColor(color);
            g.setStroke(new BasicStroke(2.2f, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));

            int count = samples.size();
            double[] xs = new double[count];
            double[] ys = new double[count];
            int idx = 0;
            for (double[] sample : samples) {
                xs[idx] = left + (double) (right - left) * idx / (count - 1);
                ys[idx] = bottom - (bottom - top) * sample[valueIndex] / 100.0;
                idx++;
            }

            Path2D path = new Path2D.Double();
            path.moveTo(xs[0], ys[0]);
            for (int i = 0; i < count - 1; i++) {
                double x0 = i > 0 ? xs[i - 1] : xs[i];
                double y0 = i > 0 ? ys[i - 1] : ys[i];
                double x1 = xs[i];
                double y1 = ys[i];
                double x2 = xs[i + 1];
                double y2 = ys[i + 1];
                double x3 = (i + 2 < count) ? xs[i + 2] : x2;
                double y3 = (i + 2 < count) ? ys[i + 2] : y2;

                double cp1x = x1 + (x2 - x0) / 6.0;
                double cp1y = y1 + (y2 - y0) / 6.0;
                double cp2x = x2 - (x3 - x1) / 6.0;
                double cp2y = y2 - (y3 - y1) / 6.0;

                path.curveTo(cp1x, cp1y, cp2x, cp2y, x2, y2);
            }
            g.draw(path);
        }
    }

    private static final class PlaceholderTextField extends JTextField {
        private final String prompt;

        private PlaceholderTextField(String prompt) {
            this.prompt = prompt;
        }

        @Override
        protected void paintComponent(Graphics graphics) {
            super.paintComponent(graphics);
            if (getText().isEmpty() && !isFocusOwner()) {
                Graphics2D g = (Graphics2D) graphics.create();
                g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
                g.setColor(AdminTheme.MUTED);
                g.setFont(getFont());
                Insets insets = getInsets();
                g.drawString(prompt, insets.left + 2, getHeight() / 2 + g.getFontMetrics().getAscent() / 2 - 2);
                g.dispose();
            }
        }
    }

    private record NavItem(String label, DashboardIcon.Kind icon) { }
}
