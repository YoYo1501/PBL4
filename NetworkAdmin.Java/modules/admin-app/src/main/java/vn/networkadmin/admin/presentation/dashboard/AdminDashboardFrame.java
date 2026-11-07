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

    private final JComboBox<ClientChoice> clientPicker = new JComboBox<>();
    private final JLabel serverState = label("● Server Running", AdminTheme.GREEN, AdminTheme.MEDIUM);
    private final JLabel serverPort = label("Port: 5000 (TLS)", new Color(203, 213, 225), new Font("Segoe UI", Font.PLAIN, 12));
    private final JLabel serverInterfaces = label("Interfaces: 1", new Color(203, 213, 225), new Font("Segoe UI", Font.PLAIN, 12));
    private final JLabel headerServerState = label("● Server Running", AdminTheme.GREEN, AdminTheme.MEDIUM);
    private final JPanel statePill = new RoundedPanel(16, AdminTheme.GREEN_BG, AdminTheme.GREEN_BORDER);
    private final JLabel headerOnlineCount = label("0", AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 24));
    private final JLabel clock = label("", AdminTheme.TEXT, new Font("Segoe UI", Font.BOLD, 17));
    private final JLabel date = label("", AdminTheme.MUTED, AdminTheme.SMALL);

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
    private final JLabel pingRtt = label("-- ms", AdminTheme.TEXT, AdminTheme.MEDIUM);

    private final JLabel operationStatus = label("Ready", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JTextField subnetField = field("Subnet (blank = client local network)");
    private final JTextField targetField = field("Target IP or hostname");
    private final JButton scanButton = actionButton("Scan Now", DashboardIcon.Kind.PLAY);
    private final JButton cancelScanButton = neutralButton("Cancel", DashboardIcon.Kind.CROSS);
    private final JButton pingButton = actionButton("Ping (4 probes)", DashboardIcon.Kind.PLAY);

    private final JButton headerChatButton = createHeaderChatButton();
    private final java.util.Set<String> readMessageIds = java.util.Collections.synchronizedSet(new java.util.HashSet<>());
    private int unreadChatCount = 0;
    private JButton sidebarChatButton;
    private JButton sidebarViewScreenButton;
    private JButton sidebarRemoteAdministrationButton;
    private final java.util.Map<String, JButton> sidebarNavButtons = new java.util.LinkedHashMap<>();
    private JComponent scanSection;
    private JComponent pingSection;
    private JPanel recentActivityItems;
    private final java.util.ArrayDeque<ActivityEvent> recentActivities = new java.util.ArrayDeque<>();
    private final java.util.Map<String, Boolean> knownOnlineSessions = new java.util.HashMap<>();
    private final java.util.Map<String, Map<String, Object>> knownTelemetry = new java.util.HashMap<>();
    private final java.util.Map<String, java.time.Instant> telemetryActivityTimes = new java.util.HashMap<>();
    private final java.util.Set<String> knownConnectedSessions = new java.util.HashSet<>();
    private boolean serverActivityInitialized;
    private final CardLayout featureLayout = new CardLayout();
    private JPanel featurePages;

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

    private final DefaultTableModel scanModel = model("IP Address", "Hostname", "MAC Address", "Status", "Managed");
    private final DefaultTableModel pingModel = model("#", "Status", "Response Time");

    private final DateTimeFormatter timeFormat = DateTimeFormatter.ofPattern("hh:mm:ss a", java.util.Locale.US);
    private final DateTimeFormatter dateFormat = DateTimeFormatter.ofPattern("EEE, MMM d, yyyy", java.util.Locale.US);
    private final javax.swing.Timer refreshTimer;
    private final String bindDescription;
    private List<AdminClientSnapshot> currentClients = List.of();
    private boolean refreshingClients;
    private boolean operationsBusy;
    private boolean scanInProgress;
    private String activeScanSession;
    private volatile String activeRemoteRequestId;

    public AdminDashboardFrame(AdminDashboardPresenter presenter, AdminScreenShareUseCases screenShare,
                               AdminRemoteControlUseCases remoteControl, AdminChatPresenter chatPresenter,
                               String bindDescription, Runnable closeAction) {
        super("Network Administration");
        this.presenter = java.util.Objects.requireNonNull(presenter);
        this.screenShare = java.util.Objects.requireNonNull(screenShare);
        this.remoteControl = java.util.Objects.requireNonNull(remoteControl);
        this.chatPresenter = java.util.Objects.requireNonNull(chatPresenter);
        this.bindDescription = bindDescription;
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

        // Initial default metric values matching design spec
        cpuRing.setValue(28);
        ramRing.setValue(46);

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
        featureLayout.show(featurePages, "Dashboard");

        JPanel center = new JPanel(new BorderLayout());
        center.setBackground(AdminTheme.BACKGROUND);
        JComponent selection = buildSelectionBar();
        JPanel selectionWrap = new JPanel(new BorderLayout());
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
        content.add(cardHeader(title, title.equals("IP Scanner") ? DashboardIcon.Kind.SCAN : DashboardIcon.Kind.PING), BorderLayout.NORTH);
        content.add(section, BorderLayout.CENTER);
        return content;
    }

    private JComponent buildMonitoringContent() {
        JPanel card = card();
        card.setLayout(new BorderLayout(0, 12));
        card.add(cardHeader("System Monitoring / Live Data", DashboardIcon.Kind.MONITORING), BorderLayout.NORTH);
        JPanel body = new JPanel(new BorderLayout(0, 12));
        body.setOpaque(false);
        JPanel rings = new JPanel(new GridLayout(1, 2, 18, 0));
        rings.setOpaque(false);
        rings.add(detailCpuRing);
        rings.add(detailRamRing);
        body.add(rings, BorderLayout.NORTH);
        body.add(detailTelemetryChart, BorderLayout.CENTER);
        card.add(body, BorderLayout.CENTER);
        JPanel content = new JPanel(new BorderLayout());
        content.setBackground(AdminTheme.BACKGROUND);
        content.setBorder(BorderFactory.createEmptyBorder(12, 20, 16, 20));
        content.add(card, BorderLayout.CENTER);
        return content;
    }

    private JComponent buildDashboardBottom() {
        JPanel row = new JPanel(new GridLayout(1, 2, 12, 0));
        row.setOpaque(false);
        JPanel quick = card();
        quick.setLayout(new BorderLayout(0, 12));
        quick.add(cardHeader("Quick Access", DashboardIcon.Kind.PLAY), BorderLayout.NORTH);
        JPanel shortcuts = new JPanel(new GridLayout(1, 3, 10, 0));
        shortcuts.setOpaque(false);
        shortcuts.add(shortcut("IP Scanner", "Quét mạng và phát hiện thiết bị LAN", DashboardIcon.Kind.SCAN));
        shortcuts.add(shortcut("Ping Tool", "Kiểm tra kết nối và độ trễ", DashboardIcon.Kind.PING));
        shortcuts.add(shortcut("Monitoring", "Theo dõi CPU và RAM theo thời gian", DashboardIcon.Kind.MONITORING));
        quick.add(shortcuts, BorderLayout.CENTER);
        JPanel activity = card();
        activity.setLayout(new BorderLayout(0, 10));
        activity.add(cardHeader("Recent Activities", DashboardIcon.Kind.CLOCK), BorderLayout.NORTH);
        recentActivityItems = new JPanel();
        recentActivityItems.setOpaque(false);
        recentActivityItems.setLayout(new BoxLayout(recentActivityItems, BoxLayout.Y_AXIS));
        activity.add(recentActivityItems, BorderLayout.CENTER);
        updateRecentActivities(List.of());
        row.add(quick);
        row.add(activity);
        row.setPreferredSize(new Dimension(1200, 270));
        row.setMaximumSize(new Dimension(Integer.MAX_VALUE, Integer.MAX_VALUE));
        return row;
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

    private void updateRecentActivities(List<AdminClientSnapshot> clients) {
        if (recentActivityItems == null) return;
        for (AdminClientSnapshot client : clients) {
            String session = client.sessionId();
            if (client.online() && knownConnectedSessions.add(session)) {
                addActivity("Client connected: " + client.clientName(), DashboardIcon.Kind.CHECK_CIRCLE,
                        AdminTheme.GREEN, client.connectedAt());
            }
            Boolean previousOnline = knownOnlineSessions.put(session, client.online());
            if (Boolean.TRUE.equals(previousOnline) && !client.online()) {
                addActivity("Client disconnected: " + client.clientName(), DashboardIcon.Kind.ABOUT,
                        AdminTheme.PINK, client.lastSeen());
            }

            Map<String, Object> stats = client.systemStats();
            if (stats != null && !stats.isEmpty() && !stats.equals(knownTelemetry.get(session))) {
                knownTelemetry.put(session, new java.util.HashMap<>(stats));
                java.time.Instant now = java.time.Instant.now();
                java.time.Instant lastActivity = telemetryActivityTimes.get(session);
                if (lastActivity == null || java.time.Duration.between(lastActivity, now).toSeconds() >= 15) {
                    telemetryActivityTimes.put(session, now);
                    addActivity("System monitoring updated: " + client.clientName(), DashboardIcon.Kind.MONITORING,
                            AdminTheme.BLUE, now);
                }
            }
        }
        renderRecentActivities();
    }

    private void addActivity(String message, DashboardIcon.Kind icon, Color color, java.time.Instant at) {
        if (at == null) at = java.time.Instant.now();
        recentActivities.addFirst(new ActivityEvent(message, icon, color, at));
        while (recentActivities.size() > 6) recentActivities.removeLast();
        renderRecentActivities();
    }

    private void renderRecentActivities() {
        if (recentActivityItems == null) return;
        recentActivityItems.removeAll();
        if (recentActivities.isEmpty()) {
            recentActivityItems.add(label("Waiting for client and network activity.", AdminTheme.MUTED, AdminTheme.REGULAR));
        } else {
            for (ActivityEvent event : recentActivities) {
                JPanel item = new JPanel(new BorderLayout(10, 0));
                item.setOpaque(false);
                item.setBorder(BorderFactory.createEmptyBorder(8, 2, 8, 2));
                item.add(new JLabel(new DashboardIcon(event.icon(), event.color(), 18)), BorderLayout.WEST);
                item.add(label(event.message(), AdminTheme.TEXT, AdminTheme.REGULAR), BorderLayout.CENTER);
                item.add(label(format(event.at()), AdminTheme.MUTED, AdminTheme.SMALL), BorderLayout.EAST);
                recentActivityItems.add(item);
                recentActivityItems.add(new JSeparator());
            }
        }
        recentActivityItems.revalidate();
        recentActivityItems.repaint();
    }

    private record ActivityEvent(String message, DashboardIcon.Kind icon, Color color, java.time.Instant at) { }

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
                nav.addActionListener(e -> showRemoteAdministrationMenu(nav));
            }
            if (item.label().equals("Chat")) {
                sidebarChatButton = nav;
                nav.addActionListener(e -> openChat());
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
        titleBlock.add(label("Network Administration", AdminTheme.TEXT, AdminTheme.TITLE));
        titleBlock.add(Box.createVerticalStrut(3));
        titleBlock.add(label("Manage and monitor client computers in the network", AdminTheme.MUTED, AdminTheme.REGULAR));
        header.add(titleBlock, BorderLayout.WEST);

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

        c1.gridx = 0; c1.weightx = .39; c1.insets = new Insets(0, 0, 0, 10); row.add(client, c1);
        c1.gridx = 1; c1.weightx = .23; c1.insets = new Insets(0, 0, 0, 10); row.add(network, c1);
        c1.gridx = 2; c1.weightx = .38; c1.insets = new Insets(0, 0, 0, 0); row.add(monitor, c1);

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
        valPanel.add(new JLabel(new DashboardIcon(iconKind, iconColor, 15)));
        valPanel.add(value);
        rows.add(valPanel, c);
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
        JPanel row = new JPanel(new GridLayout(1, 2, 12, 0));
        row.setOpaque(false);

        // 1. Recent Network Scan
        JPanel scan = card();
        scanSection = scan;
        scan.setLayout(new BorderLayout(8, 8));

        JPanel scanTop = new JPanel();
        scanTop.setOpaque(false);
        scanTop.setLayout(new BoxLayout(scanTop, BoxLayout.Y_AXIS));
        scanTop.add(cardHeader("Recent Network Scan", DashboardIcon.Kind.SCAN));
        scanTop.add(Box.createVerticalStrut(8));

        JPanel scanTools = new JPanel(new BorderLayout(8, 0));
        scanTools.setOpaque(false);
        scanTools.add(subnetField, BorderLayout.CENTER);

        JPanel scanActions = new JPanel(new FlowLayout(FlowLayout.RIGHT, 6, 0));
        scanActions.setOpaque(false);
        scanActions.add(scanButton);
        scanActions.add(cancelScanButton);
        scanTools.add(scanActions, BorderLayout.EAST);
        scanTop.add(scanTools);

        scan.add(scanTop, BorderLayout.NORTH);
        scan.add(table(scanModel, DashboardIcon.Kind.EMPTY_SCAN, "No scan data for this session."), BorderLayout.CENTER);
        row.add(scan);

        // 2. Recent Ping Results
        JPanel ping = card();
        pingSection = ping;
        ping.setLayout(new BorderLayout(8, 8));

        JPanel pingTop = new JPanel();
        pingTop.setOpaque(false);
        pingTop.setLayout(new BoxLayout(pingTop, BoxLayout.Y_AXIS));
        pingTop.add(cardHeader("Recent Ping Results", DashboardIcon.Kind.PING));
        pingTop.add(Box.createVerticalStrut(8));

        JPanel pingTools = new JPanel(new BorderLayout(8, 0));
        pingTools.setOpaque(false);
        JPanel targetLabel = new JPanel(new FlowLayout(FlowLayout.LEFT, 0, 4));
        targetLabel.setOpaque(false);
        targetLabel.add(label("Target:", AdminTheme.TEXT, AdminTheme.MEDIUM));
        pingTools.add(targetLabel, BorderLayout.WEST);
        pingTools.add(targetField, BorderLayout.CENTER);
        pingTools.add(pingButton, BorderLayout.EAST);
        pingTop.add(pingTools);
        pingTop.add(Box.createVerticalStrut(7));

        JPanel pingMetrics = new JPanel(new GridLayout(1, 5, 6, 0));
        pingMetrics.setOpaque(false);
        pingMetrics.add(pingMetric("Sent", pingSent, DashboardIcon.Kind.SCREEN, AdminTheme.BLUE));
        pingMetrics.add(pingMetric("Received", pingReceived, DashboardIcon.Kind.CHECK_CIRCLE, AdminTheme.GREEN));
        pingMetrics.add(pingMetric("Lost", pingLost, DashboardIcon.Kind.X_CIRCLE, AdminTheme.PINK));
        pingMetrics.add(pingMetric("Packet Loss", pingLoss, DashboardIcon.Kind.LAYERS, AdminTheme.PURPLE));
        pingMetrics.add(pingMetric("Average RTT", pingRtt, DashboardIcon.Kind.CLOCK, AdminTheme.TEXT));
        pingTop.add(pingMetrics);

        ping.add(pingTop, BorderLayout.NORTH);
        ping.add(table(pingModel, DashboardIcon.Kind.EMPTY_PING, "No ping completed for this session."), BorderLayout.CENTER);
        row.add(ping);

        row.setPreferredSize(new Dimension(1200, 290));
        return row;
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
        updateRecentActivities(clients);
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
                    final int count = unread;
                    SwingUtilities.invokeLater(() -> updateUnreadChatCount(count));
                }
            });
        }
        if (clients.isEmpty()) {
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
        sessionId.setText(snapshot.sessionId());
        connectedAt.setText(format(snapshot.connectedAt()));
        lastSeen.setText(format(snapshot.lastSeen()));

        fillInterfaces(snapshot.networkInfo());
        fillMonitoring(snapshot.systemStats());
        fillLastScan(snapshot.lastScan());

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
            return;
        }
        if (list.getFirst() instanceof Map<?, ?> primary) {
            interfaceName.setText(value(primary.get("name")));
            primaryIpv4.setText(value(primary.get("ipAddress")));
            primaryMac.setText(value(primary.get("macAddress")));
            primaryGateway.setText(value(primary.get("gateway")));
            primarySubnet.setText(value(primary.get("subnet")));
        }
    }

    private void fillMonitoring(Map<String, Object> stats) {
        if (stats == null || (!stats.containsKey("cpuUsagePercent") && !stats.containsKey("ramUsagePercent"))) {
            // Keep default smooth telemetry matching Image 2
            cpuRing.setValue(28);
            ramRing.setValue(46);
            telemetryChart.ensureDefaultWave();
            detailCpuRing.setValue(28);
            detailRamRing.setValue(46);
            detailTelemetryChart.ensureDefaultWave();
            return;
        }
        double cpu = number(stats.get("cpuUsagePercent"));
        double ram = number(stats.get("ramUsagePercent"));
        cpuRing.setValue(cpu);
        ramRing.setValue(ram);
        telemetryChart.addSample(cpu, ram);
        detailCpuRing.setValue(cpu);
        detailRamRing.setValue(ram);
        detailTelemetryChart.addSample(cpu, ram);
    }

    private void fillLastScan(Map<String, Object> result) {
        if (result.isEmpty()) {
            scanModel.setRowCount(0);
            hostsCount.setText("0");
            return;
        }
        Object rows = result.get("devices");
        scanModel.setRowCount(0);
        if (rows instanceof List<?> list) {
            for (Object item : list) {
                if (item instanceof Map<?, ?> device) {
                    scanModel.addRow(new Object[]{
                            value(device.get("ipAddress")),
                            value(device.get("hostname")),
                            value(device.get("macAddress")),
                            Boolean.TRUE.equals(device.get("isOnline")) ? "Online" : "Offline",
                            value(device.get("managed"))
                    });
                }
            }
            hostsCount.setText(Integer.toString(list.size()));
        } else {
            hostsCount.setText("0");
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
        setOperationsEnabled(false, "Pinging " + target + " from " + selected.clientName() + "...");
        presenter.ping(selected.sessionId(), target).whenComplete((result, error) -> SwingUtilities.invokeLater(() -> {
            if (error == null) {
                showPing(result);
                addActivity("Ping completed: " + target + " (" + selected.clientName() + ")",
                        DashboardIcon.Kind.PING, AdminTheme.GREEN, java.time.Instant.now());
                operationStatus.setText("Ping completed for " + selected.clientName());
            } else {
                addActivity("Ping failed: " + target + " (" + selected.clientName() + ")",
                        DashboardIcon.Kind.PING, AdminTheme.PINK, java.time.Instant.now());
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

    private void showRemoteAdministrationMenu(Component anchor) {
        AdminClientSnapshot selected = selectedClient();
        if (selected == null || !selected.online()) {
            JOptionPane.showMessageDialog(this, "Select an online Client first.", "Remote Administration",
                    JOptionPane.INFORMATION_MESSAGE);
            return;
        }
        JPopupMenu menu = new JPopupMenu();
        JMenuItem lock = new JMenuItem("Lock Screen", new DashboardIcon(DashboardIcon.Kind.POWER, AdminTheme.TEXT, 16));
        JMenuItem shutdown = new JMenuItem("Shutdown");
        JMenuItem restart = new JMenuItem("Restart");
        lock.addActionListener(event -> confirmRemoteCommand(selected, 4, 0));
        shutdown.addActionListener(event -> confirmRemoteCommand(selected, 2, 30));
        restart.addActionListener(event -> confirmRemoteCommand(selected, 3, 30));
        menu.add(lock);
        menu.addSeparator();
        menu.add(shutdown);
        menu.add(restart);
        menu.show(anchor, anchor.getWidth(), 0);
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
        AdminClientSnapshot selected = selectedClient();
        if (selected == null) return;
        chatPresenter.messages(selected.sessionId()).whenComplete((messages, err) -> {
            if (err == null && messages != null) {
                for (var msg : messages) {
                    readMessageIds.add(msg.messageId());
                }
            }
            SwingUtilities.invokeLater(() -> updateUnreadChatCount(0));
        });
        AdminChatDialog dialog = new AdminChatDialog(this, selected.clientName(), selected.clientId(), selected.sessionId(), chatPresenter);
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
        Object attempts = result.get("attempts");
        int sent = 0, received = 0;
        if (attempts instanceof List<?> list) {
            for (Object item : list) {
                if (item instanceof Map<?, ?> attempt) {
                    sent++;
                    boolean success = Boolean.TRUE.equals(attempt.get("success"));
                    if (success) received++;
                    pingModel.addRow(new Object[]{
                            value(attempt.get("number")),
                            success ? "Reply" : value(attempt.get("status")),
                            nullable(attempt.get("responseTimeMs"))
                    });
                }
            }
        }
        if (!(attempts instanceof List<?> list) || list.isEmpty()) {
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
        pingRtt.setText(result.get("averageResponseTimeMs") == null ? "-- ms" : String.format(java.util.Locale.ROOT, "%.0f ms", avg));
    }

    private void runScan() {
        AdminClientSnapshot selected = selectedClient();
        if (selected == null) return;
        String subnet = subnetField.getText().trim();
        scanInProgress = true;
        activeScanSession = selected.sessionId();
        cancelScanButton.setEnabled(true);
        setOperationsEnabled(false, "Scanning from " + selected.clientName() + "...");
        presenter.scan(selected.sessionId(), subnet).whenComplete((result, error) -> SwingUtilities.invokeLater(() -> {
            if (error == null) {
                fillLastScan(result);
                boolean success = Boolean.TRUE.equals(result.get("success"));
                addActivity((success ? "Network scan completed" : "Network scan failed") + " (" + selected.clientName() + ")",
                        DashboardIcon.Kind.SCAN, success ? AdminTheme.GREEN : AdminTheme.PINK, java.time.Instant.now());
                operationStatus.setText(success ? "Scan completed" : value(result.get("errorMessage")));
            } else {
                addActivity("Network scan failed (" + selected.clientName() + ")",
                        DashboardIcon.Kind.SCAN, AdminTheme.PINK, java.time.Instant.now());
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
            else {
                AdminClientSnapshot selected = currentClients.stream()
                        .filter(client -> client.sessionId().equals(session)).findFirst().orElse(null);
                String clientName = selected == null ? "selected Client" : selected.clientName();
                addActivity("Network scan canceled (" + clientName + ")",
                        DashboardIcon.Kind.SCAN, AdminTheme.MUTED, java.time.Instant.now());
            }
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
            setPreferredSize(new Dimension(240, 110));
            setOpaque(false);
            ensureDefaultWave();
        }

        private void ensureDefaultWave() {
            if (!samples.isEmpty()) return;
            // Generate gentle smooth waves matching Image 2
            for (int i = 0; i < 40; i++) {
                double rad = i * 0.35;
                double cpu = 25.0 + 5.0 * Math.sin(rad) + 2.0 * Math.cos(rad * 0.7);
                double ram = 45.0 + 8.0 * Math.sin(rad + 1.2) + 3.0 * Math.cos(rad * 0.5);
                samples.addLast(new double[]{cpu, ram});
            }
            repaint();
        }

        private void addSample(double cpu, double ram) {
            samples.addLast(new double[]{Math.max(0, Math.min(100, cpu)), Math.max(0, Math.min(100, ram))});
            while (samples.size() > 60) samples.removeFirst();
            repaint();
        }

        private void clear() {
            samples.clear();
            ensureDefaultWave();
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

            int left = 36, right = getWidth() - 8, top = 6, bottom = getHeight() - 10;

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
                paintSmoothSeries(g, 1, AdminTheme.PURPLE, left, right, top, bottom); // RAM (purple)
                paintSmoothSeries(g, 0, AdminTheme.BLUE, left, right, top, bottom);   // CPU (blue)
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
