package vn.networkadmin.admin.presentation.dashboard;

import vn.networkadmin.admin.runtime.AdminClientSnapshot;
import vn.networkadmin.admin.application.screenshare.AdminScreenShareUseCases;
import vn.networkadmin.admin.presentation.screenshare.ScreenViewerFrame;
import vn.networkadmin.admin.application.remotecontrol.AdminRemoteControlUseCases;
import vn.networkadmin.admin.presentation.chat.AdminChatDialog;
import vn.networkadmin.admin.presentation.chat.AdminChatPresenter;
import vn.networkadmin.admin.presentation.components.MetricRing;
import vn.networkadmin.admin.presentation.theme.AdminTheme;

import javax.swing.*;
import javax.swing.border.Border;
import javax.swing.table.DefaultTableModel;
import java.awt.*;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.List;
import java.util.Map;
import java.util.concurrent.CompletionException;

/** Swing view: renders presenter state and forwards user actions. */
public final class AdminDashboardFrame extends JFrame {
    private final AdminDashboardPresenter presenter;
    private final AdminScreenShareUseCases screenShare;
    private final AdminRemoteControlUseCases remoteControl;
    private final AdminChatPresenter chatPresenter;
    private final Runnable closeAction;
    private final JComboBox<ClientChoice> clientPicker = new JComboBox<>();
    private final JLabel serverState = label("Starting", AdminTheme.GREEN, AdminTheme.MEDIUM);
    private final JLabel serverAddress = label("", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel headerServerState = label("Starting", AdminTheme.GREEN, AdminTheme.MEDIUM);
    private final JLabel headerOnlineCount = label("0", AdminTheme.TEXT, AdminTheme.TITLE);
    private final JLabel clock = label("", AdminTheme.TEXT, AdminTheme.MEDIUM);
    private final JLabel date = label("", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel managedCount = label("0", AdminTheme.TEXT, AdminTheme.TITLE);
    private final JLabel onlineCount = label("0", AdminTheme.TEXT, AdminTheme.TITLE);
    private final JLabel hostsCount = label("--", AdminTheme.TEXT, AdminTheme.TITLE);
    private final JLabel pingAverage = label("--", AdminTheme.TEXT, AdminTheme.TITLE);
    private final JLabel clientName = label("No client connected", AdminTheme.TEXT, AdminTheme.TITLE);
    private final JLabel clientStatus = label("Waiting for Client connection", AdminTheme.PINK, AdminTheme.MEDIUM);
    private final JLabel clientId = label("--", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel clientOs = label("--", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel sessionId = label("--", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel connectedAt = label("--", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel lastSeen = label("--", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel cpuValue = label("0.0%", AdminTheme.BLUE, AdminTheme.MEDIUM);
    private final JLabel ramValue = label("0.0%", AdminTheme.GREEN, AdminTheme.MEDIUM);
    private final JLabel pingSummary = label("No ping completed for this session.", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JLabel operationStatus = label("Ready", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JTextField subnetField = field("Subnet (blank = Client local network)");
    private final JTextField targetField = field("Target IP or hostname");
    private final JButton scanButton = actionButton("Scan Now");
    private final JButton cancelScanButton = actionButton("Cancel");
    private final JLabel scanProgress = label("No scan completed for this session.", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final JButton pingButton = actionButton("Ping (4 requests)");
    private final JButton viewScreenButton = actionButton("View Screen");
    private final JButton chatButton = actionButton("Chat");
    private final JLabel screenShareStatus = label("Screen sharing: Stopped", AdminTheme.MUTED, AdminTheme.REGULAR);
    private final MetricRing cpuRing = new MetricRing("CPU Usage", AdminTheme.BLUE);
    private final MetricRing ramRing = new MetricRing("RAM Usage", AdminTheme.GREEN);
    private final DefaultTableModel interfacesModel = model("Interface", "IPv4", "MAC", "Gateway", "Subnet");
    private final DefaultTableModel scanModel = model("IP Address", "MAC Address", "Status", "Response");
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

    public AdminDashboardFrame(AdminDashboardPresenter presenter, AdminScreenShareUseCases screenShare,
                               AdminRemoteControlUseCases remoteControl, AdminChatPresenter chatPresenter,
                               String bindDescription, Runnable closeAction) {
        super("Network Administration — Admin Server");
        this.presenter = java.util.Objects.requireNonNull(presenter);
        this.screenShare = java.util.Objects.requireNonNull(screenShare);
        this.remoteControl = java.util.Objects.requireNonNull(remoteControl);
        this.chatPresenter = java.util.Objects.requireNonNull(chatPresenter);
        this.bindDescription = bindDescription;
        this.closeAction = java.util.Objects.requireNonNull(closeAction);
        setDefaultCloseOperation(WindowConstants.DO_NOTHING_ON_CLOSE);
        setMinimumSize(new Dimension(1180, 760)); setSize(1580, 980); setLocationRelativeTo(null);
        setContentPane(buildRoot());
        clientPicker.addActionListener(e -> refreshSelectedClient());
        scanButton.addActionListener(e -> runScan()); pingButton.addActionListener(e -> runPing());
        cancelScanButton.setEnabled(false); cancelScanButton.addActionListener(e -> cancelScan());
        viewScreenButton.addActionListener(e -> viewScreen());
        chatButton.addActionListener(e -> openChat());
        addWindowListener(new java.awt.event.WindowAdapter() {
            @Override public void windowClosing(java.awt.event.WindowEvent e) { closeApp(); }
        });
        refreshTimer = new javax.swing.Timer(1000, e -> refreshDashboard()); refreshTimer.start();
        refreshDashboard();
    }

    private JComponent buildRoot() {
        JPanel root = new JPanel(new BorderLayout()); root.setBackground(AdminTheme.BACKGROUND);
        root.add(buildSidebar(), BorderLayout.WEST);
        JPanel main = new JPanel(new BorderLayout()); main.setBackground(AdminTheme.BACKGROUND);
        main.add(buildHeader(), BorderLayout.NORTH);
        JPanel scrollContent = new JPanel(); scrollContent.setBackground(AdminTheme.BACKGROUND);
        scrollContent.setBorder(BorderFactory.createEmptyBorder(8, 18, 16, 18));
        scrollContent.setLayout(new BoxLayout(scrollContent, BoxLayout.Y_AXIS));
        scrollContent.add(buildSelectionBar()); scrollContent.add(Box.createVerticalStrut(12));
        scrollContent.add(buildSummaryCards()); scrollContent.add(Box.createVerticalStrut(12));
        scrollContent.add(buildMainRow()); scrollContent.add(Box.createVerticalStrut(12));
        scrollContent.add(buildOperationsRow());
        JScrollPane scroll = new JScrollPane(scrollContent);
        scroll.setBorder(BorderFactory.createEmptyBorder()); scroll.getViewport().setBackground(AdminTheme.BACKGROUND);
        scroll.getVerticalScrollBar().setUnitIncrement(18);
        main.add(scroll, BorderLayout.CENTER);
        main.add(buildFooter(), BorderLayout.SOUTH);
        root.add(main, BorderLayout.CENTER);
        return root;
    }

    private JComponent buildSidebar() {
        JPanel side = new JPanel(new BorderLayout()); side.setBackground(AdminTheme.SIDEBAR);
        side.setPreferredSize(new Dimension(222, 0));
        JPanel top = new JPanel(); top.setOpaque(false); top.setBorder(BorderFactory.createEmptyBorder(24, 18, 14, 18));
        top.setLayout(new BoxLayout(top, BoxLayout.Y_AXIS));
        JLabel logo = label("◎", AdminTheme.BLUE, new Font("Segoe UI Symbol", Font.PLAIN, 54)); logo.setAlignmentX(Component.CENTER_ALIGNMENT);
        JLabel brand = label("NET ADMIN", AdminTheme.BLUE, new Font("Segoe UI", Font.BOLD, 21)); brand.setAlignmentX(Component.CENTER_ALIGNMENT);
        JLabel role = label("Server", AdminTheme.MUTED, AdminTheme.REGULAR); role.setAlignmentX(Component.CENTER_ALIGNMENT);
        top.add(logo); top.add(brand); top.add(Box.createVerticalStrut(5)); top.add(role); top.add(Box.createVerticalStrut(22));
        for (String item : List.of("▦   Dashboard", "▣   Clients", "⌕   IP Scanner", "↔   Ping Tool", "∿   Monitoring", "⚙   Settings", "ⓘ   About")) {
            JButton nav = new JButton(item); nav.setHorizontalAlignment(SwingConstants.LEFT); nav.setForeground(AdminTheme.MUTED);
            nav.setBackground(AdminTheme.SIDEBAR); nav.setBorder(BorderFactory.createEmptyBorder(12, 14, 12, 8));
            nav.setFont(AdminTheme.REGULAR); nav.setFocusPainted(false); nav.setMaximumSize(new Dimension(Integer.MAX_VALUE, 44));
            if (item.contains("Dashboard")) { nav.setBackground(new Color(14, 73, 156)); nav.setForeground(Color.WHITE); }
            if (item.contains("IP Scanner")) nav.addActionListener(e -> subnetField.requestFocusInWindow());
            if (item.contains("Ping Tool")) nav.addActionListener(e -> targetField.requestFocusInWindow());
            top.add(nav);
        }
        side.add(top, BorderLayout.NORTH);
        JPanel status = card("Server Status"); status.setLayout(new BoxLayout(status, BoxLayout.Y_AXIS));
        status.add(serverState); status.add(Box.createVerticalStrut(7)); status.add(label("Listening Address", AdminTheme.MUTED, AdminTheme.REGULAR));
        JLabel listen = label(bindDescription, AdminTheme.TEXT, AdminTheme.REGULAR); listen.setToolTipText(bindDescription); status.add(listen);
        JPanel wrap = new JPanel(new BorderLayout()); wrap.setOpaque(false); wrap.setBorder(BorderFactory.createEmptyBorder(0, 12, 12, 12)); wrap.add(status);
        side.add(wrap, BorderLayout.SOUTH);
        return side;
    }

    private JComponent buildHeader() {
        JPanel header = new JPanel(new BorderLayout()); header.setBackground(AdminTheme.BACKGROUND);
        header.setBorder(BorderFactory.createEmptyBorder(18, 24, 14, 24));
        JPanel titles = new JPanel(); titles.setOpaque(false); titles.setLayout(new BoxLayout(titles, BoxLayout.Y_AXIS));
        titles.add(label("Network Administration", AdminTheme.TEXT, AdminTheme.TITLE));
        titles.add(Box.createVerticalStrut(5)); titles.add(label("Centralized LAN Monitoring & Management", AdminTheme.MUTED, AdminTheme.REGULAR));
        JPanel right = new JPanel(new GridLayout(2, 2, 20, 4)); right.setOpaque(false);
        right.add(headerServerState); right.add(label("Connected Agents", AdminTheme.MUTED, AdminTheme.REGULAR));
        right.add(serverAddress); right.add(headerOnlineCount);
        JPanel dateTime = new JPanel(); dateTime.setOpaque(false); dateTime.setLayout(new BoxLayout(dateTime, BoxLayout.Y_AXIS));
        dateTime.add(clock); dateTime.add(date);
        JPanel trailing = new JPanel(new BorderLayout(26, 0)); trailing.setOpaque(false); trailing.add(right, BorderLayout.CENTER); trailing.add(dateTime, BorderLayout.EAST);
        header.add(titles, BorderLayout.WEST); header.add(trailing, BorderLayout.EAST);
        return header;
    }

    private JComponent buildSelectionBar() {
        JPanel row = new JPanel(new BorderLayout(12, 0)); row.setOpaque(false);
        row.add(label("SELECTED AGENT", AdminTheme.MUTED, AdminTheme.MEDIUM), BorderLayout.WEST);
        clientPicker.setFont(AdminTheme.REGULAR); clientPicker.setBackground(AdminTheme.PANEL); clientPicker.setForeground(AdminTheme.TEXT);
        row.add(clientPicker, BorderLayout.CENTER);
        return row;
    }

    private JComponent buildSummaryCards() {
        JPanel row = new JPanel(new GridLayout(1, 4, 12, 0)); row.setOpaque(false);
        row.add(summaryCard("Managed Clients", managedCount, "Known agents seen by this server"));
        row.add(summaryCard("Online Clients", onlineCount, "Currently connected agents"));
        row.add(summaryCard("Hosts in Last Scan", hostsCount, "For selected client session"));
        row.add(summaryCard("Last Ping (avg)", pingAverage, "Most recent ping request"));
        row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 122)); return row;
    }

    private JComponent buildMainRow() {
        JPanel row = new JPanel(new GridLayout(1, 2, 12, 0)); row.setOpaque(false);
        JPanel client = card("Selected Client"); client.setLayout(new BorderLayout(12, 10));
        JPanel details = new JPanel(new GridLayout(7, 2, 10, 7)); details.setOpaque(false);
        details.add(clientName); details.add(clientStatus);
        details.add(label("Operating System", AdminTheme.MUTED, AdminTheme.REGULAR)); details.add(clientOs);
        details.add(label("Client ID", AdminTheme.MUTED, AdminTheme.REGULAR)); details.add(clientId);
        details.add(label("Session ID", AdminTheme.MUTED, AdminTheme.REGULAR)); details.add(sessionId);
        details.add(label("Connected", AdminTheme.MUTED, AdminTheme.REGULAR)); details.add(connectedAt);
        details.add(label("Last seen", AdminTheme.MUTED, AdminTheme.REGULAR)); details.add(lastSeen);
        JPanel featureButtons = new JPanel(new FlowLayout(FlowLayout.LEFT, 6, 0)); featureButtons.setOpaque(false);
        featureButtons.add(viewScreenButton); featureButtons.add(chatButton);
        details.add(featureButtons); details.add(screenShareStatus);
        client.add(details, BorderLayout.NORTH);
        client.add(table(interfacesModel), BorderLayout.CENTER);
        row.add(client);

        JPanel monitor = card("System Monitoring / LIVE DATA"); monitor.setLayout(new BorderLayout(0, 8));
        JLabel live = label("Waiting for telemetry", AdminTheme.MUTED, AdminTheme.REGULAR); monitor.add(live, BorderLayout.NORTH);
        JPanel rings = new JPanel(new GridLayout(1, 2, 4, 0)); rings.setOpaque(false);
        rings.add(ringWithLabel(cpuRing, cpuValue)); rings.add(ringWithLabel(ramRing, ramValue));
        monitor.add(rings, BorderLayout.CENTER);
        JLabel legend = label("CPU / blue       RAM / green", AdminTheme.MUTED, AdminTheme.REGULAR); monitor.add(legend, BorderLayout.SOUTH);
        row.add(monitor);
        row.setPreferredSize(new Dimension(1200, 318)); row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 318));
        return row;
    }

    private JComponent buildOperationsRow() {
        JPanel row = new JPanel(new GridLayout(1, 2, 12, 0)); row.setOpaque(false);
        JPanel scan = card("Recent Network Scan"); scan.setLayout(new BorderLayout(8, 8));
        JPanel scanTools = new JPanel(new BorderLayout(8, 0)); scanTools.setOpaque(false);
        scanTools.add(subnetField, BorderLayout.CENTER);
        JPanel scanActions = new JPanel(new FlowLayout(FlowLayout.RIGHT, 5, 0)); scanActions.setOpaque(false);
        scanActions.add(cancelScanButton); scanActions.add(scanButton); scanTools.add(scanActions, BorderLayout.EAST);
        JPanel scanHeader = new JPanel(new BorderLayout(0, 5)); scanHeader.setOpaque(false);
        scanHeader.add(scanTools, BorderLayout.NORTH); scanHeader.add(scanProgress, BorderLayout.SOUTH);
        scan.add(scanHeader, BorderLayout.NORTH); scan.add(table(scanModel), BorderLayout.CENTER);
        row.add(scan);
        JPanel ping = card("Recent Ping Results"); ping.setLayout(new BorderLayout(8, 8));
        JPanel pingTools = new JPanel(new BorderLayout(8, 0)); pingTools.setOpaque(false);
        pingTools.add(targetField, BorderLayout.CENTER); pingTools.add(pingButton, BorderLayout.EAST);
        JPanel pingTop = new JPanel(new BorderLayout(0, 7)); pingTop.setOpaque(false); pingTop.add(pingTools, BorderLayout.NORTH); pingTop.add(pingSummary, BorderLayout.SOUTH);
        ping.add(pingTop, BorderLayout.NORTH); ping.add(table(pingModel), BorderLayout.CENTER);
        row.add(ping);
        row.setPreferredSize(new Dimension(1200, 300)); row.setMaximumSize(new Dimension(Integer.MAX_VALUE, 300));
        return row;
    }

    private JComponent buildFooter() {
        JPanel footer = new JPanel(new BorderLayout()); footer.setBackground(AdminTheme.BACKGROUND);
        footer.setBorder(BorderFactory.createEmptyBorder(4, 24, 8, 20)); footer.add(operationStatus, BorderLayout.WEST);
        return footer;
    }

    private JComponent summaryCard(String title, JLabel value, String subtitle) {
        JPanel p = card(title); p.setLayout(new BorderLayout(0, 4)); p.add(value, BorderLayout.CENTER);
        p.add(label(subtitle, AdminTheme.MUTED, AdminTheme.REGULAR), BorderLayout.SOUTH); return p;
    }

    private static JComponent ringWithLabel(MetricRing ring, JLabel value) {
        JPanel panel = new JPanel(new BorderLayout()); panel.setOpaque(false); panel.add(ring, BorderLayout.CENTER);
        value.setHorizontalAlignment(SwingConstants.CENTER); panel.add(value, BorderLayout.SOUTH); return panel;
    }

    private JPanel card(String title) {
        JPanel p = new JPanel(); p.setBackground(AdminTheme.PANEL); p.setBorder(panelBorder(title));
        return p;
    }

    private Border panelBorder(String title) {
        Border outer = BorderFactory.createLineBorder(AdminTheme.BORDER);
        Border inner = BorderFactory.createEmptyBorder(12, 14, 14, 14);
        return BorderFactory.createCompoundBorder(outer, BorderFactory.createTitledBorder(inner, title,
                javax.swing.border.TitledBorder.LEADING, javax.swing.border.TitledBorder.TOP,
                AdminTheme.MEDIUM, new Color(185, 218, 243)));
    }

    private JComponent table(DefaultTableModel model) {
        JTable table = new JTable(model); table.setFillsViewportHeight(true); table.setRowHeight(25);
        table.setBackground(AdminTheme.PANEL); table.setForeground(AdminTheme.TEXT); table.setGridColor(AdminTheme.BORDER);
        table.setSelectionBackground(new Color(19, 74, 110)); table.setSelectionForeground(Color.WHITE);
        table.setFont(AdminTheme.REGULAR); table.getTableHeader().setBackground(AdminTheme.PANEL_ALT);
        table.getTableHeader().setForeground(AdminTheme.TEXT); table.getTableHeader().setFont(AdminTheme.MEDIUM);
        JScrollPane pane = new JScrollPane(table); pane.setBorder(BorderFactory.createLineBorder(AdminTheme.BORDER));
        pane.getViewport().setBackground(AdminTheme.PANEL); return pane;
    }

    private static DefaultTableModel model(String... columns) {
        return new DefaultTableModel(columns, 0) { @Override public boolean isCellEditable(int row, int column) { return false; } };
    }
    private static JLabel label(String text, Color color, Font font) {
        JLabel result = new JLabel(text); result.setForeground(color); result.setFont(font); return result;
    }
    private JTextField field(String placeholder) {
        JTextField result = new JTextField(); result.setToolTipText(placeholder); result.setBackground(AdminTheme.PANEL_ALT);
        result.setForeground(AdminTheme.TEXT); result.setCaretColor(Color.WHITE); result.setBorder(BorderFactory.createCompoundBorder(
                BorderFactory.createLineBorder(AdminTheme.BORDER), BorderFactory.createEmptyBorder(7, 9, 7, 9)));
        result.setFont(AdminTheme.REGULAR); return result;
    }
    private JButton actionButton(String text) {
        JButton button = new JButton(text); button.setBackground(new Color(12, 79, 170)); button.setForeground(Color.WHITE);
        button.setFocusPainted(false); button.setBorder(BorderFactory.createEmptyBorder(9, 14, 9, 14)); button.setFont(AdminTheme.MEDIUM); return button;
    }

    private void refreshDashboard() {
        presenter.refresh().whenComplete((state, error) -> SwingUtilities.invokeLater(() -> {
            if (error != null) {
                operationStatus.setText("Dashboard refresh failed: " + rootMessage(error));
                return;
            }
            applyDashboard(state);
        }));
    }

    private void applyDashboard(AdminDashboardPresenter.DashboardState state) {
        serverState.setText(state.listening() ? "●  Server Running" : "●  Starting Server");
        serverState.setForeground(state.listening() ? AdminTheme.GREEN : AdminTheme.PURPLE);
        headerServerState.setText(state.listening() ? "●  Server Running" : "●  Starting Server");
        headerServerState.setForeground(state.listening() ? AdminTheme.GREEN : AdminTheme.PURPLE);
        serverAddress.setText(bindDescription); clock.setText(java.time.ZonedDateTime.now().format(timeFormat));
        date.setText(java.time.ZonedDateTime.now().format(dateFormat));
        List<AdminClientSnapshot> clients = state.clients();
        currentClients = clients;
        long online = clients.stream().filter(AdminClientSnapshot::online).count();
        managedCount.setText(Integer.toString(clients.size())); onlineCount.setText(Long.toString(online));
        headerOnlineCount.setText(Long.toString(online));
        refreshingClients = true;
        String selected = selectedSessionId();
        clientPicker.removeAllItems();
        for (AdminClientSnapshot client : clients) clientPicker.addItem(new ClientChoice(client.sessionId(), client.clientName(), client.networkInfo(), client.online()));
        if (selected != null) for (int i = 0; i < clientPicker.getItemCount(); i++)
            if (clientPicker.getItemAt(i).sessionId.equals(selected)) { clientPicker.setSelectedIndex(i); break; }
        refreshingClients = false;
        refreshSelectedClient();
    }

    private String selectedSessionId() {
        Object selected = clientPicker.getSelectedItem(); return selected instanceof ClientChoice c ? c.sessionId : null;
    }

    private void refreshSelectedClient() {
        if (refreshingClients) return;
        ClientChoice choice = (ClientChoice) clientPicker.getSelectedItem();
        if (choice == null) { showNoClient(); return; }
        AdminClientSnapshot snapshot = currentClients.stream()
                .filter(client -> client.sessionId().equals(choice.sessionId)).findFirst().orElse(null);
        if (snapshot == null) { showNoClient(); return; }
        clientName.setText(snapshot.clientName()); clientStatus.setText(snapshot.online() ? "●  Online" : "●  Offline / stale data");
        clientStatus.setForeground(snapshot.online() ? AdminTheme.GREEN : AdminTheme.PINK);
        Map<String, Object> info = snapshot.networkInfo();
        clientOs.setText(value(info.get("osName")) + " " + value(info.get("osVersion")) + " (" + value(info.get("architecture")) + ")");
        clientId.setText(snapshot.clientId()); sessionId.setText(snapshot.sessionId());
        connectedAt.setText(format(snapshot.connectedAt())); lastSeen.setText(format(snapshot.lastSeen()));
        fillInterfaces(snapshot.networkInfo()); fillMonitoring(snapshot.systemStats()); fillLastScan(snapshot.lastScan());
        refreshScreenShareStatus(snapshot.sessionId());
        viewScreenButton.setEnabled(snapshot.online()); chatButton.setEnabled(snapshot.online());
        scanButton.setEnabled(snapshot.online()); pingButton.setEnabled(snapshot.online());
        scanButton.setEnabled(snapshot.online() && !operationsBusy); pingButton.setEnabled(snapshot.online() && !operationsBusy);
        operationStatus.setText("Streaming from " + snapshot.clientName() + "  •  Session " + snapshot.sessionId());
    }

    private void showNoClient() {
        clientName.setText("No client connected"); clientStatus.setText("Waiting for Client connection"); clientStatus.setForeground(AdminTheme.PINK);
        clientId.setText("--"); sessionId.setText("--"); connectedAt.setText("--"); lastSeen.setText("--");
        clientOs.setText("--");
        cpuValue.setText("0.0%"); ramValue.setText("0.0%"); cpuRing.setValue(0); ramRing.setValue(0);
        interfacesModel.setRowCount(0); scanModel.setRowCount(0); pingSummary.setText("No ping completed for this session.");
        hostsCount.setText("--");
        screenShareStatus.setText("Screen sharing: Stopped"); viewScreenButton.setEnabled(false); chatButton.setEnabled(false);
        scanButton.setEnabled(false); pingButton.setEnabled(false);
        scanProgress.setText("No scan completed for this session.");
    }

    private void fillInterfaces(Map<String, Object> networkInfo) {
        interfacesModel.setRowCount(0);
        Object rows = networkInfo.get("interfaces"); if (!(rows instanceof List<?> list)) return;
        for (Object item : list) if (item instanceof Map<?, ?> info)
            interfacesModel.addRow(new Object[]{value(info.get("name")), value(info.get("ipAddress")), value(info.get("macAddress")), value(info.get("gateway")), value(info.get("subnet"))});
    }

    private void fillMonitoring(Map<String, Object> stats) {
        double cpu = number(stats.get("cpuUsagePercent")), ram = number(stats.get("ramUsagePercent"));
        cpuRing.setValue(cpu); ramRing.setValue(ram); cpuValue.setText(String.format(java.util.Locale.ROOT, "%.1f%%", cpu));
        ramValue.setText(String.format(java.util.Locale.ROOT, "%.1f%%", ram));
    }

    private void fillLastScan(Map<String, Object> result) {
        if (result.isEmpty()) { hostsCount.setText("--"); scanProgress.setText("No scan completed for this session."); return; }
        if (Boolean.TRUE.equals(result.get("progress"))) {
            scanProgress.setText("Scanning " + value(result.get("subnet")) + " - " + value(result.get("scannedHosts"))
                    + " / " + value(result.get("totalHosts")) + " hosts; " + value(result.get("onlineHosts")) + " online");
            return;
        }
        Object rows = result.get("devices"); scanModel.setRowCount(0);
        if (rows instanceof List<?> list) for (Object item : list) if (item instanceof Map<?, ?> device)
            scanModel.addRow(new Object[]{value(device.get("ipAddress")), value(device.get("macAddress")),
                    Boolean.TRUE.equals(device.get("isOnline")) ? "Online" : "Offline", nullable(device.get("responseTimeMs"))});
        hostsCount.setText(rows instanceof List<?> list ? Integer.toString(list.size()) : "0");
        scanProgress.setText(Boolean.TRUE.equals(result.get("success")) ? "Scan completed: " + hostsCount.getText() + " devices found."
                : value(result.get("errorMessage")));
    }

    private void runPing() {
        AdminClientSnapshot selected = selectedClient(); if (selected == null) return;
        String target = targetField.getText().trim();
        if (target.isBlank()) { targetField.requestFocusInWindow(); return; }
        setOperationsEnabled(false, "Pinging " + target + " from " + selected.clientName() + "...");
        presenter.ping(selected.sessionId(), target).whenComplete((result, error) -> SwingUtilities.invokeLater(() -> {
            if (error == null) { showPing(result); operationStatus.setText("Ping completed for " + selected.clientName()); }
            else operationStatus.setText("Ping failed: " + rootMessage(error));
            setOperationsEnabled(true, operationStatus.getText());
        }));
    }

    private void viewScreen() {
        AdminClientSnapshot selected = selectedClient(); if (selected == null) return;
        try {
            screenShare.requestViewing(selected.clientId(), selected.sessionId());
            ScreenViewerFrame viewer = new ScreenViewerFrame(selected.clientName(), selected.clientId(), selected.sessionId(), screenShare, remoteControl);
            viewer.setVisible(true);
            refreshScreenShareStatus(selected.sessionId());
        } catch (Exception error) {
            JOptionPane.showMessageDialog(this, rootMessage(error), "Cannot view screen", JOptionPane.WARNING_MESSAGE);
        }
    }

    private void openChat() {
        AdminClientSnapshot selected = selectedClient(); if (selected == null) return;
        AdminChatDialog dialog = new AdminChatDialog(this, selected.clientName(), selected.clientId(), selected.sessionId(), chatPresenter);
        dialog.setVisible(true);
    }

    private void refreshScreenShareStatus(String selectedSession) {
        var state = screenShare.snapshot(selectedSession);
        screenShareStatus.setText("Screen sharing: " + state.status());
        screenShareStatus.setToolTipText(state.detail());
        viewScreenButton.setEnabled(!state.status().equals("Pending") && !state.status().equals("Accepted")
                && !state.status().equals("Viewing"));
    }

    private void showPing(Map<String, Object> result) {
        double avg = number(result.get("averageResponseTimeMs"));
        pingAverage.setText(result.get("averageResponseTimeMs") == null ? "--" : String.format(java.util.Locale.ROOT, "%.1f ms", avg));
        pingSummary.setText(value(result.get("statusMessage")) + "  •  avg " + (result.get("averageResponseTimeMs") == null ? "--" : String.format(java.util.Locale.ROOT, "%.1f ms", avg)));
        pingModel.setRowCount(0);
        Object attempts = result.get("attempts");
        if (attempts instanceof List<?> list) for (Object item : list) if (item instanceof Map<?, ?> attempt)
            pingModel.addRow(new Object[]{value(attempt.get("number")), Boolean.TRUE.equals(attempt.get("success")) ? "Reply" : value(attempt.get("status")), nullable(attempt.get("responseTimeMs"))});
        if (!(attempts instanceof List<?> list) || list.isEmpty()) pingModel.addRow(new Object[]{"1", Boolean.TRUE.equals(result.get("success")) ? "Reply" : value(result.get("statusMessage")), nullable(result.get("roundtripTimeMs"))});
    }

    private void runScan() {
        AdminClientSnapshot selected = selectedClient(); if (selected == null) return;
        String subnet = subnetField.getText().trim();
        scanInProgress = true; activeScanSession = selected.sessionId(); cancelScanButton.setEnabled(true);
        setOperationsEnabled(false, "Scanning from " + selected.clientName() + "...");
        presenter.scan(selected.sessionId(), subnet).whenComplete((result, error) -> SwingUtilities.invokeLater(() -> {
            if (error == null) {
                fillLastScan(result);
                operationStatus.setText(Boolean.TRUE.equals(result.get("success")) ? "Scan completed" : value(result.get("errorMessage")));
            } else operationStatus.setText("Scan failed: " + rootMessage(error));
            scanInProgress = false; activeScanSession = null; cancelScanButton.setEnabled(false);
            setOperationsEnabled(true, operationStatus.getText());
        }));
    }

    private void cancelScan() {
        String session = activeScanSession; if (!scanInProgress || session == null) return;
        cancelScanButton.setEnabled(false); operationStatus.setText("Canceling LAN scan...");
        presenter.cancelScan(session).whenComplete((ignored, error) -> SwingUtilities.invokeLater(() -> {
            if (error != null) operationStatus.setText("Could not cancel scan: " + rootMessage(error));
        }));
    }

    private AdminClientSnapshot selectedClient() {
        ClientChoice choice = (ClientChoice) clientPicker.getSelectedItem();
        if (choice == null) { operationStatus.setText("Connect a Client before starting a remote operation."); return null; }
        AdminClientSnapshot snapshot = currentClients.stream()
                .filter(client -> client.sessionId().equals(choice.sessionId)).findFirst().orElse(null);
        if (snapshot == null) { operationStatus.setText("Selected Client session is no longer active."); return null; }
        if (!snapshot.online()) { operationStatus.setText("Selected Client is offline; reconnect it before starting an operation."); return null; }
        return snapshot;
    }

    private void setOperationsEnabled(boolean enabled, String message) {
        operationsBusy = !enabled;
        AdminClientSnapshot selected = selectedSnapshot(); boolean online = selected != null && selected.online();
        scanButton.setEnabled(enabled && online); pingButton.setEnabled(enabled && online);
        cancelScanButton.setEnabled(!enabled && scanInProgress); operationStatus.setText(message);
    }

    private AdminClientSnapshot selectedSnapshot() {
        String selected = selectedSessionId();
        return selected == null ? null : currentClients.stream().filter(client -> client.sessionId().equals(selected)).findFirst().orElse(null);
    }

    private void closeApp() { refreshTimer.stop(); closeAction.run(); dispose(); }
    private static String format(java.time.Instant instant) {
        return instant == null ? "--" : java.time.format.DateTimeFormatter.ofPattern("M/d/yyyy h:mm:ss a", java.util.Locale.US).withZone(ZoneId.systemDefault()).format(instant);
    }
    private static String value(Object value) { return value == null || value.toString().isBlank() ? "--" : value.toString(); }
    private static Object nullable(Object value) { return value == null ? "--" : value + (value instanceof Number ? " ms" : ""); }
    private static double number(Object value) { return value instanceof Number n && Double.isFinite(n.doubleValue()) ? n.doubleValue() : 0; }
    private static String rootMessage(Throwable error) {
        Throwable current = error instanceof CompletionException && error.getCause() != null ? error.getCause() : error;
        while (current.getCause() != null) current = current.getCause();
        return current.getMessage() == null ? current.getClass().getSimpleName() : current.getMessage();
    }

    private record ClientChoice(String sessionId, String clientName, Map<String, Object> networkInfo, boolean online) {
        @Override public String toString() {
            Object interfaces = networkInfo.get("interfaces"); String ip = "--";
            if (interfaces instanceof List<?> list && !list.isEmpty() && list.getFirst() instanceof Map<?, ?> info) ip = value(info.get("ipAddress"));
            return clientName + "  |  " + ip + "  |  " + (online ? "Online" : "Offline") + "  |  Session " + sessionId.substring(0, Math.min(8, sessionId.length()));
        }
    }
}
