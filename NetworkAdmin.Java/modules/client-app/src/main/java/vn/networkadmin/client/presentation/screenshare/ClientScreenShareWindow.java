package vn.networkadmin.client.presentation.screenshare;

import vn.networkadmin.client.application.chat.ClientChatUseCases;
import vn.networkadmin.client.application.remoteadministration.RemoteAdministrationClientView;
import vn.networkadmin.client.application.remotecontrol.RemoteControlClientView;
import vn.networkadmin.client.application.screenshare.ScreenShareClientView;
import vn.networkadmin.client.infrastructure.monitoring.ClientTelemetrySampler;

import javax.swing.*;
import javax.swing.border.EmptyBorder;
import java.awt.*;
import java.awt.event.MouseAdapter;
import java.awt.event.MouseEvent;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.Locale;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;

/** Main Swing presentation for the Client. Services remain outside this view. */
public final class ClientScreenShareWindow implements ScreenShareClientView, RemoteControlClientView, RemoteAdministrationClientView, AutoCloseable {
    private static final Color PRIMARY = new Color(8, 120, 249);
    private static final Color PRIMARY_DARK = new Color(0, 86, 179);
    private static final Color SIDEBAR = new Color(6, 43, 77);
    private static final Color BACKGROUND = new Color(244, 247, 252);
    private static final Color CARD = Color.WHITE;
    private static final Color TEXT = new Color(20, 33, 61);
    private static final Color SECONDARY = new Color(100, 116, 139);
    private static final Color SUCCESS = new Color(22, 185, 87);
    private static final Color WARNING = new Color(245, 158, 11);
    private static final Color DANGER = new Color(239, 68, 68);
    private static final Color BORDER = new Color(229, 234, 242);
    private static final DateTimeFormatter TIME = DateTimeFormatter.ofPattern("HH:mm").withZone(ZoneId.systemDefault());
    private static final DateTimeFormatter DATE = DateTimeFormatter.ofPattern("EEE, MMM d, yyyy", Locale.ENGLISH).withZone(ZoneId.systemDefault());

    private final JFrame frame;
    private final String serverEndpoint;
    private final JLabel connectionLabel = label("Disconnected from Admin", 13, SECONDARY, false);
    private final JLabel sidebarConnection = label("Disconnected from Admin", 13, Color.WHITE, true);
    private final JLabel sidebarSession = label("Session: —", 12, Color.WHITE, false);
    private final JLabel pageTitle = label("Network Administration – Client", 30, TEXT, true);
    private final JLabel pageSubtitle = label("Trạng thái máy trạm và kết nối đến hệ thống quản trị", 15, SECONDARY, false);
    private final JLabel topSession = label("—", 15, TEXT, true);
    private final JLabel topClock = label("--:--", 20, TEXT, true);
    private final JLabel topDate = label("", 12, SECONDARY, false);
    private final JLabel sessionValue = label("—", 18, TEXT, true);
    private final JLabel sessionDetail = label("Chưa có phiên kết nối", 12, SECONDARY, false);
    private final JLabel connectionPill = label("Disconnected", 13, DANGER, true);
    private final JLabel clientState = label("Offline", 18, DANGER, true);
    private final JLabel clientDetail = label("Đang chờ kết nối đến Admin", 12, SECONDARY, false);
    private final JLabel sharingState = label("Không hoạt động", 18, SECONDARY, true);
    private final JLabel sharingDetail = label("Chưa chia sẻ màn hình", 12, SECONDARY, false);
    private final JLabel controlState = label("Chưa cho phép", 18, SECONDARY, true);
    private final JLabel controlDetail = label("Chưa có yêu cầu điều khiển", 12, SECONDARY, false);
    private final JLabel remoteAdministrationState = label("No pending operation", 18, SECONDARY, true);
    private final JLabel remoteAdministration = label("No remote administration operation pending", 12, SECONDARY, false);
    private final JLabel downloadValue = label("0.00 Mbps", 13, PRIMARY, true);
    private final JLabel uploadValue = label("0.00 Mbps", 13, SUCCESS, true);
    private final JLabel machineName = label("Đang thu thập…", 14, TEXT, true);
    private final JLabel osName = label("Đang thu thập…", 14, TEXT, true);
    private final JLabel cpuModel = label("Đang thu thập…", 14, TEXT, true);
    private final JLabel memory = label("Đang thu thập…", 14, TEXT, true);
    private final JLabel resolution = label("Đang thu thập…", 14, TEXT, true);
    private final JLabel ipAddress = label("Đang thu thập…", 14, TEXT, true);
    private final JLabel macAddress = label("Đang thu thập…", 14, TEXT, true);
    private final JButton stop = actionButton("Dừng chia sẻ", SUCCESS);
    private final JButton revoke = actionButton("Thu hồi quyền", DANGER);
    private final HintTextField chatInput = new HintTextField("Nhập tin nhắn…");
    private final HintTextField chatPageInput = new HintTextField("Nhập tin nhắn…");
    private final JButton cancelRemoteAdministration = actionButton("Cancel operation", DANGER);
    private final ChatTranscript chatTranscript = new ChatTranscript();
    private final ChatTranscript chatPageTranscript = new ChatTranscript();
    private final LineChartPanel networkChart = new LineChartPanel(PRIMARY, SUCCESS, false);
    private final LineChartPanel performanceChart = new LineChartPanel(PRIMARY, new Color(117, 23, 234), true);
    private final MetricRing cpuRing = new MetricRing(PRIMARY);
    private final MetricRing ramRing = new MetricRing(new Color(117, 23, 234));
    private final MetricRing diskRing = new MetricRing(PRIMARY);
    private final ScheduledExecutorService telemetryWorkers;
    private final ExecutorService chatWorkers;
    private final ClientTelemetrySampler telemetry = new ClientTelemetrySampler();
    private final CardLayout contentLayout = new CardLayout();
    private final JPanel content = new JPanel(contentLayout);
    private volatile Runnable stopAction = () -> { };
    private volatile Runnable revokeAction = () -> { };
    private volatile Consumer<String> chatSender = ignored -> { };
    private volatile boolean activelySharing;
    private volatile boolean activelyControlled;
    private volatile Runnable cancelRemoteAction = () -> { };
    private javax.swing.Timer remoteCountdown;
    private String clientDisplayName = "NetworkAdmin Client";

    public ClientScreenShareWindow() { this("configured endpoint"); }
    public ClientScreenShareWindow(String host, int port) { this((host == null || host.isBlank() ? "configured endpoint" : host) + ":" + port); }

    private ClientScreenShareWindow(String endpoint) {
        serverEndpoint = endpoint;
        if (GraphicsEnvironment.isHeadless()) {
            frame = null; telemetryWorkers = null; chatWorkers = null; return;
        }
        frame = new JFrame("Network Administration – Client");
        frame.setDefaultCloseOperation(WindowConstants.DO_NOTHING_ON_CLOSE);
        frame.addWindowListener(new java.awt.event.WindowAdapter() {
            @Override public void windowClosing(java.awt.event.WindowEvent event) {
                if (activelySharing || activelyControlled) { frame.setVisible(true); frame.toFront(); }
                else close();
            }
        });
        frame.setMinimumSize(new Dimension(1080, 700));
        frame.setSize(1680, 942);
        frame.setLocationRelativeTo(null);
        frame.setContentPane(buildShell());
        stop.setEnabled(false); revoke.setEnabled(false);
        stop.addActionListener(event -> stopAction.run());
        revoke.addActionListener(event -> revokeAction.run());
        cancelRemoteAdministration.setEnabled(false);
        cancelRemoteAdministration.addActionListener(event -> {
            Runnable action = cancelRemoteAction;
            cancelRemoteAction = () -> { };
            cancelRemoteAdministration.setEnabled(false);
            action.run();
        });
        chatInput.addActionListener(event -> sendChat(chatInput));
        chatPageInput.addActionListener(event -> sendChat(chatPageInput));
        frame.setVisible(true);
        telemetryWorkers = Executors.newSingleThreadScheduledExecutor(task -> daemon(task, "client-dashboard-telemetry"));
        chatWorkers = Executors.newSingleThreadExecutor(task -> daemon(task, "client-chat-sender"));
        telemetryWorkers.scheduleWithFixedDelay(this::sampleTelemetry, 0, 2, TimeUnit.SECONDS);
    }

    private static Thread daemon(Runnable task, String name) { Thread thread = new Thread(task, name); thread.setDaemon(true); return thread; }

    private JPanel buildShell() {
        JPanel root = new JPanel(new BorderLayout()); root.setBackground(BACKGROUND); root.add(buildSidebar(), BorderLayout.WEST);
        JPanel workspace = new JPanel(new BorderLayout()); workspace.setBackground(BACKGROUND);
        content.add(buildDashboard(), "dashboard");
        content.add(chatPage(), "chat");
        content.add(simplePage("Network Info", "Thông tin mạng được đọc trực tiếp từ các interface IPv4 đang hoạt động."), "network");
        content.add(simplePage("Logs", "Nhật ký client sẽ hiển thị ở đây khi logging service được bật."), "logs");
        content.add(simplePage("Settings", "Cấu hình kết nối được đọc từ biến môi trường và tham số khởi động."), "settings");
        content.add(simplePage("About", "NetworkAdmin Client · Java 21 · Swing"), "about");
        JScrollPane scroll = new JScrollPane(content); scroll.setBorder(null); scroll.setHorizontalScrollBarPolicy(ScrollPaneConstants.HORIZONTAL_SCROLLBAR_NEVER); scroll.getViewport().setBackground(BACKGROUND); scroll.getVerticalScrollBar().setUnitIncrement(18);
        workspace.add(buildHeader(), BorderLayout.NORTH); workspace.add(scroll, BorderLayout.CENTER); root.add(workspace, BorderLayout.CENTER); return root;
    }

    private JPanel buildSidebar() {
        JPanel sidebar = new JPanel(new BorderLayout()); sidebar.setBackground(SIDEBAR); sidebar.setPreferredSize(new Dimension(242, 0));
        JPanel top = new JPanel(); top.setOpaque(false); top.setBorder(new EmptyBorder(28, 24, 18, 18)); top.setLayout(new BoxLayout(top, BoxLayout.Y_AXIS));
        JLabel logo = iconLabel(VectorIcon.Type.LOGO, 52, new Color(33, 145, 255)); logo.setAlignmentX(Component.LEFT_ALIGNMENT); top.add(logo); top.add(Box.createVerticalStrut(5)); top.add(label("NetworkAdmin", 18, Color.WHITE, true)); top.add(label("Client", 18, Color.WHITE, true));
        JLabel version = label("v2.0", 12, Color.WHITE, true); version.setOpaque(true); version.setBackground(PRIMARY); version.setBorder(new EmptyBorder(5, 11, 5, 11));
        JPanel versionWrap = new JPanel(new FlowLayout(FlowLayout.LEFT, 0, 0)); versionWrap.setOpaque(false); versionWrap.setBorder(new EmptyBorder(10, 0, 24, 0)); versionWrap.add(version); top.add(versionWrap); sidebar.add(top, BorderLayout.NORTH);
        JPanel nav = new JPanel(); nav.setOpaque(false); nav.setBorder(new EmptyBorder(0, 16, 0, 16)); nav.setLayout(new BoxLayout(nav, BoxLayout.Y_AXIS));
        addNavButton(nav, VectorIcon.Type.DASHBOARD, "Dashboard", "dashboard", true); addNavButton(nav, VectorIcon.Type.CHAT, "Chat", "chat", false); addNavButton(nav, VectorIcon.Type.NETWORK, "Network Info", "network", false); addNavButton(nav, VectorIcon.Type.LOGS, "Logs", "logs", false); addNavButton(nav, VectorIcon.Type.SETTINGS, "Settings", "settings", false); addNavButton(nav, VectorIcon.Type.ABOUT, "About", "about", false); sidebar.add(nav, BorderLayout.CENTER);
        RoundedPanel status = new RoundedPanel(new Color(18, 67, 108)); status.setLayout(new GridBagLayout()); status.setBorder(new EmptyBorder(14, 14, 14, 14));
        sidebarConnection.setIcon(new VectorIcon(VectorIcon.Type.DOT, 11, SUCCESS)); sidebarSession.setIcon(new VectorIcon(VectorIcon.Type.SESSION, 16, Color.WHITE));
        GridBagConstraints c = new GridBagConstraints(); c.gridx = 0; c.gridy = 0; c.anchor = GridBagConstraints.WEST; c.insets = new Insets(0, 0, 8, 0); status.add(sidebarConnection, c); c.gridy++; status.add(label("Server:  " + serverEndpoint + " (TLS)", 12, Color.WHITE, false), c); c.gridy++; c.insets = new Insets(7, 0, 0, 0); status.add(sidebarSession, c);
        JPanel statusWrap = new JPanel(new BorderLayout()); statusWrap.setOpaque(false); statusWrap.setBorder(new EmptyBorder(0, 16, 24, 16)); statusWrap.add(status, BorderLayout.CENTER); sidebar.add(statusWrap, BorderLayout.SOUTH); return sidebar;
    }

    private void addNavButton(JPanel parent, VectorIcon.Type icon, String text, String page, boolean active) {
        JButton button = new JButton(text, new VectorIcon(icon, 23, Color.WHITE)); button.setHorizontalAlignment(SwingConstants.LEFT); button.setIconTextGap(16); button.setFont(new Font("Segoe UI", Font.PLAIN, 15)); button.setForeground(Color.WHITE); button.setBorder(new EmptyBorder(12, 15, 12, 10)); button.setFocusPainted(false); button.setContentAreaFilled(true); button.setOpaque(true); button.putClientProperty("navActive", active); button.setBackground(active ? PRIMARY : SIDEBAR);
        button.addActionListener(event -> { contentLayout.show(content, page); pageTitle.setText(page.equals("dashboard") ? "Network Administration – Client" : text); pageSubtitle.setText(page.equals("dashboard") ? "Trạng thái máy trạm và kết nối đến hệ thống quản trị" : "NetworkAdmin Client"); for (Component child : parent.getComponents()) if (child instanceof JButton navButton) { boolean selected = navButton == button; navButton.putClientProperty("navActive", selected); navButton.setBackground(selected ? PRIMARY : SIDEBAR); } });
        button.addMouseListener(new MouseAdapter() { @Override public void mouseEntered(MouseEvent event) { if (!Boolean.TRUE.equals(button.getClientProperty("navActive"))) button.setBackground(new Color(14, 73, 116)); } @Override public void mouseExited(MouseEvent event) { if (!Boolean.TRUE.equals(button.getClientProperty("navActive"))) button.setBackground(SIDEBAR); } });
        button.setAlignmentX(Component.LEFT_ALIGNMENT); parent.add(button); parent.add(Box.createVerticalStrut(4));
    }

    private JPanel buildHeader() {
        JPanel header = new JPanel(new BorderLayout(18, 0)); header.setBackground(BACKGROUND); header.setBorder(new EmptyBorder(24, 27, 14, 29));
        JPanel titles = new JPanel(); titles.setOpaque(false); titles.setLayout(new BoxLayout(titles, BoxLayout.Y_AXIS)); titles.add(pageTitle); titles.add(Box.createVerticalStrut(4)); titles.add(pageSubtitle); header.add(titles, BorderLayout.WEST);
        JPanel meta = new JPanel(new GridLayout(1, 3, 20, 0)); meta.setOpaque(false); meta.add(metaBlock(VectorIcon.Type.CONNECTION, connectionPill, connectionLabel)); meta.add(metaBlock(VectorIcon.Type.SESSION, topSession, label("Session", 11, SECONDARY, false))); JPanel clock = new JPanel(); clock.setOpaque(false); clock.setLayout(new BoxLayout(clock, BoxLayout.Y_AXIS)); clock.add(new JLabel(new VectorIcon(VectorIcon.Type.CLOCK, 22, PRIMARY))); clock.add(topClock); clock.add(topDate); meta.add(clock); header.add(meta, BorderLayout.EAST); return header;
    }

    private JPanel metaBlock(VectorIcon.Type icon, JLabel value, JLabel caption) { JPanel panel = new JPanel(); panel.setOpaque(false); panel.setLayout(new BoxLayout(panel, BoxLayout.Y_AXIS)); panel.add(iconLabel(icon, 22, PRIMARY)); panel.add(value); panel.add(caption); return panel; }

    private JPanel buildDashboard() {
        JPanel dashboard = new JPanel(); dashboard.setBackground(BACKGROUND); dashboard.setBorder(new EmptyBorder(0, 27, 30, 29)); dashboard.setLayout(new BoxLayout(dashboard, BoxLayout.Y_AXIS));
        ResponsiveGrid statusCards = new ResponsiveGrid(4, 205, 105, 12); statusCards.add(clientCard()); statusCards.add(shareCard()); statusCards.add(controlCard()); statusCards.add(sessionCard()); dashboard.add(statusCards); dashboard.add(Box.createVerticalStrut(14));
        ResponsiveGrid remoteAdministrationGrid = new ResponsiveGrid(1, 760, 92, 12); remoteAdministrationGrid.add(remoteAdministrationCard()); dashboard.add(remoteAdministrationGrid); dashboard.add(Box.createVerticalStrut(14));
        ResponsiveGrid charts = new ResponsiveGrid(2, 380, 340, 14); charts.add(networkCard()); charts.add(performanceCard()); dashboard.add(charts); dashboard.add(Box.createVerticalStrut(14));
        ResponsiveGrid details = new ResponsiveGrid(2, 380, 340, 14); details.add(stationCard()); details.add(chatCard()); dashboard.add(details); return dashboard;
    }

    private JPanel clientCard() { RoundedPanel card = card(new Color(239, 246, 255)); card.setLayout(new BorderLayout(11, 0)); card.add(iconLabel(VectorIcon.Type.CLIENT, 31, PRIMARY), BorderLayout.WEST); card.add(vertical("Trạng thái client", clientState, clientDetail), BorderLayout.CENTER); return card; }
    private JPanel shareCard() { RoundedPanel card = card(new Color(239, 252, 244)); card.setLayout(new BorderLayout(11, 0)); card.add(iconLabel(VectorIcon.Type.SCREEN, 31, SUCCESS), BorderLayout.WEST); card.add(vertical("Chia sẻ màn hình", sharingState, sharingDetail), BorderLayout.CENTER); card.add(stop, BorderLayout.EAST); return card; }
    private JPanel controlCard() { RoundedPanel card = card(new Color(249, 242, 255)); card.setLayout(new BorderLayout(11, 0)); card.add(iconLabel(VectorIcon.Type.CONTROL, 31, new Color(117, 23, 234)), BorderLayout.WEST); card.add(vertical("Điều khiển từ xa", controlState, controlDetail), BorderLayout.CENTER); card.add(revoke, BorderLayout.EAST); return card; }
    private JPanel sessionCard() { RoundedPanel card = card(new Color(255, 244, 248)); card.setLayout(new BorderLayout(11, 0)); card.add(iconLabel(VectorIcon.Type.CALENDAR, 31, DANGER), BorderLayout.WEST); card.add(vertical("Phiên hiện tại", sessionValue, sessionDetail), BorderLayout.CENTER); return card; }

    private JPanel remoteAdministrationCard() {
        RoundedPanel card = card(new Color(255, 248, 236));
        card.setLayout(new BorderLayout(11, 0));
        card.add(iconLabel(VectorIcon.Type.CLOCK, 31, WARNING), BorderLayout.WEST);
        card.add(vertical("Remote Administration", remoteAdministrationState, remoteAdministration), BorderLayout.CENTER);
        card.add(cancelRemoteAdministration, BorderLayout.EAST);
        return card;
    }

    private JPanel networkCard() {
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 8)); card.add(sectionHeading(VectorIcon.Type.TRAFFIC, "Lưu lượng mạng", PRIMARY), BorderLayout.NORTH);
        JPanel legend = new JPanel(new FlowLayout(FlowLayout.CENTER, 30, 0)); legend.setOpaque(false); legend.add(coloredLegend("Tải xuống: ", downloadValue, PRIMARY)); legend.add(coloredLegend("Tải lên: ", uploadValue, SUCCESS)); card.add(legend, BorderLayout.CENTER); networkChart.setBorder(new EmptyBorder(0, 8, 8, 8)); card.add(networkChart, BorderLayout.SOUTH); return card;
    }

    private JPanel performanceCard() {
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 4)); card.add(sectionHeading(VectorIcon.Type.PERFORMANCE, "Hiệu suất hệ thống", PRIMARY), BorderLayout.NORTH);
        JPanel rings = new JPanel(new GridLayout(1, 3, 12, 0)); rings.setOpaque(false); rings.add(ringColumn(cpuRing, VectorIcon.Type.CPU, "CPU Usage")); rings.add(ringColumn(ramRing, VectorIcon.Type.RAM, "RAM Usage")); rings.add(ringColumn(diskRing, VectorIcon.Type.RESOLUTION, "Disk Usage")); card.add(rings, BorderLayout.CENTER); performanceChart.setPreferredSize(new Dimension(0, 108)); card.add(performanceChart, BorderLayout.SOUTH); return card;
    }
    @Override public void showCountdown(String command, int seconds, Runnable cancel) {
        cancelRemoteAction = Objects.requireNonNull(cancel);
        onEdt(() -> {
            if (remoteCountdown != null) remoteCountdown.stop();
            final int[] remaining = {Math.max(1, seconds)};
            remoteAdministrationState.setText("Pending operation");
            remoteAdministrationState.setForeground(WARNING);
            remoteAdministration.setText(command + " scheduled in " + remaining[0] + " seconds. Cancel if needed.");
            cancelRemoteAdministration.setEnabled(true);
            frame.setVisible(true); frame.setExtendedState(Frame.NORMAL); frame.toFront();
            remoteCountdown = new javax.swing.Timer(1000, event -> {
                remaining[0]--;
                if (remaining[0] <= 0) {
                    remoteCountdown.stop();
                    remoteCountdown = null;
                    remoteAdministrationState.setText("Executing");
                    remoteAdministrationState.setForeground(SUCCESS);
                    remoteAdministration.setText(command + " is being executed.");
                    cancelRemoteAdministration.setEnabled(false);
                    cancelRemoteAction = () -> { };
                } else remoteAdministration.setText(command + " scheduled in " + remaining[0] + " seconds. Cancel if needed.");
            });
            remoteCountdown.start();
        });
    }
    @Override public void clearCountdown(String status) {
        onEdt(() -> {
            if (remoteCountdown != null) { remoteCountdown.stop(); remoteCountdown = null; }
            remoteAdministrationState.setText("No pending operation");
            remoteAdministrationState.setForeground(SECONDARY);
            remoteAdministration.setText(status == null || status.isBlank() ? "No remote administration operation pending" : status);
            cancelRemoteAdministration.setEnabled(false);
            cancelRemoteAction = () -> { };
        });
    }

    private JPanel stationCard() {
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 7)); card.add(sectionHeading(VectorIcon.Type.WORKSTATION, "Thông tin máy trạm", PRIMARY), BorderLayout.NORTH);
        JPanel rows = new JPanel(); rows.setOpaque(false); rows.setLayout(new BoxLayout(rows, BoxLayout.Y_AXIS));
        addInfoRow(rows, VectorIcon.Type.COMPUTER, "Tên máy", machineName, false);
        addInfoRow(rows, VectorIcon.Type.OS, "Hệ điều hành", osName, true);
        addInfoRow(rows, VectorIcon.Type.CPU, "CPU", cpuModel, true);
        addInfoRow(rows, VectorIcon.Type.RAM, "RAM", memory, true);
        addInfoRow(rows, VectorIcon.Type.RESOLUTION, "Độ phân giải", resolution, true);
        addInfoRow(rows, VectorIcon.Type.WIFI, "Địa chỉ IP", ipAddress, true);
        addInfoRow(rows, VectorIcon.Type.MAC, "MAC Address", macAddress, true);
        cpuModel.setToolTipText("CPU model"); card.add(rows, BorderLayout.CENTER); return card;
    }

    private JPanel chatCard() {
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 8)); card.add(sectionHeading(VectorIcon.Type.CHAT, "Chat với Admin", PRIMARY), BorderLayout.NORTH); JScrollPane scroll = new JScrollPane(chatTranscript); scroll.setBorder(null); scroll.getViewport().setBackground(CARD); card.add(scroll, BorderLayout.CENTER);
        JPanel input = chatInput(chatInput, false); card.add(input, BorderLayout.SOUTH); return card;
    }

    private JPanel chatPage() {
        JPanel page = new JPanel(new BorderLayout()); page.setBackground(BACKGROUND); page.setBorder(new EmptyBorder(25, 27, 30, 29)); RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 10)); card.add(sectionHeading(VectorIcon.Type.CHAT, "Chat với Admin", PRIMARY), BorderLayout.NORTH); JScrollPane scroll = new JScrollPane(chatPageTranscript); scroll.setBorder(null); scroll.getViewport().setBackground(CARD); card.add(scroll, BorderLayout.CENTER); card.add(chatInput(chatPageInput, true), BorderLayout.SOUTH); page.add(card, BorderLayout.CENTER); return page;
    }

    private JPanel chatInput(JTextField source, boolean wideButton) {
        JPanel row = new JPanel(new BorderLayout(8, 0)); row.setOpaque(false); source.setFont(new Font("Segoe UI", Font.PLAIN, 14)); source.setBorder(BorderFactory.createCompoundBorder(BorderFactory.createLineBorder(BORDER), new EmptyBorder(9, 12, 9, 12))); JButton send = sendButton(wideButton ? "Gửi" : "", wideButton ? 82 : 52); send.addActionListener(event -> sendChat(source)); row.add(source, BorderLayout.CENTER); row.add(send, BorderLayout.EAST); return row;
    }

    private JPanel simplePage(String title, String description) { JPanel page = new JPanel(new BorderLayout()); page.setBackground(BACKGROUND); page.setBorder(new EmptyBorder(25, 27, 30, 29)); RoundedPanel card = card(CARD); card.setLayout(new BoxLayout(card, BoxLayout.Y_AXIS)); card.add(label(title, 24, TEXT, true)); card.add(Box.createVerticalStrut(12)); card.add(label(description, 15, SECONDARY, false)); page.add(card, BorderLayout.NORTH); return page; }

    private void addInfoRow(JPanel panel, VectorIcon.Type icon, String name, JLabel value, boolean separatorBefore) {
        if (separatorBefore) {
            JSeparator separator = new JSeparator(); separator.setForeground(BORDER); separator.setMaximumSize(new Dimension(Integer.MAX_VALUE, 1)); panel.add(separator);
        }
        JPanel row = new JPanel(new BorderLayout(10, 0)); row.setOpaque(false); row.setBorder(new EmptyBorder(5, 3, 5, 3));
        row.add(iconLabel(icon, 18, PRIMARY), BorderLayout.WEST); row.add(label(name, 14, TEXT, false), BorderLayout.CENTER);
        value.setHorizontalAlignment(SwingConstants.RIGHT); row.add(value, BorderLayout.EAST); row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 36)); panel.add(row);
    }

    private JPanel ringColumn(MetricRing ring, VectorIcon.Type icon, String title) { JPanel panel = new JPanel(new BorderLayout(0, 3)); panel.setOpaque(false); panel.add(ring, BorderLayout.CENTER); JPanel caption = new JPanel(new FlowLayout(FlowLayout.CENTER, 4, 0)); caption.setOpaque(false); caption.add(iconLabel(icon, 15, TEXT)); caption.add(label(title, 13, TEXT, true)); panel.add(caption, BorderLayout.SOUTH); return panel; }

    private JPanel sectionHeading(VectorIcon.Type icon, String text, Color color) { JPanel panel = new JPanel(new BorderLayout(10, 0)); panel.setOpaque(false); panel.setBorder(new EmptyBorder(1, 2, 0, 2)); panel.add(iconLabel(icon, 26, color), BorderLayout.WEST); panel.add(label(text, 19, TEXT, true), BorderLayout.CENTER); if (text.equals("Lưu lượng mạng")) { JComboBox<String> range = new JComboBox<>(new String[]{"Thời gian thực", "60 giây gần nhất"}); range.setFont(new Font("Segoe UI", Font.PLAIN, 12)); range.setBorder(BorderFactory.createLineBorder(BORDER)); panel.add(range, BorderLayout.EAST); } return panel; }
    private JPanel coloredLegend(String prefix, JLabel value, Color color) { JPanel panel = new JPanel(new FlowLayout(FlowLayout.CENTER, 4, 0)); panel.setOpaque(false); panel.add(iconLabel(VectorIcon.Type.DOT, 11, color)); panel.add(label(prefix, 13, TEXT, false)); panel.add(value); return panel; }
    private JPanel vertical(String title, JLabel state, JLabel detail) { JPanel body = new JPanel(); body.setOpaque(false); body.setLayout(new BoxLayout(body, BoxLayout.Y_AXIS)); body.add(label(title, 13, TEXT, true)); body.add(Box.createVerticalStrut(2)); body.add(state); body.add(detail); return body; }
    private RoundedPanel card(Color fill) { RoundedPanel panel = new RoundedPanel(fill); panel.setBorder(new EmptyBorder(14, 16, 14, 16)); return panel; }
    private static JLabel label(String text, int size, Color color, boolean bold) { JLabel result = new JLabel(text); result.setFont(new Font("Segoe UI", bold ? Font.BOLD : Font.PLAIN, size)); result.setForeground(color); return result; }
    private static JLabel iconLabel(VectorIcon.Type type, int size, Color color) { JLabel result = new JLabel(new VectorIcon(type, size, color)); result.setPreferredSize(new Dimension(size, size)); result.setMinimumSize(new Dimension(size, size)); return result; }

    private static JButton actionButton(String text, Color color) { JButton button = new JButton(text); button.setFont(new Font("Segoe UI", Font.BOLD, 12)); button.setForeground(color); button.setFocusPainted(false); button.setOpaque(false); button.setContentAreaFilled(false); button.setBorder(BorderFactory.createLineBorder(new Color(color.getRed(), color.getGreen(), color.getBlue(), 150), 1, true)); button.setMargin(new Insets(7, 10, 7, 10)); return button; }
    private static JButton sendButton(String text, int width) { JButton button = new JButton(text, new VectorIcon(VectorIcon.Type.SEND, 19, Color.WHITE)); button.setPreferredSize(new Dimension(width, 42)); button.setBackground(PRIMARY); button.setForeground(Color.WHITE); button.setFont(new Font("Segoe UI", Font.BOLD, 12)); button.setFocusPainted(false); button.setBorderPainted(false); button.setOpaque(true); return button; }

    private void sampleTelemetry() { try { ClientTelemetrySampler.TelemetrySnapshot value = telemetry.sample(); SwingUtilities.invokeLater(() -> applyTelemetry(value)); } catch (RuntimeException error) { System.getLogger(getClass().getName()).log(System.Logger.Level.DEBUG, "Client telemetry sample failed", error); } }
    private void applyTelemetry(ClientTelemetrySampler.TelemetrySnapshot value) { double cpu = clamp(value.cpuPercent()), ram = clamp(value.ramPercent()), disk = clamp(value.diskPercent()); downloadValue.setText(String.format(Locale.ROOT, "%.2f Mbps", value.downloadMbps())); uploadValue.setText(String.format(Locale.ROOT, "%.2f Mbps", value.uploadMbps())); cpuRing.setValue(cpu); ramRing.setValue(ram); diskRing.setValue(disk); networkChart.addPoint(value.downloadMbps(), value.uploadMbps()); performanceChart.addPoint(cpu, ram); machineName.setText(value.machineName()); osName.setText(value.operatingSystem()); cpuModel.setText(value.cpuModel()); memory.setText(formatMemory(value.usedMemoryBytes(), value.totalMemoryBytes())); resolution.setText(value.screenResolution()); ipAddress.setText(value.ipAddress()); macAddress.setText(value.macAddress()); topClock.setText(TIME.format(value.sampledAt())); topDate.setText(DATE.format(value.sampledAt())); }
    private static String formatMemory(long used, long total) { return total <= 0 ? "Unavailable" : String.format(Locale.ROOT, "%.1f / %.1f GB", used / 1_073_741_824.0, total / 1_073_741_824.0); }
    private static double clamp(double value) { return !Double.isFinite(value) ? 0 : Math.max(0, Math.min(100, value)); }

    @Override public CompletionStage<Boolean> requestConsent(String adminName) { return confirm("Screen sharing request", "Admin is requesting to view your screen. Capturing starts only after you accept.", "Accept"); }
    @Override public CompletionStage<Boolean> requestControlConsent(String adminName) { return confirm("Remote control request", "Admin requests mouse and keyboard control. You can revoke access at any time.", "Allow Control"); }
    private CompletionStage<Boolean> confirm(String title, String prompt, String accept) { if (GraphicsEnvironment.isHeadless()) return CompletableFuture.completedFuture(false); CompletableFuture<Boolean> result = new CompletableFuture<>(); SwingUtilities.invokeLater(() -> { frame.setVisible(true); frame.setExtendedState(JFrame.NORMAL); frame.toFront(); Object[] options = {accept, "Reject"}; int choice = JOptionPane.showOptionDialog(frame, prompt, title, JOptionPane.YES_NO_OPTION, JOptionPane.WARNING_MESSAGE, null, options, options[1]); result.complete(choice == 0); }); return result; }

    @Override public void sharingStarted(Runnable action) { stopAction = Objects.requireNonNull(action); activelySharing = true; onEdt(() -> { sharingState.setText("Đang hoạt động"); sharingState.setForeground(SUCCESS); sharingDetail.setText("Màn hình đang được Admin xem"); stop.setEnabled(true); frame.setVisible(true); }); }
    @Override public void sharingStopped(String reason) { stopAction = () -> { }; activelySharing = false; onEdt(() -> { sharingState.setText("Không hoạt động"); sharingState.setForeground(SECONDARY); sharingDetail.setText(reason == null || reason.isBlank() ? "Chưa chia sẻ màn hình" : reason); stop.setEnabled(false); }); }
    @Override public void controlStarted(Runnable action) { revokeAction = Objects.requireNonNull(action); activelyControlled = true; onEdt(() -> { controlState.setText("Đã cho phép"); controlState.setForeground(SUCCESS); controlDetail.setText("Admin đang điều khiển máy"); revoke.setEnabled(true); frame.setVisible(true); frame.toFront(); }); }
    @Override public void controlStopped(String reason) { revokeAction = () -> { }; activelyControlled = false; onEdt(() -> { controlState.setText("Chưa cho phép"); controlState.setForeground(SECONDARY); controlDetail.setText(reason == null || reason.isBlank() ? "Chưa có quyền điều khiển" : reason); revoke.setEnabled(false); }); }
    @Override public void connectionChanged(boolean connected) { onEdt(() -> { connectionLabel.setText(connected ? "Connected to Admin" : "Disconnected from Admin"); sidebarConnection.setText(connected ? "Connected to Admin" : "Disconnected from Admin"); sidebarConnection.setIcon(new VectorIcon(VectorIcon.Type.DOT, 11, connected ? SUCCESS : DANGER)); connectionPill.setText(connected ? "Connected" : "Disconnected"); connectionPill.setIcon(new VectorIcon(VectorIcon.Type.DOT, 10, connected ? SUCCESS : DANGER)); connectionPill.setForeground(connected ? SUCCESS : DANGER); clientState.setText(connected ? "Online" : "Offline"); clientState.setForeground(connected ? SUCCESS : DANGER); clientDetail.setText(connected ? "Đang kết nối với Admin" : "Đang chờ kết nối đến Admin"); }); }
    @Override public void connectionFailed(String reason) { onEdt(() -> { connectionPill.setText("Reconnecting"); connectionPill.setIcon(new VectorIcon(VectorIcon.Type.DOT, 10, WARNING)); connectionPill.setForeground(WARNING); connectionLabel.setText("Retrying connection"); clientDetail.setText(reason == null ? "Đang thử kết nối lại" : reason); }); }
    @Override public void clientIdentity(String name, String clientId) { clientDisplayName = name == null || name.isBlank() ? "NetworkAdmin Client" : name; }
    @Override public void sessionChanged(String sessionId) { onEdt(() -> { String value = sessionId == null || sessionId.isBlank() ? "—" : sessionId; sessionValue.setText(value); topSession.setText(value); sidebarSession.setText("Session: " + value); sessionDetail.setText(value.equals("—") ? "Chưa có phiên kết nối" : "Đang hoạt động"); }); }

    public void setChatSender(Consumer<String> action) { chatSender = Objects.requireNonNull(action); }
    public void chatReceived(ClientChatUseCases.ChatMessage message) { if (message != null) onEdt(() -> { chatTranscript.addMessage(message); chatPageTranscript.addMessage(message); }); }
    private void sendChat(JTextField source) { String value = source.getText(); if (value == null || value.isBlank()) return; source.setText(""); chatWorkers.execute(() -> { try { chatSender.accept(value.strip()); } catch (RuntimeException error) { onEdt(() -> JOptionPane.showMessageDialog(frame, error.getMessage(), "Chat error", JOptionPane.WARNING_MESSAGE)); } }); }
    @Override public void close() {
        if (telemetryWorkers != null) telemetryWorkers.shutdownNow();
        if (chatWorkers != null) chatWorkers.shutdownNow();
        onEdt(() -> {
            if (remoteCountdown != null) { remoteCountdown.stop(); remoteCountdown = null; }
            cancelRemoteAction = () -> { };
            cancelRemoteAdministration.setEnabled(false);
            if (frame != null) frame.dispose();
        });
    }
    private static void onEdt(Runnable action) { if (GraphicsEnvironment.isHeadless()) return; if (SwingUtilities.isEventDispatchThread()) action.run(); else SwingUtilities.invokeLater(action); }

    private static final class RoundedPanel extends JPanel { private final Color fill; private RoundedPanel(Color fill) { this.fill = fill; setOpaque(false); } @Override protected void paintComponent(Graphics graphics) { Graphics2D g = (Graphics2D) graphics.create(); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); g.setColor(new Color(18, 42, 73, 20)); g.fillRoundRect(2, 3, getWidth() - 4, getHeight() - 4, 18, 18); g.setColor(fill); g.fillRoundRect(0, 0, getWidth() - 4, getHeight() - 6, 17, 17); g.setColor(BORDER); g.drawRoundRect(0, 0, getWidth() - 4, getHeight() - 6, 17, 17); g.dispose(); super.paintComponent(graphics); } }
    private static final class ResponsiveGrid extends JPanel {
        private final int columns, minCellWidth, rowHeight, gap;
        private ResponsiveGrid(int columns, int minCellWidth, int rowHeight, int gap) { this.columns = columns; this.minCellWidth = minCellWidth; this.rowHeight = rowHeight; this.gap = gap; setOpaque(false); setLayout(null); setAlignmentX(Component.LEFT_ALIGNMENT); setPreferredSize(new Dimension(columns * minCellWidth + (columns - 1) * gap, rowHeight)); setMinimumSize(new Dimension(minCellWidth, rowHeight)); setMaximumSize(new Dimension(Integer.MAX_VALUE, rowHeight * 3 + gap * 2)); }
        @Override public void doLayout() { int width = getWidth(); int actualColumns = Math.max(1, Math.min(columns, (width + gap) / (minCellWidth + gap))); int cellWidth = Math.max(1, (width - (actualColumns - 1) * gap) / actualColumns); int rows = (getComponentCount() + actualColumns - 1) / actualColumns; for (int i = 0; i < getComponentCount(); i++) { int row = i / actualColumns, column = i % actualColumns; getComponent(i).setBounds(column * (cellWidth + gap), row * (rowHeight + gap), cellWidth, rowHeight); } setPreferredSize(new Dimension(width, rows * rowHeight + Math.max(0, rows - 1) * gap)); }
    }
    private static final class MetricRing extends JComponent { private final Color color; private double value; private MetricRing(Color color) { this.color = color; setPreferredSize(new Dimension(106, 104)); } void setValue(double value) { this.value = clamp(value); repaint(); } @Override protected void paintComponent(Graphics graphics) { Graphics2D g = (Graphics2D) graphics.create(); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); int size = Math.min(getWidth(), getHeight()) - 18, x = (getWidth() - size) / 2, y = 4; g.setStroke(new BasicStroke(8, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND)); g.setColor(new Color(219, 224, 233)); g.drawArc(x, y, size, size, 90, -359); g.setColor(color); g.drawArc(x, y, size, size, 90, -(int) Math.round(value * 3.59)); g.setFont(new Font("Segoe UI", Font.BOLD, 23)); g.setColor(TEXT); String text = String.format(Locale.ROOT, "%.0f%%", value); FontMetrics metrics = g.getFontMetrics(); g.drawString(text, (getWidth() - metrics.stringWidth(text)) / 2, y + size / 2 + metrics.getAscent() / 3); g.dispose(); } }
    private static final class LineChartPanel extends JPanel {
        private final Color firstColor, secondColor; private final boolean percentage; private final Deque<double[]> values = new ArrayDeque<>();
        private LineChartPanel(Color firstColor, Color secondColor, boolean percentage) { this.firstColor = firstColor; this.secondColor = secondColor; this.percentage = percentage; setOpaque(false); setPreferredSize(new Dimension(0, 190)); setToolTipText(""); }
        void addPoint(double first, double second) { values.addLast(new double[]{Math.max(0, first), Math.max(0, second)}); while (values.size() > 31) values.removeFirst(); repaint(); }
        @Override public String getToolTipText(MouseEvent event) { if (values.isEmpty()) return null; int left = 38, width = Math.max(1, getWidth() - left - 8); int index = Math.min(values.size() - 1, Math.max(0, Math.round((event.getX() - left) * (values.size() - 1f) / width))); double[] value = values.stream().skip(index).findFirst().orElse(values.peekLast()); return String.format(Locale.ROOT, "Download: %.2f | Upload: %.2f %s", value[0], value[1], percentage ? "%" : "Mbps"); }
        @Override protected void paintComponent(Graphics graphics) { super.paintComponent(graphics); Graphics2D g = (Graphics2D) graphics.create(); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); int left = 38, top = 10, right = 8, bottom = 28, width = Math.max(1, getWidth() - left - right), height = Math.max(1, getHeight() - top - bottom); double max = percentage ? 100 : scaleMax(); g.setFont(new Font("Segoe UI", Font.PLAIN, 10)); for (int i = 0; i <= 4; i++) { int y = top + height - i * height / 4; g.setColor(new Color(227, 232, 240)); g.drawLine(left, y, left + width, y); g.setColor(SECONDARY); g.drawString(formatScale(max * i / 4), 0, y + 4); } if (values.isEmpty()) { g.setColor(SECONDARY); g.drawString("Chưa có dữ liệu telemetry", left + 14, top + height / 2); g.dispose(); return; } drawSeries(g, left, top, width, height, 0, firstColor, max); drawSeries(g, left, top, width, height, 1, secondColor, max); g.setColor(SECONDARY); g.drawString("60s", left, getHeight() - 6); g.drawString("Bây giờ", left + width - 42, getHeight() - 6); g.dispose(); }
        private double scaleMax() { double max = values.stream().flatMapToDouble(a -> java.util.stream.DoubleStream.of(a[0], a[1])).max().orElse(1); double step = max < 10 ? 2 : max < 100 ? 25 : 50; return Math.max(step, Math.ceil(max / step) * step); }
        private void drawSeries(Graphics2D g, int left, int top, int width, int height, int index, Color color, double max) { double[] previous = null; int previousX = 0, i = 0; g.setColor(color); g.setStroke(new BasicStroke(2.1f, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND)); for (double[] value : values) { int x = left + (values.size() == 1 ? width : i * width / (values.size() - 1)); int y = top + height - (int) Math.round(Math.min(max, value[index]) * height / max); if (previous != null) g.drawLine(previousX, (int) previous[1], x, y); previous = new double[]{x, y}; previousX = x; i++; } }
        private String formatScale(double value) { return percentage ? String.format(Locale.ROOT, "%.0f%%", value) : String.format(Locale.ROOT, "%.0f", value); }
    }
    private static final class HintTextField extends JTextField { private final String hint; private HintTextField(String hint) { this.hint = hint; } @Override protected void paintComponent(Graphics graphics) { super.paintComponent(graphics); if (getText().isEmpty() && !isFocusOwner()) { Graphics2D g = (Graphics2D) graphics.create(); g.setColor(SECONDARY); g.setFont(getFont()); g.drawString(hint, getInsets().left, getHeight() / 2 + g.getFontMetrics().getAscent() / 2 - 2); g.dispose(); } } }
    private static final class ChatTranscript extends JPanel {
        private final JPanel rows = new JPanel(); private final JLabel empty = label("Chưa có tin nhắn", 13, SECONDARY, false);
        private ChatTranscript() { setLayout(new BorderLayout()); setBackground(CARD); rows.setOpaque(false); rows.setLayout(new BoxLayout(rows, BoxLayout.Y_AXIS)); add(empty, BorderLayout.CENTER); add(rows, BorderLayout.NORTH); }
        void addMessage(ClientChatUseCases.ChatMessage message) { if (empty.getParent() != null) remove(empty); boolean outgoing = !"Admin".equalsIgnoreCase(message.sender()); JPanel line = new JPanel(new FlowLayout(outgoing ? FlowLayout.RIGHT : FlowLayout.LEFT, 4, 5)); line.setOpaque(false); Bubble bubble = new Bubble(outgoing ? new Color(214, 231, 255) : new Color(241, 243, 248)); bubble.setLayout(new BoxLayout(bubble, BoxLayout.Y_AXIS)); JLabel text = label("<html><body style='width:260px'>" + escapeHtml(message.text()) + "</body></html>", 13, TEXT, false); text.setBorder(new EmptyBorder(2, 4, 2, 4)); JLabel stamp = label(TIME.format(message.sentAt()) + (outgoing ? " · Đã gửi" : ""), 10, SECONDARY, false); stamp.setAlignmentX(outgoing ? RIGHT_ALIGNMENT : LEFT_ALIGNMENT); bubble.add(text); bubble.add(stamp); line.add(bubble); rows.add(line); revalidate(); rows.revalidate(); rows.repaint(); JScrollPane scroll = (JScrollPane) SwingUtilities.getAncestorOfClass(JScrollPane.class, this); if (scroll != null) SwingUtilities.invokeLater(() -> scroll.getVerticalScrollBar().setValue(scroll.getVerticalScrollBar().getMaximum())); }
        private static String escapeHtml(String value) { return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\n", "<br>"); }
    }
    private static final class Bubble extends JPanel { private final Color fill; private Bubble(Color fill) { this.fill = fill; setOpaque(false); setBorder(new EmptyBorder(6, 10, 6, 10)); } @Override protected void paintComponent(Graphics graphics) { Graphics2D g = (Graphics2D) graphics.create(); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); g.setColor(fill); g.fillRoundRect(0, 0, getWidth() - 1, getHeight() - 1, 15, 15); g.dispose(); super.paintComponent(graphics); } }
}
