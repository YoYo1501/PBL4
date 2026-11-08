package vn.networkadmin.client.presentation.screenshare;

import vn.networkadmin.client.application.chat.ClientChatUseCases;
import vn.networkadmin.client.application.remoteadministration.RemoteAdministrationClientView;
import vn.networkadmin.client.application.remotecontrol.RemoteControlClientView;
import vn.networkadmin.client.application.screenshare.ScreenShareClientView;
import vn.networkadmin.client.infrastructure.monitoring.ClientTelemetrySampler;
import vn.networkadmin.client.infrastructure.monitoring.NetworkTrafficHistory;
import vn.networkadmin.network.domain.NetworkInterfaceInfo;

import javax.swing.*;
import javax.swing.border.EmptyBorder;
import java.awt.*;
import java.awt.event.MouseEvent;
import java.awt.geom.Path2D;
import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.List;
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
    // Keep these values aligned with admin-app's AdminTheme without creating a module dependency.
    private static final Color PRIMARY = new Color(8, 117, 249);
    private static final Color PRIMARY_DARK = new Color(0, 86, 179);
    private static final Color SIDEBAR = new Color(8, 24, 47);
    private static final Color SIDEBAR_HOVER = new Color(20, 42, 70);
    private static final Color BACKGROUND = new Color(243, 247, 253);
    private static final Color CARD = Color.WHITE;
    private static final Color TEXT = new Color(11, 25, 48);
    private static final Color SECONDARY = new Color(83, 107, 141);
    private static final Color SUCCESS = new Color(22, 163, 74);
    private static final Color WARNING = new Color(245, 158, 11);
    private static final Color DANGER = new Color(239, 68, 68);
    private static final Color BORDER = new Color(218, 229, 244);
    private static final DateTimeFormatter TIME = DateTimeFormatter.ofPattern("HH:mm").withZone(ZoneId.systemDefault());
    private static final DateTimeFormatter DATE = DateTimeFormatter.ofPattern("EEE, MMM d, yyyy", Locale.ENGLISH).withZone(ZoneId.systemDefault());
    private static final DateTimeFormatter LOG_TIME = DateTimeFormatter.ofPattern("HH:mm:ss").withZone(ZoneId.systemDefault());

    private final JFrame frame;
    private final String serverEndpoint;
    private final JLabel connectionLabel = label("Disconnected from Admin", 13, SECONDARY, false);
    private final JLabel sidebarConnection = label("Disconnected from Admin", 13, Color.WHITE, true);
    private final JLabel sidebarSession = label("Session: —", 12, Color.WHITE, false);
    private final JLabel pageTitle = label("Network Administration – Client", 34, TEXT, true);
    private final JLabel pageSubtitle = label("Workstation status and connection to the management system", 16, SECONDARY, false);
    private final JLabel topSession = label("—", 15, TEXT, true);
    private final JLabel topClock = label("--:--", 20, TEXT, true);
    private final JLabel topDate = label("", 12, SECONDARY, false);
    private final JLabel sessionValue = label("—", 18, TEXT, true);
    private final JLabel sessionDetail = label("No active connection session", 12, SECONDARY, false);
    private final JLabel connectionPill = label("Disconnected", 13, DANGER, true);
    private final JLabel clientState = label("Offline", 18, DANGER, true);
    private final JLabel clientDetail = label("Waiting for connection to Admin", 12, SECONDARY, false);
    private final JLabel sharingState = label("Inactive", 18, SECONDARY, true);
    private final JLabel sharingDetail = label("Screen sharing is not active", 12, SECONDARY, false);
    private final JLabel controlState = label("Not allowed", 18, SECONDARY, true);
    private final JLabel controlDetail = label("No remote control request", 12, SECONDARY, false);
    private final JLabel remoteAdministrationState = label("No pending operation", 18, SECONDARY, true);
    private final JLabel remoteAdministration = label("No remote administration operation pending", 12, SECONDARY, false);
    private final JLabel downloadValue = label("0.00 Mbps", 13, PRIMARY, true);
    private final JLabel uploadValue = label("0.00 Mbps", 13, SUCCESS, true);
    private final JLabel machineName = label("Collecting…", 14, TEXT, true);
    private final JLabel osName = label("Collecting…", 14, TEXT, true);
    private final JLabel cpuModel = label("Collecting…", 14, TEXT, true);
    private final JLabel memory = label("Collecting…", 14, TEXT, true);
    private final JLabel resolution = label("Collecting…", 14, TEXT, true);
    private final JLabel ipAddress = label("Collecting…", 14, TEXT, true);
    private final JLabel macAddress = label("Collecting…", 14, TEXT, true);
    private final JButton stop = actionButton("Stop Sharing", ButtonVariant.SECONDARY, VectorIcon.Type.STOP);
    private final JButton revoke = actionButton("Revoke Access", ButtonVariant.DANGER, VectorIcon.Type.LOCK);
    private final HintTextField chatInput = new HintTextField("Type a message…");
    private final HintTextField chatPageInput = new HintTextField("Type a message…");
    private final JButton cancelRemoteAdministration = actionButton("Cancel Operation", ButtonVariant.SECONDARY, null);
    private final ChatTranscript chatTranscript = new ChatTranscript();
    private final ChatTranscript chatPageTranscript = new ChatTranscript();
    private final LineChartPanel networkChart = new LineChartPanel(PRIMARY, SUCCESS, false, 60, "Collecting network traffic...");
    private final LineChartPanel performanceChart = new LineChartPanel(PRIMARY, new Color(117, 23, 234), true, 31, "No telemetry data");
    private final MetricRing cpuRing = new MetricRing(PRIMARY);
    private final MetricRing ramRing = new MetricRing(new Color(117, 23, 234));
    private final MetricRing diskRing = new MetricRing(PRIMARY);
    private final ScheduledExecutorService telemetryWorkers;
    private final ExecutorService chatWorkers;
    private final ClientTelemetrySampler telemetry = new ClientTelemetrySampler();
    private final CardLayout contentLayout = new CardLayout();
    private final JPanel content = new JPanel(contentLayout);
    private final DefaultListModel<String> logModel = new DefaultListModel<>();
    private final JLabel logEmptyLabel = label("No log entries yet", 14, SECONDARY, false);
    private JList<String> logList;
    private JPanel networkInfoRows;
    private JLabel networkInfoStatus;
    private JPanel settingsRows;
    private JPanel dashboard;
    private ResponsiveGrid statusCards;
    private ResponsiveGrid remoteAdministrationGrid;
    private ResponsiveGrid charts;
    private ResponsiveGrid details;
    private volatile Runnable stopAction = () -> { };
    private volatile Runnable revokeAction = () -> { };
    private volatile Runnable disconnectAction = () -> { };
    private volatile Consumer<String> chatSender = ignored -> { };
    private volatile boolean activelySharing;
    private volatile boolean activelyControlled;
    private volatile boolean connectedToAdmin;
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
                close();
            }
        });
        frame.setMinimumSize(new Dimension(1080, 680));
        Rectangle workArea = GraphicsEnvironment.getLocalGraphicsEnvironment().getMaximumWindowBounds();
        frame.setSize(Math.min(1680, Math.max(1080, workArea.width)), Math.min(942, Math.max(680, workArea.height)));
        frame.setLocationRelativeTo(null);
        frame.setExtendedState(JFrame.MAXIMIZED_BOTH);
        frame.setContentPane(buildShell());
        stop.setEnabled(false); stop.setVisible(false);
        revoke.setEnabled(false); revoke.setVisible(false);
        cancelRemoteAdministration.setEnabled(false); cancelRemoteAdministration.setVisible(false);
        stop.addActionListener(event -> stopAction.run());
        revoke.addActionListener(event -> revokeAction.run());
        cancelRemoteAdministration.setEnabled(false);
        cancelRemoteAdministration.addActionListener(event -> {
            Runnable action = cancelRemoteAction;
            cancelRemoteAction = () -> { };
            cancelRemoteAdministration.setEnabled(false);
            cancelRemoteAdministration.setVisible(false);
            setRemoteAdministrationVisible(false);
            action.run();
        });
        chatInput.addActionListener(event -> sendChat(chatInput));
        chatPageInput.addActionListener(event -> sendChat(chatPageInput));
        frame.setVisible(true);
        telemetryWorkers = Executors.newSingleThreadScheduledExecutor(task -> daemon(task, "client-dashboard-telemetry"));
        chatWorkers = Executors.newSingleThreadExecutor(task -> daemon(task, "client-chat-sender"));
        telemetryWorkers.scheduleWithFixedDelay(this::sampleTelemetry, 0, 1, TimeUnit.SECONDS);
        telemetryWorkers.scheduleWithFixedDelay(this::refreshNetworkInfoPage, 15, 15, TimeUnit.SECONDS);
        logEvent("Client dashboard started");
        refreshNetworkInfoPage();
    }

    private static Thread daemon(Runnable task, String name) { Thread thread = new Thread(task, name); thread.setDaemon(true); return thread; }

    private JPanel buildShell() {
        JPanel root = new JPanel(new BorderLayout()); root.setBackground(BACKGROUND); root.add(buildSidebar(), BorderLayout.WEST);
        JPanel workspace = new JPanel(new BorderLayout()); workspace.setBackground(BACKGROUND);
        content.add(buildDashboard(), "dashboard");
        content.add(chatPage(), "chat");
        content.add(buildNetworkInfoPage(), "network");
        content.add(buildLogsPage(), "logs");
        content.add(buildSettingsPage(), "settings");
        content.add(simplePage("About", "NetworkAdmin Client · Java 21 · Swing"), "about");
        workspace.add(buildHeader(), BorderLayout.NORTH); workspace.add(content, BorderLayout.CENTER); root.add(workspace, BorderLayout.CENTER); return root;
    }

    private JPanel buildSidebar() {
        JPanel sidebar = new JPanel(new BorderLayout()); sidebar.setBackground(SIDEBAR); sidebar.setPreferredSize(new Dimension(250, 0));

        JPanel top = new JPanel(); top.setOpaque(false); top.setLayout(new BoxLayout(top, BoxLayout.Y_AXIS));
        JPanel brandRow = new JPanel(new FlowLayout(FlowLayout.LEFT, 10, 0)); brandRow.setOpaque(false); brandRow.setBorder(new EmptyBorder(20, 16, 16, 16));
        brandRow.add(iconLabel(VectorIcon.Type.LOGO, 42, new Color(33, 145, 255)));
        JPanel brandText = new JPanel(); brandText.setOpaque(false); brandText.setLayout(new BoxLayout(brandText, BoxLayout.Y_AXIS));
        brandText.add(label("NetworkAdmin", 19, Color.WHITE, true)); brandText.add(Box.createVerticalStrut(2)); brandText.add(label("Client", 19, Color.WHITE, true));
        JLabel version = label("v2.0", 10, Color.WHITE, true); version.setOpaque(true); version.setBackground(PRIMARY); version.setBorder(new EmptyBorder(2, 6, 2, 6));
        JPanel versionWrap = new JPanel(new FlowLayout(FlowLayout.LEFT, 0, 0)); versionWrap.setOpaque(false); versionWrap.setBorder(new EmptyBorder(8, 0, 0, 0)); versionWrap.add(version); brandText.add(versionWrap);
        brandRow.add(brandText); top.add(brandRow); sidebar.add(top, BorderLayout.NORTH);

        JPanel nav = new JPanel(); nav.setOpaque(false); nav.setBorder(new EmptyBorder(0, 0, 0, 0)); nav.setLayout(new BoxLayout(nav, BoxLayout.Y_AXIS));
        addNavButton(nav, VectorIcon.Type.DASHBOARD, "Dashboard", "dashboard", true);
        addNavButton(nav, VectorIcon.Type.CHAT, "Chat", "chat", false);
        addNavButton(nav, VectorIcon.Type.NETWORK, "Network Info", "network", false);
        addNavButton(nav, VectorIcon.Type.LOGS, "Logs", "logs", false);
        addSidebarDivider(nav);
        addNavButton(nav, VectorIcon.Type.SETTINGS, "Settings", "settings", false);
        addNavButton(nav, VectorIcon.Type.ABOUT, "About", "about", false);
        sidebar.add(nav, BorderLayout.CENTER);

        RoundedPanel status = new RoundedPanel(new Color(18, 42, 72), new Color(48, 77, 115));
        status.setLayout(new BoxLayout(status, BoxLayout.Y_AXIS)); status.setBorder(new EmptyBorder(12, 14, 12, 14));
        sidebarConnection.setIcon(new VectorIcon(VectorIcon.Type.DOT, 11, SUCCESS)); sidebarConnection.setAlignmentX(Component.LEFT_ALIGNMENT);
        sidebarSession.setIcon(new VectorIcon(VectorIcon.Type.SESSION, 16, Color.WHITE)); sidebarSession.setFont(new Font("Segoe UI", Font.PLAIN, 11)); sidebarSession.setAlignmentX(Component.LEFT_ALIGNMENT);
        status.add(sidebarConnection); status.add(Box.createVerticalStrut(5));
        status.add(label("Server:  " + serverEndpoint + " (TLS)", 12, Color.WHITE, false)); status.add(Box.createVerticalStrut(3)); status.add(sidebarSession);
        JPanel statusWrap = new JPanel(new BorderLayout()); statusWrap.setOpaque(false); statusWrap.setBorder(new EmptyBorder(0, 12, 14, 12)); statusWrap.add(status, BorderLayout.CENTER); sidebar.add(statusWrap, BorderLayout.SOUTH);
        return sidebar;
    }

    private void addNavButton(JPanel parent, VectorIcon.Type icon, String text, String page, boolean active) {
        StyledButton button = new StyledButton(text, icon, ButtonVariant.GHOST); button.setSidebarMode(true); button.setActive(active); button.putClientProperty("navActive", active); button.setHorizontalAlignment(SwingConstants.LEFT); button.setIconTextGap(12);
        button.addActionListener(event -> { contentLayout.show(content, page); pageTitle.setText(page.equals("dashboard") ? "Network Administration – Client" : text); pageSubtitle.setText(page.equals("dashboard") ? "Workstation status and connection to the management system" : "NetworkAdmin Client"); for (Component child : parent.getComponents()) if (child instanceof JPanel wrapper) for (Component nested : wrapper.getComponents()) if (nested instanceof StyledButton navButton) { boolean selected = navButton == button; navButton.putClientProperty("navActive", selected); navButton.setActive(selected); } });
        button.setCursor(Cursor.getPredefinedCursor(Cursor.HAND_CURSOR));
        JPanel wrapper = new JPanel(new BorderLayout()); wrapper.setOpaque(false); wrapper.setBorder(new EmptyBorder(3, 10, 3, 10)); wrapper.setPreferredSize(new Dimension(0, 52)); wrapper.setMinimumSize(new Dimension(0, 52)); wrapper.setMaximumSize(new Dimension(Integer.MAX_VALUE, 52)); wrapper.add(button, BorderLayout.CENTER); parent.add(wrapper);
    }

    private static void addSidebarDivider(JPanel parent) { JSeparator divider = new JSeparator(); divider.setForeground(new Color(30, 41, 59)); divider.setBackground(new Color(30, 41, 59)); divider.setMaximumSize(new Dimension(Integer.MAX_VALUE, 1)); parent.add(Box.createVerticalStrut(5)); parent.add(divider); parent.add(Box.createVerticalStrut(5)); }

    private JPanel buildHeader() {
        JPanel header = new JPanel(new BorderLayout(18, 0));
        header.setBackground(BACKGROUND);
        header.setBorder(new EmptyBorder(16, 27, 10, 29));

        JPanel titles = new JPanel();
        titles.setOpaque(false);
        titles.setLayout(new BoxLayout(titles, BoxLayout.Y_AXIS));
        titles.setMinimumSize(new Dimension(0, 0));
        titles.add(pageTitle);
        titles.add(Box.createVerticalStrut(4));
        titles.add(pageSubtitle);
        header.add(titles, BorderLayout.CENTER);

        JPanel meta = new JPanel(new FlowLayout(FlowLayout.RIGHT, 18, 0));
        meta.setOpaque(false);
        meta.setMinimumSize(new Dimension(0, 0));
        meta.add(metaBlock(VectorIcon.Type.CONNECTION, connectionPill, connectionLabel));
        meta.add(metaBlock(VectorIcon.Type.SESSION, topSession, label("Session", 11, SECONDARY, false)));
        JPanel clock = new JPanel();
        clock.setOpaque(false);
        clock.setLayout(new BoxLayout(clock, BoxLayout.Y_AXIS));
        clock.add(new JLabel(new VectorIcon(VectorIcon.Type.CLOCK, 22, PRIMARY)));
        clock.add(topClock);
        clock.add(topDate);
        meta.add(clock);
        header.add(meta, BorderLayout.EAST);
        return header;
    }

    private JPanel metaBlock(VectorIcon.Type icon, JLabel value, JLabel caption) { JPanel panel = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 0)); panel.setOpaque(false); panel.add(iconLabel(icon, 22, PRIMARY)); JPanel text = new JPanel(); text.setOpaque(false); text.setLayout(new BoxLayout(text, BoxLayout.Y_AXIS)); text.add(value); text.add(caption); panel.add(text); return panel; }

    private JPanel buildDashboard() {
        dashboard = new JPanel(new GridBagLayout()); dashboard.setBackground(BACKGROUND); dashboard.setBorder(new EmptyBorder(0, 27, 12, 29));
        statusCards = new ResponsiveGrid(4, 205, 78, 10); statusCards.add(clientCard()); statusCards.add(shareCard()); statusCards.add(controlCard()); statusCards.add(sessionCard());
        remoteAdministrationGrid = new ResponsiveGrid(1, 760, 68, 8); remoteAdministrationGrid.add(remoteAdministrationCard()); remoteAdministrationGrid.setVisible(false);
        charts = new ResponsiveGrid(2, 380, 215, 10); charts.add(networkCard()); charts.add(performanceCard());
        details = new ResponsiveGrid(2, 380, 216, 10); details.add(stationCard()); details.add(chatCard());
        rebuildDashboardLayout();
        return dashboard;
    }

    private void rebuildDashboardLayout() {
        if (dashboard == null) return;
        dashboard.removeAll();
        int row = 0;
        addDashboardRow(dashboard, statusCards, row++, .13, 70);
        if (remoteAdministrationGrid.isVisible()) addDashboardRow(dashboard, remoteAdministrationGrid, row++, .10, 60);
        addDashboardRow(dashboard, charts, row++, remoteAdministrationGrid.isVisible() ? .38 : .43, 195);
        addDashboardRow(dashboard, details, row, remoteAdministrationGrid.isVisible() ? .39 : .44, 195);
        dashboard.revalidate();
        dashboard.repaint();
    }

    private void setRemoteAdministrationVisible(boolean visible) {
        if (remoteAdministrationGrid == null || remoteAdministrationGrid.isVisible() == visible) return;
        remoteAdministrationGrid.setVisible(visible);
        rebuildDashboardLayout();
    }

    private static void addDashboardRow(JPanel dashboard, JComponent component, int row, double weight, int minimumHeight) {
        GridBagConstraints constraints = new GridBagConstraints();
        constraints.gridx = 0; constraints.gridy = row; constraints.weightx = 1; constraints.weighty = weight;
        constraints.fill = GridBagConstraints.BOTH; constraints.insets = new Insets(row == 0 ? 0 : 8, 0, 0, 0);
        component.setMinimumSize(new Dimension(0, minimumHeight)); dashboard.add(component, constraints);
    }

    private JPanel clientCard() { RoundedPanel card = card(new Color(239, 246, 255)); card.setLayout(new BorderLayout(11, 0)); card.add(iconLabel(VectorIcon.Type.CLIENT, 31, PRIMARY), BorderLayout.WEST); card.add(vertical("Client Status", clientState, clientDetail), BorderLayout.CENTER); return card; }
    private JPanel shareCard() { RoundedPanel card = card(new Color(239, 252, 244)); card.setLayout(new BorderLayout(11, 0)); card.add(iconLabel(VectorIcon.Type.SCREEN, 31, SUCCESS), BorderLayout.WEST); card.add(vertical("Screen Sharing", sharingState, sharingDetail), BorderLayout.CENTER); card.add(buttonSlot(stop), BorderLayout.EAST); return card; }
    private JPanel controlCard() { RoundedPanel card = card(new Color(249, 242, 255)); card.setLayout(new BorderLayout(11, 0)); card.add(iconLabel(VectorIcon.Type.CONTROL, 31, new Color(117, 23, 234)), BorderLayout.WEST); card.add(vertical("Remote Control", controlState, controlDetail), BorderLayout.CENTER); card.add(buttonSlot(revoke), BorderLayout.EAST); return card; }
    private JPanel sessionCard() { RoundedPanel card = card(new Color(255, 244, 248)); card.setLayout(new BorderLayout(11, 0)); card.add(iconLabel(VectorIcon.Type.CALENDAR, 31, DANGER), BorderLayout.WEST); card.add(vertical("Current Session", sessionValue, sessionDetail), BorderLayout.CENTER); return card; }

    private JPanel remoteAdministrationCard() {
        RoundedPanel card = card(new Color(255, 248, 236));
        card.setLayout(new BorderLayout(11, 0));
        card.add(iconLabel(VectorIcon.Type.CLOCK, 31, WARNING), BorderLayout.WEST);
        JPanel details = new JPanel(new BorderLayout(0, 2));
        details.setOpaque(false);
        JPanel titleLine = new JPanel(new FlowLayout(FlowLayout.LEFT, 8, 0));
        titleLine.setOpaque(false);
        titleLine.add(label("Remote Administration", 13, TEXT, true));
        titleLine.add(remoteAdministrationState);
        details.add(titleLine, BorderLayout.NORTH);
        details.add(remoteAdministration, BorderLayout.CENTER);
        card.add(details, BorderLayout.CENTER);
        card.add(buttonSlot(cancelRemoteAdministration), BorderLayout.EAST);
        return card;
    }

    private JPanel buttonSlot(JButton button) { JPanel slot = new JPanel(new GridBagLayout()); slot.setOpaque(false); slot.add(button); return slot; }

    private JPanel networkCard() {
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 4)); card.add(sectionHeading(VectorIcon.Type.TRAFFIC, "Network Traffic", PRIMARY), BorderLayout.NORTH);
        JPanel body = new JPanel(new BorderLayout(0, 2)); body.setOpaque(false);
        JPanel legend = new JPanel(new FlowLayout(FlowLayout.CENTER, 24, 0)); legend.setOpaque(false); legend.add(coloredLegend("Download: ", downloadValue, PRIMARY)); legend.add(coloredLegend("Upload: ", uploadValue, SUCCESS)); body.add(legend, BorderLayout.NORTH);
        networkChart.setPreferredSize(new Dimension(0, 148)); networkChart.setBorder(new EmptyBorder(0, 8, 4, 8)); body.add(networkChart, BorderLayout.CENTER); card.add(body, BorderLayout.CENTER); return card;
    }

    private JPanel performanceCard() {
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 4)); card.add(sectionHeading(VectorIcon.Type.PERFORMANCE, "System Performance", PRIMARY), BorderLayout.NORTH);
        JPanel body = new JPanel(new BorderLayout(0, 2)); body.setOpaque(false);
        JPanel rings = new JPanel(new GridLayout(1, 3, 10, 0)); rings.setOpaque(false); rings.add(ringColumn(cpuRing, VectorIcon.Type.CPU, "CPU Usage")); rings.add(ringColumn(ramRing, VectorIcon.Type.RAM, "RAM Usage")); rings.add(ringColumn(diskRing, VectorIcon.Type.RESOLUTION, "Disk Usage")); body.add(rings, BorderLayout.NORTH);
        performanceChart.setPreferredSize(new Dimension(0, 72)); body.add(performanceChart, BorderLayout.CENTER); card.add(body, BorderLayout.CENTER); return card;
    }
    @Override public void showCountdown(String command, int seconds, Runnable cancel) {
        cancelRemoteAction = Objects.requireNonNull(cancel);
        logEvent("Remote administration pending: " + safe(command));
        onEdt(() -> {
            if (remoteCountdown != null) remoteCountdown.stop();
            final int[] remaining = {Math.max(1, seconds)};
            remoteAdministrationState.setText("Pending operation");
            remoteAdministrationState.setForeground(WARNING);
            remoteAdministration.setText(command + " scheduled in " + remaining[0] + " seconds. Cancel if needed.");
            cancelRemoteAdministration.setEnabled(true);
            cancelRemoteAdministration.setVisible(connectedToAdmin);
            setRemoteAdministrationVisible(true);
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
                    cancelRemoteAdministration.setVisible(false);
                    cancelRemoteAction = () -> { };
                    setRemoteAdministrationVisible(false);
                } else remoteAdministration.setText(command + " scheduled in " + remaining[0] + " seconds. Cancel if needed.");
            });
            remoteCountdown.start();
        });
    }
    @Override public void clearCountdown(String status) {
        logEvent("Remote administration cleared" + (status == null || status.isBlank() ? "" : ": " + status));
        onEdt(() -> {
            if (remoteCountdown != null) { remoteCountdown.stop(); remoteCountdown = null; }
            remoteAdministrationState.setText("No pending operation");
            remoteAdministrationState.setForeground(SECONDARY);
            remoteAdministration.setText(status == null || status.isBlank() ? "No remote administration operation pending" : status);
            cancelRemoteAdministration.setEnabled(false);
            cancelRemoteAdministration.setVisible(false);
            cancelRemoteAction = () -> { };
            setRemoteAdministrationVisible(false);
        });
    }

    private JPanel stationCard() {
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 7)); card.add(sectionHeading(VectorIcon.Type.WORKSTATION, "Workstation Information", PRIMARY), BorderLayout.NORTH);
        JPanel rows = new JPanel(); rows.setOpaque(false); rows.setLayout(new BoxLayout(rows, BoxLayout.Y_AXIS));
        addInfoRow(rows, VectorIcon.Type.COMPUTER, "Computer Name", machineName, false);
        addInfoRow(rows, VectorIcon.Type.OS, "Operating System", osName, true);
        addInfoRow(rows, VectorIcon.Type.CPU, "CPU", cpuModel, true);
        addInfoRow(rows, VectorIcon.Type.RAM, "RAM", memory, true);
        addInfoRow(rows, VectorIcon.Type.RESOLUTION, "Resolution", resolution, true);
        addInfoRow(rows, VectorIcon.Type.WIFI, "IP Address", ipAddress, true);
        addInfoRow(rows, VectorIcon.Type.MAC, "MAC Address", macAddress, true);
        cpuModel.setToolTipText("CPU model"); card.add(rows, BorderLayout.CENTER); return card;
    }

    private JPanel chatCard() {
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 8)); card.add(sectionHeading(VectorIcon.Type.CHAT, "Chat with Admin", PRIMARY), BorderLayout.NORTH); JScrollPane scroll = new JScrollPane(chatTranscript); scroll.setBorder(null); scroll.setHorizontalScrollBarPolicy(ScrollPaneConstants.HORIZONTAL_SCROLLBAR_NEVER); scroll.getViewport().setBackground(CARD); scroll.getVerticalScrollBar().setUnitIncrement(14); card.add(scroll, BorderLayout.CENTER);
        JPanel input = chatInput(chatInput, false); card.add(input, BorderLayout.SOUTH); return card;
    }

    private JPanel chatPage() {
        JPanel page = new JPanel(new BorderLayout()); page.setBackground(BACKGROUND); page.setBorder(new EmptyBorder(25, 27, 30, 29)); RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 10)); card.add(sectionHeading(VectorIcon.Type.CHAT, "Chat with Admin", PRIMARY), BorderLayout.NORTH); JScrollPane scroll = new JScrollPane(chatPageTranscript); scroll.setBorder(null); scroll.setHorizontalScrollBarPolicy(ScrollPaneConstants.HORIZONTAL_SCROLLBAR_NEVER); scroll.getViewport().setBackground(CARD); scroll.getVerticalScrollBar().setUnitIncrement(14); card.add(scroll, BorderLayout.CENTER); card.add(chatInput(chatPageInput, true), BorderLayout.SOUTH); page.add(card, BorderLayout.CENTER); return page;
    }

    private JPanel chatInput(JTextField source, boolean wideButton) {
        JPanel row = new JPanel(new BorderLayout(8, 0)); row.setOpaque(false); source.setFont(new Font("Segoe UI", Font.PLAIN, 14)); source.setBorder(new RoundedLineBorder(BORDER, 8));
        JButton send = sendButton(); send.addActionListener(event -> sendChat(source)); row.add(source, BorderLayout.CENTER); row.add(send, BorderLayout.EAST); return row;
    }

    private JPanel simplePage(String title, String description) { return buildAboutPage(); }

    private JPanel buildNetworkInfoPage() {
        JPanel page = pageContainer();
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 10));
        card.add(sectionHeading(VectorIcon.Type.NETWORK, "Network Information", PRIMARY), BorderLayout.NORTH);
        networkInfoStatus = label("Loading local network interfaces...", 12, SECONDARY, false);
        card.add(networkInfoStatus, BorderLayout.SOUTH);
        networkInfoRows = new JPanel(); networkInfoRows.setOpaque(false); networkInfoRows.setLayout(new BoxLayout(networkInfoRows, BoxLayout.Y_AXIS));
        networkInfoRows.add(label("Loading local network interfaces...", 14, SECONDARY, false));
        JScrollPane scroll = new JScrollPane(networkInfoRows); scroll.setBorder(null); scroll.setHorizontalScrollBarPolicy(ScrollPaneConstants.HORIZONTAL_SCROLLBAR_NEVER); scroll.getViewport().setBackground(CARD); scroll.getVerticalScrollBar().setUnitIncrement(14);
        card.add(scroll, BorderLayout.CENTER); page.add(card, BorderLayout.CENTER); return page;
    }

    private JPanel buildLogsPage() {
        JPanel page = pageContainer();
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 10));
        card.add(sectionHeading(VectorIcon.Type.LOGS, "Client Logs", PRIMARY), BorderLayout.NORTH);
        logList = new JList<>(logModel); logList.setFont(new Font("Consolas", Font.PLAIN, 13)); logList.setForeground(TEXT); logList.setBackground(CARD); logList.setSelectionMode(ListSelectionModel.SINGLE_SELECTION); logList.setBorder(new EmptyBorder(4, 4, 4, 4));
        JScrollPane scroll = new JScrollPane(logList); scroll.setBorder(null); scroll.setHorizontalScrollBarPolicy(ScrollPaneConstants.HORIZONTAL_SCROLLBAR_NEVER); scroll.getViewport().setBackground(CARD); scroll.getVerticalScrollBar().setUnitIncrement(14);
        JPanel body = new JPanel(new BorderLayout()); body.setOpaque(false); body.add(logEmptyLabel, BorderLayout.NORTH); body.add(scroll, BorderLayout.CENTER);
        card.add(body, BorderLayout.CENTER); page.add(card, BorderLayout.CENTER); return page;
    }

    private JPanel buildSettingsPage() {
        JPanel page = pageContainer();
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 10));
        card.add(sectionHeading(VectorIcon.Type.SETTINGS, "Client Settings", PRIMARY), BorderLayout.NORTH);
        settingsRows = new JPanel(); settingsRows.setOpaque(false); settingsRows.setLayout(new BoxLayout(settingsRows, BoxLayout.Y_AXIS));
        JScrollPane scroll = new JScrollPane(settingsRows); scroll.setBorder(null); scroll.setHorizontalScrollBarPolicy(ScrollPaneConstants.HORIZONTAL_SCROLLBAR_NEVER); scroll.getViewport().setBackground(CARD); scroll.getVerticalScrollBar().setUnitIncrement(14);
        card.add(scroll, BorderLayout.CENTER); page.add(card, BorderLayout.CENTER); refreshSettingsPage(); return page;
    }

    private JPanel buildAboutPage() {
        JPanel page = pageContainer();
        RoundedPanel card = card(CARD); card.setLayout(new BorderLayout(0, 10));
        card.add(sectionHeading(VectorIcon.Type.ABOUT, "About NetworkAdmin Client", PRIMARY), BorderLayout.NORTH);
        JPanel rows = new JPanel(); rows.setOpaque(false); rows.setLayout(new BoxLayout(rows, BoxLayout.Y_AXIS));
        addDataRow(rows, "Application", "NetworkAdmin Client"); addDataRow(rows, "Version", "v2.0");
        addDataRow(rows, "Runtime", System.getProperty("java.version", "Unavailable") + " / Swing");
        addDataRow(rows, "Operating system", System.getProperty("os.name", "Unavailable") + " " + System.getProperty("os.version", ""));
        addDataRow(rows, "Architecture", System.getProperty("os.arch", "Unavailable")); addDataRow(rows, "Client identity", clientDisplayName);
        card.add(rows, BorderLayout.CENTER); page.add(card, BorderLayout.CENTER); return page;
    }

    private JPanel pageContainer() { JPanel page = new JPanel(new BorderLayout()); page.setBackground(BACKGROUND); page.setBorder(new EmptyBorder(25, 27, 30, 29)); return page; }

    private void refreshNetworkInfoPage() {
        if (telemetryWorkers == null) return;
        try {
            telemetryWorkers.execute(() -> {
                try { List<NetworkInterfaceInfo> interfaces = telemetry.networkInterfaces(); onEdt(() -> renderNetworkInterfaces(interfaces)); }
                catch (RuntimeException error) { onEdt(() -> renderNetworkInfoError(error)); }
            });
        } catch (RuntimeException ignored) { }
    }

    private void renderNetworkInterfaces(List<NetworkInterfaceInfo> interfaces) {
        if (networkInfoRows == null || networkInfoStatus == null) return;
        networkInfoRows.removeAll();
        if (interfaces == null || interfaces.isEmpty()) networkInfoRows.add(label("No local IPv4 network interfaces were detected.", 14, SECONDARY, false));
        else for (NetworkInterfaceInfo info : interfaces) networkInfoRows.add(networkInterfaceCard(info));
        networkInfoStatus.setText("Updated " + LOG_TIME.format(Instant.now()) + " - " + (interfaces == null ? 0 : interfaces.size()) + " interface(s)");
        networkInfoRows.revalidate(); networkInfoRows.repaint();
    }

    private void renderNetworkInfoError(RuntimeException error) {
        if (networkInfoRows == null || networkInfoStatus == null) return;
        networkInfoRows.removeAll(); networkInfoRows.add(label("Unable to read local network interfaces: " + safe(error.getMessage()), 14, DANGER, false));
        networkInfoStatus.setText("Network inventory unavailable"); networkInfoRows.revalidate(); networkInfoRows.repaint();
    }

    private JPanel networkInterfaceCard(NetworkInterfaceInfo info) {
        RoundedPanel panel = card(new Color(248, 251, 255)); panel.setLayout(new BorderLayout(0, 5));
        JPanel title = new JPanel(new BorderLayout(8, 0)); title.setOpaque(false);
        title.add(label(safe(info.name()), 15, TEXT, true), BorderLayout.WEST);
        title.add(label(info.virtual() ? "Virtual interface" : "Physical interface", 12, info.virtual() ? SECONDARY : SUCCESS, false), BorderLayout.EAST);
        panel.add(title, BorderLayout.NORTH);
        JPanel values = new JPanel(new GridLayout(0, 2, 16, 3)); values.setOpaque(false);
        addNetworkValue(values, "Description", info.description()); addNetworkValue(values, "IPv4 address", info.ipAddress()); addNetworkValue(values, "MAC address", info.macAddress()); addNetworkValue(values, "Gateway", info.gateway()); addNetworkValue(values, "Subnet", info.subnet());
        panel.add(values, BorderLayout.CENTER); panel.setAlignmentX(Component.LEFT_ALIGNMENT); panel.setMaximumSize(new Dimension(Integer.MAX_VALUE, 118)); return panel;
    }

    private void addNetworkValue(JPanel parent, String name, String value) { JPanel row = new JPanel(new BorderLayout(8, 0)); row.setOpaque(false); row.add(label(name, 12, SECONDARY, false), BorderLayout.WEST); JLabel actual = label(safe(value), 12, TEXT, true); actual.setHorizontalAlignment(SwingConstants.RIGHT); row.add(actual, BorderLayout.CENTER); parent.add(row); }

    private void refreshSettingsPage() {
        if (settingsRows == null) return;
        settingsRows.removeAll(); addDataRow(settingsRows, "Server endpoint", serverEndpoint); addDataRow(settingsRows, "Transport", "TLS");
        addDataRow(settingsRows, "Connection", connectedToAdmin ? "Connected to Admin" : "Disconnected from Admin"); addDataRow(settingsRows, "Session", sessionValue.getText());
        addDataRow(settingsRows, "Client identity", clientDisplayName); addDataRow(settingsRows, "Telemetry sampling", "1 second"); addDataRow(settingsRows, "Runtime", System.getProperty("java.version", "Unavailable"));
        settingsRows.revalidate(); settingsRows.repaint();
    }

    private void addDataRow(JPanel parent, String name, String value) {
        JPanel row = new JPanel(new BorderLayout(16, 0)); row.setOpaque(false); row.setBorder(new EmptyBorder(8, 4, 8, 4));
        row.add(label(name, 14, SECONDARY, false), BorderLayout.WEST); JLabel actual = label(safe(value), 14, TEXT, true); actual.setHorizontalAlignment(SwingConstants.RIGHT); row.add(actual, BorderLayout.CENTER);
        JSeparator separator = new JSeparator(); separator.setForeground(BORDER); JPanel wrapper = new JPanel(new BorderLayout()); wrapper.setOpaque(false); wrapper.add(row, BorderLayout.CENTER); wrapper.add(separator, BorderLayout.SOUTH); wrapper.setMaximumSize(new Dimension(Integer.MAX_VALUE, 42)); parent.add(wrapper);
    }

    private static String safe(String value) { return value == null || value.isBlank() ? "Unavailable" : value; }

    private void addInfoRow(JPanel panel, VectorIcon.Type icon, String name, JLabel value, boolean separatorBefore) {
        if (separatorBefore) {
            JSeparator separator = new JSeparator(); separator.setForeground(BORDER); separator.setMaximumSize(new Dimension(Integer.MAX_VALUE, 1)); panel.add(separator);
        }
        JPanel row = new JPanel(new BorderLayout(8, 0)); row.setOpaque(false); row.setBorder(new EmptyBorder(2, 3, 2, 3));
        row.add(iconLabel(icon, 16, PRIMARY), BorderLayout.WEST); row.add(label(name, 13, TEXT, false), BorderLayout.CENTER);
        value.setHorizontalAlignment(SwingConstants.RIGHT); value.setFont(new Font("Segoe UI", Font.BOLD, 13)); row.add(value, BorderLayout.EAST); row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 24)); panel.add(row);
    }

    private JPanel ringColumn(MetricRing ring, VectorIcon.Type icon, String title) { JPanel panel = new JPanel(new BorderLayout(0, 3)); panel.setOpaque(false); panel.add(ring, BorderLayout.CENTER); JPanel caption = new JPanel(new FlowLayout(FlowLayout.CENTER, 4, 0)); caption.setOpaque(false); caption.add(iconLabel(icon, 15, TEXT)); caption.add(label(title, 13, TEXT, true)); panel.add(caption, BorderLayout.SOUTH); return panel; }

    private JPanel sectionHeading(VectorIcon.Type icon, String text, Color color) { JPanel panel = new JPanel(new BorderLayout(10, 0)); panel.setOpaque(false); panel.setBorder(new EmptyBorder(1, 2, 0, 2)); panel.add(iconLabel(icon, 26, color), BorderLayout.WEST); panel.add(label(text, 19, TEXT, true), BorderLayout.CENTER); if (text.equals("Network Traffic")) { JLabel realtime = label("Real-time", 12, SECONDARY, true); realtime.setOpaque(true); realtime.setBackground(CARD); realtime.setBorder(new RoundedLineBorder(BORDER, 8)); realtime.setHorizontalAlignment(SwingConstants.CENTER); realtime.setPreferredSize(new Dimension(82, 30)); panel.add(realtime, BorderLayout.EAST); } return panel; }
    private JPanel coloredLegend(String prefix, JLabel value, Color color) { JPanel panel = new JPanel(new FlowLayout(FlowLayout.CENTER, 4, 0)); panel.setOpaque(false); panel.add(iconLabel(VectorIcon.Type.DOT, 11, color)); panel.add(label(prefix, 13, TEXT, false)); panel.add(value); return panel; }
    private JPanel vertical(String title, JLabel state, JLabel detail) { JPanel body = new JPanel(); body.setOpaque(false); body.setLayout(new BoxLayout(body, BoxLayout.Y_AXIS)); body.add(label(title, 13, TEXT, true)); body.add(Box.createVerticalStrut(2)); body.add(state); body.add(detail); return body; }
    private RoundedPanel card(Color fill) { RoundedPanel panel = new RoundedPanel(fill); panel.setBorder(new EmptyBorder(10, 14, 10, 14)); return panel; }
    private static JLabel label(String text, int size, Color color, boolean bold) { JLabel result = new JLabel(text); result.setFont(new Font("Segoe UI", bold ? Font.BOLD : Font.PLAIN, size)); result.setForeground(color); return result; }
    private static JLabel iconLabel(VectorIcon.Type type, int size, Color color) { JLabel result = new JLabel(new VectorIcon(type, size, color)); result.setPreferredSize(new Dimension(size, size)); result.setMinimumSize(new Dimension(size, size)); return result; }

    private enum ButtonVariant { PRIMARY, SECONDARY, DANGER, GHOST }

    private static JButton actionButton(String text, ButtonVariant variant, VectorIcon.Type icon) {
        return new StyledButton(text, icon, variant, 16, 0);
    }

    private static JButton sendButton() {
        StyledButton button = new StyledButton("Send", VectorIcon.Type.SEND, ButtonVariant.PRIMARY, 18, 88);
        button.setToolTipText("Send message");
        button.getAccessibleContext().setAccessibleDescription("Send message");
        return button;
    }

    private static final class StyledButton extends JButton {
        private final ButtonVariant variant;
        private final VectorIcon.Type iconType;
        private final int iconSize;
        private final int fixedWidth;
        private boolean sidebarMode;
        private boolean active;

        private StyledButton(String text, VectorIcon.Type iconType, ButtonVariant variant) {
            this(text, iconType, variant, 20, 0);
        }

        private StyledButton(String text, VectorIcon.Type iconType, ButtonVariant variant, int iconSize, int fixedWidth) {
            super(text);
            this.variant = variant;
            this.iconType = iconType;
            this.iconSize = iconSize;
            this.fixedWidth = fixedWidth;
            setFont(new Font("Segoe UI", Font.BOLD, 12));
            setFocusPainted(false);
            setBorderPainted(false);
            setContentAreaFilled(false);
            setOpaque(false);
            setRolloverEnabled(true);
            setBorder(new EmptyBorder(0, 12, 0, 12));
            setMargin(new Insets(0, 12, 0, 12));
            refreshIcon();
        }

        private void setSidebarMode(boolean sidebarMode) { this.sidebarMode = sidebarMode; refreshIcon(); repaint(); }
        private void setActive(boolean active) { this.active = active; if (sidebarMode) setFont(new Font("Segoe UI", active ? Font.BOLD : Font.PLAIN, 15)); refreshIcon(); repaint(); }

        @Override public void setEnabled(boolean enabled) { super.setEnabled(enabled); refreshIcon(); }

        @Override public Dimension getPreferredSize() {
            Dimension base = super.getPreferredSize();
            int height = sidebarMode ? 42 : Math.max(36, base.height);
            int width = fixedWidth > 0 ? fixedWidth : base.width;
            return new Dimension(Math.max(width, sidebarMode ? 190 : 34), height);
        }

        @Override protected void paintComponent(Graphics graphics) {
            Graphics2D g = (Graphics2D) graphics.create();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            int width = getWidth() - 1, height = getHeight() - 1;
            Color fill = fillColor();
            if (fill != null) { g.setColor(fill); g.fillRoundRect(0, 0, width, height, 8, 8); }
            Color border = borderColor();
            if (border != null) { g.setColor(border); g.drawRoundRect(0, 0, width, height, 8, 8); }
            if (isFocusOwner()) { g.setColor(new Color(PRIMARY.getRed(), PRIMARY.getGreen(), PRIMARY.getBlue(), 110)); g.drawRoundRect(2, 2, width - 4, height - 4, 7, 7); }
            Color original = getForeground(); setForeground(foregroundColor()); super.paintComponent(graphics); setForeground(original); g.dispose();
        }

        private Color fillColor() {
            if (!isEnabled()) return sidebarMode ? new Color(6, 43, 77) : new Color(248, 250, 252);
            if (sidebarMode) return active ? PRIMARY : getModel().isRollover() ? SIDEBAR_HOVER : SIDEBAR;
            return switch (variant) {
                case PRIMARY -> getModel().isPressed() ? PRIMARY_DARK : getModel().isRollover() ? new Color(0, 101, 220) : PRIMARY;
                case SECONDARY -> getModel().isPressed() ? new Color(241, 245, 249) : getModel().isRollover() ? new Color(248, 250, 252) : CARD;
                case DANGER -> getModel().isPressed() ? new Color(254, 215, 215) : getModel().isRollover() ? new Color(254, 226, 226) : new Color(254, 242, 242);
                case GHOST -> getModel().isRollover() ? new Color(241, 245, 249) : null;
            };
        }

        private Color borderColor() {
            if (sidebarMode || !isEnabled()) return sidebarMode ? null : BORDER;
            return switch (variant) {
                case PRIMARY, GHOST -> null;
                case SECONDARY -> BORDER;
                case DANGER -> new Color(252, 165, 165);
            };
        }

        private Color foregroundColor() {
            if (!isEnabled()) return SECONDARY;
            if (sidebarMode) return active ? Color.WHITE : new Color(190, 207, 231);
            if (variant == ButtonVariant.PRIMARY) return Color.WHITE;
            return variant == ButtonVariant.DANGER ? DANGER : TEXT;
        }

        private void refreshIcon() {
            if (iconType == null) { setIcon(null); setDisabledIcon(null); return; }
            Color color = sidebarMode ? (active ? Color.WHITE : new Color(190, 207, 231)) : variant == ButtonVariant.PRIMARY ? Color.WHITE : variant == ButtonVariant.DANGER ? DANGER : TEXT;
            setIcon(new VectorIcon(iconType, iconSize, color));
            setDisabledIcon(new VectorIcon(iconType, iconSize, SECONDARY));
        }
    }

    private static final class RoundedLineBorder extends javax.swing.border.AbstractBorder {
        private final Color color;
        private final int radius;
        private RoundedLineBorder(Color color, int radius) { this.color = color; this.radius = radius; }
        @Override public Insets getBorderInsets(Component component) { return new Insets(1, 12, 1, 12); }
        @Override public void paintBorder(Component component, Graphics graphics, int x, int y, int width, int height) {
            Graphics2D g = (Graphics2D) graphics.create(); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); g.setColor(color); g.drawRoundRect(x, y, width - 1, height - 1, radius, radius); g.dispose();
        }
    }

    private void sampleTelemetry() { try { ClientTelemetrySampler.TelemetrySnapshot value = telemetry.sample(); SwingUtilities.invokeLater(() -> applyTelemetry(value)); } catch (RuntimeException error) { System.getLogger(getClass().getName()).log(System.Logger.Level.DEBUG, "Client telemetry sample failed", error); onEdt(() -> applyNetworkValues(0, 0)); } }
    private void applyTelemetry(ClientTelemetrySampler.TelemetrySnapshot value) { if (!SwingUtilities.isEventDispatchThread()) { SwingUtilities.invokeLater(() -> applyTelemetry(value)); return; } double cpu = clamp(value.cpuPercent()), ram = clamp(value.ramPercent()), disk = clamp(value.diskPercent()); applyNetworkValues(value.downloadMbps(), value.uploadMbps()); cpuRing.setValue(cpu); ramRing.setValue(ram); diskRing.setValue(disk); performanceChart.addPoint(cpu, ram); machineName.setText(value.machineName()); osName.setText(value.operatingSystem()); cpuModel.setText(value.cpuModel()); memory.setText(formatMemory(value.usedMemoryBytes(), value.totalMemoryBytes())); resolution.setText(value.screenResolution()); ipAddress.setText(value.ipAddress()); macAddress.setText(value.macAddress()); topClock.setText(TIME.format(value.sampledAt())); topDate.setText(DATE.format(value.sampledAt())); refreshSettingsPage(); }
    private void applyNetworkValues(double downloadMbps, double uploadMbps) { double safeDownload = nonNegative(downloadMbps), safeUpload = nonNegative(uploadMbps); downloadValue.setText(String.format(Locale.ROOT, "%.2f Mbps", safeDownload)); uploadValue.setText(String.format(Locale.ROOT, "%.2f Mbps", safeUpload)); networkChart.addPoint(safeDownload, safeUpload); }
    private static String formatMemory(long used, long total) { return total <= 0 ? "Unavailable" : String.format(Locale.ROOT, "%.1f / %.1f GB", used / 1_073_741_824.0, total / 1_073_741_824.0); }
    private static double clamp(double value) { return !Double.isFinite(value) ? 0 : Math.max(0, Math.min(100, value)); }
    private static double nonNegative(double value) { return !Double.isFinite(value) ? 0 : Math.max(0, value); }

    private void logEvent(String message) {
        if (message == null || message.isBlank()) return;
        onEdt(() -> {
            logModel.addElement(LOG_TIME.format(Instant.now()) + "  " + message);
            while (logModel.size() > 250) logModel.remove(0);
            logEmptyLabel.setVisible(logModel.isEmpty());
            if (logList != null && logModel.size() > 0) logList.ensureIndexIsVisible(logModel.size() - 1);
        });
    }

    @Override public CompletionStage<Boolean> requestConsent(String adminName) { return confirm("Screen sharing request", "Admin is requesting to view your screen. Capturing starts only after you accept.", "Accept"); }
    @Override public CompletionStage<Boolean> requestControlConsent(String adminName) { return confirm("Remote control request", "Admin requests mouse and keyboard control. You can revoke access at any time.", "Allow Control"); }
    private CompletionStage<Boolean> confirm(String title, String prompt, String accept) { if (GraphicsEnvironment.isHeadless()) return CompletableFuture.completedFuture(false); CompletableFuture<Boolean> result = new CompletableFuture<>(); SwingUtilities.invokeLater(() -> { frame.setVisible(true); frame.setExtendedState(JFrame.NORMAL); frame.toFront(); Object[] options = {accept, "Reject"}; int choice = JOptionPane.showOptionDialog(frame, prompt, title, JOptionPane.YES_NO_OPTION, JOptionPane.WARNING_MESSAGE, null, options, options[1]); result.complete(choice == 0); }); return result; }

    @Override public void sharingStarted(Runnable action) { stopAction = Objects.requireNonNull(action); activelySharing = true; logEvent("Screen sharing started"); onEdt(() -> { sharingState.setText("Active"); sharingState.setForeground(SUCCESS); sharingDetail.setText("Screen is being viewed by Admin"); stop.setEnabled(true); stop.setVisible(connectedToAdmin); frame.setVisible(true); }); }
    @Override public void sharingStopped(String reason) {
        stopAction = () -> { };
        activelySharing = false;
        logEvent("Screen sharing stopped" + (reason == null || reason.isBlank() ? "" : ": " + reason));
        revokeRemoteControlAfterSharingStops();
        onEdt(() -> { sharingState.setText("Inactive"); sharingState.setForeground(SECONDARY); sharingDetail.setText(reason == null || reason.isBlank() ? "Screen sharing is not active" : reason); stop.setEnabled(false); stop.setVisible(false); });
    }
    private void revokeRemoteControlAfterSharingStops() {
        if (!activelyControlled) return;
        Runnable action = revokeAction;
        revokeAction = () -> { };
        try { action.run(); }
        catch (RuntimeException error) { System.getLogger(getClass().getName()).log(System.Logger.Level.DEBUG, "Could not revoke remote control after screen sharing stopped.", error); }
    }
    @Override public void controlStarted(Runnable action) { revokeAction = Objects.requireNonNull(action); activelyControlled = true; logEvent("Remote control enabled"); onEdt(() -> { controlState.setText("Allowed"); controlState.setForeground(SUCCESS); controlDetail.setText("Admin is controlling this computer"); revoke.setEnabled(true); revoke.setVisible(connectedToAdmin); frame.setVisible(true); frame.toFront(); }); }
    @Override public void controlStopped(String reason) { revokeAction = () -> { }; activelyControlled = false; logEvent("Remote control stopped" + (reason == null || reason.isBlank() ? "" : ": " + reason)); onEdt(() -> { controlState.setText("Not allowed"); controlState.setForeground(SECONDARY); controlDetail.setText(reason == null || reason.isBlank() ? "No remote control access" : reason); revoke.setEnabled(false); revoke.setVisible(false); }); }
    @Override public void connectionChanged(boolean connected) { boolean changed = connectedToAdmin != connected; connectedToAdmin = connected; if (changed) logEvent(connected ? "Connected to Admin" : "Disconnected from Admin"); onEdt(() -> { connectionLabel.setText(connected ? "Connected to Admin" : "Disconnected from Admin"); sidebarConnection.setText(connected ? "Connected to Admin" : "Disconnected from Admin"); sidebarConnection.setIcon(new VectorIcon(VectorIcon.Type.DOT, 11, connected ? SUCCESS : DANGER)); connectionPill.setText(connected ? "Connected" : "Disconnected"); connectionPill.setIcon(new VectorIcon(VectorIcon.Type.DOT, 10, connected ? SUCCESS : DANGER)); connectionPill.setForeground(connected ? SUCCESS : DANGER); clientState.setText(connected ? "Online" : "Offline"); clientState.setForeground(connected ? SUCCESS : DANGER); clientDetail.setText(connected ? "Connected to Admin" : "Waiting for connection to Admin"); stop.setVisible(connected && activelySharing); revoke.setVisible(connected && activelyControlled); if (!connected) cancelRemoteAdministration.setVisible(false); refreshSettingsPage(); }); }
    @Override public void connectionFailed(String reason) { connectedToAdmin = false; logEvent("Connection retry" + (reason == null || reason.isBlank() ? "" : ": " + reason)); onEdt(() -> { connectionPill.setText("Reconnecting"); connectionPill.setIcon(new VectorIcon(VectorIcon.Type.DOT, 10, WARNING)); connectionPill.setForeground(WARNING); connectionLabel.setText("Retrying connection"); clientDetail.setText(reason == null ? "Retrying connection" : reason); stop.setVisible(false); revoke.setVisible(false); cancelRemoteAdministration.setVisible(false); refreshSettingsPage(); }); }
    @Override public void clientIdentity(String name, String clientId) { clientDisplayName = name == null || name.isBlank() ? "NetworkAdmin Client" : name; onEdt(this::refreshSettingsPage); }
    @Override public void sessionChanged(String sessionId) { onEdt(() -> { String value = sessionId == null || sessionId.isBlank() ? "—" : sessionId; sessionValue.setText(value); topSession.setText(value); sidebarSession.setText("<html><body style='width:190px'>Session: " + value + "</body></html>"); sidebarSession.setToolTipText("Session: " + value); sessionDetail.setText(value.equals("—") ? "No active connection session" : "Active"); }); }

    public void setChatSender(Consumer<String> action) { chatSender = Objects.requireNonNull(action); }
    public void setDisconnectAction(Runnable action) { disconnectAction = Objects.requireNonNull(action); }
    public void chatReceived(ClientChatUseCases.ChatMessage message) {
        if (message == null) return;
        boolean fromAdmin = "Admin".equalsIgnoreCase(message.sender());
        String participant = fromAdmin ? "Admin" : clientDisplayName;
        ClientChatUseCases.ChatMessage privateMessage = new ClientChatUseCases.ChatMessage(message.messageId(), participant, message.text(), message.sentAt());
        logEvent("Chat message " + (fromAdmin ? "received from Admin" : "sent by " + clientDisplayName));
        onEdt(() -> { chatTranscript.addMessage(privateMessage); chatPageTranscript.addMessage(privateMessage); });
    }
    private void sendChat(JTextField source) { String value = source.getText(); if (value == null || value.isBlank()) return; source.setText(""); chatWorkers.execute(() -> { try { chatSender.accept(value.strip()); logEvent("Chat message sent to Admin"); } catch (RuntimeException error) { logEvent("Chat message failed" + (error.getMessage() == null ? "" : ": " + error.getMessage())); onEdt(() -> JOptionPane.showMessageDialog(frame, error.getMessage(), "Chat error", JOptionPane.WARNING_MESSAGE)); } }); }
    @Override public void close() {
        Runnable disconnect = disconnectAction;
        disconnectAction = () -> { };
        try { disconnect.run(); }
        catch (RuntimeException error) { System.getLogger(getClass().getName()).log(System.Logger.Level.DEBUG, "Could not close the Client connection cleanly.", error); }
        connectedToAdmin = false;
        if (telemetryWorkers != null) telemetryWorkers.shutdownNow();
        if (chatWorkers != null) chatWorkers.shutdownNow();
        onEdt(() -> {
            if (remoteCountdown != null) { remoteCountdown.stop(); remoteCountdown = null; }
            cancelRemoteAction = () -> { };
            cancelRemoteAdministration.setEnabled(false);
            stop.setVisible(false);
            revoke.setVisible(false);
            cancelRemoteAdministration.setVisible(false);
            if (frame != null) frame.dispose();
        });
    }
    private static void onEdt(Runnable action) { if (GraphicsEnvironment.isHeadless()) return; if (SwingUtilities.isEventDispatchThread()) action.run(); else SwingUtilities.invokeLater(action); }

    private static final class RoundedPanel extends JPanel { private final Color fill; private final Color border; private RoundedPanel(Color fill) { this(fill, BORDER); } private RoundedPanel(Color fill, Color border) { this.fill = fill; this.border = border; setOpaque(false); } @Override protected void paintComponent(Graphics graphics) { Graphics2D g = (Graphics2D) graphics.create(); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); g.setColor(fill); g.fillRoundRect(0, 0, getWidth() - 1, getHeight() - 1, 12, 12); g.setColor(border); g.drawRoundRect(0, 0, getWidth() - 1, getHeight() - 1, 12, 12); g.dispose(); super.paintComponent(graphics); } }
    private static final class ResponsiveGrid extends JPanel {
        private final int columns, minCellWidth, rowHeight, gap;
        private ResponsiveGrid(int columns, int minCellWidth, int rowHeight, int gap) { this.columns = columns; this.minCellWidth = minCellWidth; this.rowHeight = rowHeight; this.gap = gap; setOpaque(false); setLayout(null); setAlignmentX(Component.LEFT_ALIGNMENT); setPreferredSize(new Dimension(columns * minCellWidth + (columns - 1) * gap, rowHeight)); setMinimumSize(new Dimension(minCellWidth, rowHeight)); setMaximumSize(new Dimension(Integer.MAX_VALUE, rowHeight * 3 + gap * 2)); }
        @Override public void doLayout() { int width = getWidth(); int height = getHeight(); int actualColumns = Math.max(1, Math.min(columns, (width + gap) / (minCellWidth + gap))); int cellWidth = Math.max(1, (width - (actualColumns - 1) * gap) / actualColumns); int rows = (getComponentCount() + actualColumns - 1) / actualColumns; int actualRowHeight = Math.max(1, (height - Math.max(0, rows - 1) * gap) / Math.max(1, rows)); for (int i = 0; i < getComponentCount(); i++) { int row = i / actualColumns, column = i % actualColumns; getComponent(i).setBounds(column * (cellWidth + gap), row * (actualRowHeight + gap), cellWidth, actualRowHeight); } }
    }
    private static final class MetricRing extends JComponent { private final Color color; private double value; private MetricRing(Color color) { this.color = color; setPreferredSize(new Dimension(94, 88)); } void setValue(double value) { this.value = clamp(value); repaint(); } @Override protected void paintComponent(Graphics graphics) { Graphics2D g = (Graphics2D) graphics.create(); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); int size = Math.min(getWidth(), getHeight()) - 18, x = (getWidth() - size) / 2, y = 2; g.setStroke(new BasicStroke(7, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND)); g.setColor(new Color(226, 232, 240)); g.drawArc(x, y, size, size, 90, -359); g.setColor(color); g.drawArc(x, y, size, size, 90, -(int) Math.round(value * 3.59)); g.setFont(new Font("Segoe UI", Font.BOLD, 18)); g.setColor(TEXT); String text = String.format(Locale.ROOT, "%.0f%%", value); FontMetrics metrics = g.getFontMetrics(); g.drawString(text, (getWidth() - metrics.stringWidth(text)) / 2, y + size / 2 + metrics.getAscent() / 3); g.dispose(); } }
    private static final class LineChartPanel extends JPanel {
        private final Color firstColor, secondColor;
        private final boolean percentage;
        private final String emptyMessage;
        private final NetworkTrafficHistory history;

        private LineChartPanel(Color firstColor, Color secondColor, boolean percentage, int maxPoints, String emptyMessage) {
            this.firstColor = firstColor;
            this.secondColor = secondColor;
            this.percentage = percentage;
            this.emptyMessage = emptyMessage;
            this.history = new NetworkTrafficHistory(maxPoints);
            setOpaque(false);
            setPreferredSize(new Dimension(0, 190));
            setToolTipText("");
        }

        void addPoint(double first, double second) {
            history.add(first, second);
            repaint();
        }

        @Override public String getToolTipText(MouseEvent event) {
            if (history.size() == 0) return null;
            var values = history.snapshot();
            int left = 38;
            int width = Math.max(1, getWidth() - left - 8);
            int index = Math.min(values.size() - 1, Math.max(0,
                    Math.round((event.getX() - left) * (values.size() - 1f) / width)));
            NetworkTrafficHistory.Sample value = values.get(index);
            return String.format(Locale.ROOT, "Download: %.2f | Upload: %.2f %s",
                    value.downloadMbps(), value.uploadMbps(), percentage ? "%" : "Mbps");
        }

        @Override protected void paintComponent(Graphics graphics) {
            super.paintComponent(graphics);
            Graphics2D g = (Graphics2D) graphics.create();
            g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
            int left = 38, top = 10, right = 8, bottom = 28;
            int width = Math.max(1, getWidth() - left - right);
            int height = Math.max(1, getHeight() - top - bottom);
            double max = percentage ? 100 : scaleMax();
            g.setFont(new Font("Segoe UI", Font.PLAIN, 10));
            for (int i = 0; i <= 5; i++) {
                int y = top + height - i * height / 5;
                g.setColor(new Color(227, 232, 240));
                g.drawLine(left, y, left + width, y);
                g.setColor(SECONDARY);
                g.drawString(formatScale(max * i / 5), 0, y + 4);
            }
            for (int i = 0; i <= 12; i++) {
                int x = left + i * width / 12;
                g.setColor(new Color(239, 243, 248));
                g.drawLine(x, top, x, top + height);
            }
            var values = history.snapshot();
            if (values.size() < 2) {
                g.setColor(SECONDARY);
                g.drawString(emptyMessage, left + 14, top + height / 2);
            } else {
                drawSeries(g, left, top, width, height, 0, firstColor, max);
                drawSeries(g, left, top, width, height, 1, secondColor, max);
            }
            g.setColor(SECONDARY);
            g.drawString("-60s", left, getHeight() - 6);
            g.drawString("Now", left + width - 24, getHeight() - 6);
            g.dispose();
        }

        private double scaleMax() {
            var values = history.snapshot();
            double max = values.stream()
                    .flatMapToDouble(value -> java.util.stream.DoubleStream.of(value.downloadMbps(), value.uploadMbps()))
                    .max().orElse(0);
            return Math.max(1, Math.ceil(max * 1.2));
        }

        private void drawSeries(Graphics2D g, int left, int top, int width, int height, int index, Color color, double max) {
            var points = history.snapshot();
            if (points.isEmpty()) return;
            Path2D line = new Path2D.Double();
            Path2D area = new Path2D.Double();
            int firstX = left;
            int firstY = yFor(valueAt(points.get(0), index), top, height, max);
            line.moveTo(firstX, firstY);
            area.moveTo(firstX, top + height);
            area.lineTo(firstX, firstY);
            for (int i = 1; i < points.size(); i++) {
                int x = left + i * width / (points.size() - 1);
                int y = yFor(valueAt(points.get(i), index), top, height, max);
                line.lineTo(x, y);
                area.lineTo(x, y);
            }
            int lastX = left + width;
            area.lineTo(lastX, top + height);
            area.closePath();
            g.setPaint(new GradientPaint(0, top, new Color(color.getRed(), color.getGreen(), color.getBlue(), 42),
                    0, top + height, new Color(color.getRed(), color.getGreen(), color.getBlue(), 0)));
            g.fill(area);
            g.setColor(color);
            g.setStroke(new BasicStroke(2.1f, BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
            g.draw(line);
            g.setColor(color);
            for (int i = 0; i < points.size(); i++) {
                int x = left + (points.size() == 1 ? width : i * width / (points.size() - 1));
                int y = yFor(valueAt(points.get(i), index), top, height, max);
                g.fillOval(x - 3, y - 3, 6, 6);
            }
        }

        private static int yFor(double value, int top, int height, double max) {
            return top + height - (int) Math.round(Math.min(max, nonNegative(value)) * height / max);
        }

        private static double valueAt(NetworkTrafficHistory.Sample value, int index) {
            return index == 0 ? value.downloadMbps() : value.uploadMbps();
        }

        private String formatScale(double value) { return percentage ? String.format(Locale.ROOT, "%.0f%%", value) : String.format(Locale.ROOT, "%.0f", value); }
    }
    private static final class HintTextField extends JTextField { private final String hint; private HintTextField(String hint) { this.hint = hint; } @Override protected void paintComponent(Graphics graphics) { super.paintComponent(graphics); if (getText().isEmpty() && !isFocusOwner()) { Graphics2D g = (Graphics2D) graphics.create(); g.setColor(SECONDARY); g.setFont(getFont()); g.drawString(hint, getInsets().left, getHeight() / 2 + g.getFontMetrics().getAscent() / 2 - 2); g.dispose(); } } }
    private static final class ChatTranscript extends JPanel {
        private final JPanel rows = new JPanel(); private final JLabel empty = label("No messages yet", 13, SECONDARY, false);
        private ChatTranscript() { setLayout(new BorderLayout()); setBackground(CARD); rows.setOpaque(false); rows.setLayout(new BoxLayout(rows, BoxLayout.Y_AXIS)); add(empty, BorderLayout.CENTER); add(rows, BorderLayout.NORTH); }
        void addMessage(ClientChatUseCases.ChatMessage message) {
            empty.setVisible(false);
            if (empty.getParent() == this) remove(empty);
            String senderName = message.sender() == null || message.sender().isBlank() ? "Unknown" : message.sender();
            boolean outgoing = !"Admin".equalsIgnoreCase(senderName);
            JPanel line = new JPanel(new FlowLayout(outgoing ? FlowLayout.RIGHT : FlowLayout.LEFT, 4, 5));
            line.setOpaque(false);
            JPanel messageBlock = new JPanel(new BorderLayout(0, 2));
            messageBlock.setOpaque(false);
            Bubble bubble = new Bubble(outgoing ? new Color(214, 231, 255) : new Color(241, 243, 248));
            bubble.setLayout(new BoxLayout(bubble, BoxLayout.Y_AXIS));
            JTextArea text = new JTextArea(message.text() == null ? "" : message.text());
            text.setFont(new Font("Segoe UI", Font.PLAIN, 13));
            text.setForeground(TEXT);
            text.setOpaque(false);
            text.setEditable(false);
            text.setFocusable(false);
            text.setLineWrap(true);
            text.setWrapStyleWord(true);
            text.setBorder(new EmptyBorder(2, 4, 2, 4));
            FontMetrics messageMetrics = text.getFontMetrics(text.getFont());
            int messageWidth = Math.min(360, Math.max(48, messageMetrics.stringWidth(longestLine(message.text())) + 16));
            text.setPreferredSize(new Dimension(messageWidth, wrappedMessageHeight(message.text(), messageMetrics, messageWidth - 8)));
            JLabel stamp = label(TIME.format(message.sentAt()) + (outgoing ? " · Sent" : ""), 10, SECONDARY, false);
            stamp.setAlignmentX(outgoing ? RIGHT_ALIGNMENT : LEFT_ALIGNMENT);
            bubble.add(text);
            bubble.add(stamp);
            JLabel sender = label(senderName, 11, SECONDARY, true);
            sender.setHorizontalAlignment(outgoing ? SwingConstants.RIGHT : SwingConstants.LEFT);
            messageBlock.add(sender, BorderLayout.NORTH);
            messageBlock.add(bubble, BorderLayout.CENTER);
            line.add(messageBlock);
            rows.add(line);
            revalidate();
            rows.revalidate();
            rows.repaint();
            JScrollPane scroll = (JScrollPane) SwingUtilities.getAncestorOfClass(JScrollPane.class, this);
            if (scroll != null) SwingUtilities.invokeLater(() -> scroll.getVerticalScrollBar().setValue(scroll.getVerticalScrollBar().getMaximum()));
        }
        private static String longestLine(String value) { if (value == null || value.isEmpty()) return ""; String longest = ""; for (String line : value.split("\\R", -1)) if (line.length() > longest.length()) longest = line; return longest; }
        private static int wrappedMessageHeight(String value, FontMetrics metrics, int width) { int lines = 0; for (String line : (value == null ? "" : value).split("\\R", -1)) lines += Math.max(1, (int) Math.ceil(metrics.stringWidth(line) / (double) Math.max(1, width))); return lines * metrics.getHeight() + 4; }
        private static String escapeHtml(String value) { return value.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;").replace("\n", "<br>"); }
    }
    private static final class Bubble extends JPanel { private final Color fill; private Bubble(Color fill) { this.fill = fill; setOpaque(false); setBorder(new EmptyBorder(6, 10, 6, 10)); } @Override protected void paintComponent(Graphics graphics) { Graphics2D g = (Graphics2D) graphics.create(); g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON); g.setColor(fill); g.fillRoundRect(0, 0, getWidth() - 1, getHeight() - 1, 15, 15); g.dispose(); super.paintComponent(graphics); } }
}
