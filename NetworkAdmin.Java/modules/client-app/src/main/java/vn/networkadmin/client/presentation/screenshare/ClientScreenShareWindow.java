package vn.networkadmin.client.presentation.screenshare;

import vn.networkadmin.client.application.chat.ClientChatUseCases;
import vn.networkadmin.client.application.remotecontrol.RemoteControlClientView;
import vn.networkadmin.client.application.screenshare.ScreenShareClientView;
import vn.networkadmin.client.application.remoteadministration.RemoteAdministrationClientView;

import javax.swing.*;
import java.awt.*;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.function.Consumer;

/** Minimal Client feature window: connection, consent, revoke controls, and chat. */
public final class ClientScreenShareWindow implements ScreenShareClientView, RemoteControlClientView, RemoteAdministrationClientView {
    private final JFrame frame;
    private final JLabel connection = new JLabel("Disconnected from Admin");
    private final JLabel identity = new JLabel("Client: NetworkAdmin Client");
    private final JLabel sharing = new JLabel("Screen is not being shared");
    private final JLabel control = new JLabel("Remote control is not allowed");
    private final JLabel remoteAdministration = new JLabel("No remote administration operation pending");
    private final JButton cancelRemoteAdministration = new JButton("Cancel pending operation");
    private final JButton stop = new JButton("Stop Sharing");
    private final JButton revoke = new JButton("Revoke Control");
    private final JTextArea chat = new JTextArea();
    private final JTextField chatInput = new JTextField();
    private volatile Runnable stopAction = () -> { }, revokeAction = () -> { };
    private volatile Consumer<String> chatSender = ignored -> { };
    private volatile boolean activelySharing, activelyControlled;
    private volatile Runnable cancelRemoteAction = () -> { };
    private javax.swing.Timer remoteCountdown;

    public ClientScreenShareWindow() {
        if (GraphicsEnvironment.isHeadless()) { frame = null; return; }
        frame = new JFrame("Network Administration - Client");
        frame.setDefaultCloseOperation(WindowConstants.DO_NOTHING_ON_CLOSE);
        frame.addWindowListener(new java.awt.event.WindowAdapter() {
            @Override public void windowClosing(java.awt.event.WindowEvent event) {
                if (activelySharing || activelyControlled) { frame.setVisible(true); frame.toFront(); }
                else frame.setExtendedState(Frame.ICONIFIED);
            }
        });
        JPanel root = new JPanel(new BorderLayout(8, 10)); root.setBorder(BorderFactory.createEmptyBorder(14, 16, 14, 16));
        JPanel statuses = new JPanel(); statuses.setOpaque(false); statuses.setLayout(new BoxLayout(statuses, BoxLayout.Y_AXIS));
        statuses.add(identity); statuses.add(Box.createVerticalStrut(5)); statuses.add(connection);
        statuses.add(Box.createVerticalStrut(8)); statuses.add(sharing);
        statuses.add(Box.createVerticalStrut(8)); statuses.add(control); root.add(statuses, BorderLayout.NORTH);
        JPanel remotePanel = new JPanel(new BorderLayout(8, 0));
        remotePanel.setBorder(BorderFactory.createTitledBorder("Remote Administration"));
        remotePanel.add(remoteAdministration, BorderLayout.CENTER);
        cancelRemoteAdministration.setEnabled(false);
        cancelRemoteAdministration.addActionListener(event -> cancelRemoteAction.run());
        remotePanel.add(cancelRemoteAdministration, BorderLayout.EAST);
        chat.setEditable(false); chat.setLineWrap(true); chat.setWrapStyleWord(true);
        JPanel chatPanel = new JPanel(new BorderLayout(5, 5)); chatPanel.setBorder(BorderFactory.createTitledBorder("Chat with Admin"));
        chatPanel.add(new JScrollPane(chat), BorderLayout.CENTER);
        JButton send = new JButton("Send"); send.addActionListener(event -> sendChat()); chatInput.addActionListener(event -> sendChat());
        JPanel input = new JPanel(new BorderLayout(5, 0)); input.add(chatInput, BorderLayout.CENTER); input.add(send, BorderLayout.EAST);
        chatPanel.add(input, BorderLayout.SOUTH);
        JPanel center = new JPanel(new BorderLayout(0, 8)); center.add(remotePanel, BorderLayout.NORTH); center.add(chatPanel, BorderLayout.CENTER);
        root.add(center, BorderLayout.CENTER);
        stop.setEnabled(false); stop.addActionListener(event -> stopAction.run());
        revoke.setEnabled(false); revoke.addActionListener(event -> revokeAction.run());
        JPanel buttons = new JPanel(new FlowLayout(FlowLayout.RIGHT)); buttons.add(revoke); buttons.add(stop);
        root.add(buttons, BorderLayout.SOUTH);
        frame.setContentPane(root); frame.setSize(520, 390); frame.setLocationRelativeTo(null);
        SwingUtilities.invokeLater(() -> frame.setVisible(true));
    }

    @Override public CompletionStage<Boolean> requestConsent(String adminName) {
        return confirm("Screen sharing request", "Admin is requesting to view your screen. Capturing starts only after you accept.", "Accept");
    }

    @Override public CompletionStage<Boolean> requestControlConsent(String adminName) {
        return confirm("Remote control request", "Admin requests mouse and keyboard control. You can revoke access at any time.", "Allow Control");
    }

    private CompletionStage<Boolean> confirm(String title, String prompt, String accept) {
        if (GraphicsEnvironment.isHeadless()) return CompletableFuture.completedFuture(false);
        CompletableFuture<Boolean> result = new CompletableFuture<>();
        SwingUtilities.invokeLater(() -> {
            frame.setVisible(true); frame.setExtendedState(Frame.NORMAL); frame.toFront();
            Object[] options = {accept, "Reject"};
            int choice = JOptionPane.showOptionDialog(frame, prompt, title, JOptionPane.YES_NO_OPTION,
                    JOptionPane.WARNING_MESSAGE, null, options, options[1]);
            result.complete(choice == 0);
        });
        return result;
    }

    @Override public void sharingStarted(Runnable action) {
        stopAction = action; activelySharing = true;
        onEdt(() -> { sharing.setText("Your screen is being shared with Admin"); stop.setEnabled(true); frame.setVisible(true); });
    }
    @Override public void sharingStopped(String reason) {
        stopAction = () -> { }; activelySharing = false;
        onEdt(() -> { sharing.setText(reason == null || reason.isBlank() ? "Screen is not being shared" : reason); stop.setEnabled(false); });
    }
    @Override public void controlStarted(Runnable action) {
        revokeAction = action; activelyControlled = true;
        onEdt(() -> { control.setText("Admin has remote-control permission"); revoke.setEnabled(true); frame.setVisible(true); frame.toFront(); });
    }
    @Override public void controlStopped(String reason) {
        revokeAction = () -> { }; activelyControlled = false;
        onEdt(() -> { control.setText(reason == null || reason.isBlank() ? "Remote control is not allowed" : reason); revoke.setEnabled(false); });
    }
    @Override public void showCountdown(String command, int seconds, Runnable cancel) {
        cancelRemoteAction = Objects.requireNonNull(cancel);
        onEdt(() -> {
            if (remoteCountdown != null) remoteCountdown.stop();
            final int[] remaining = {seconds};
            remoteAdministration.setText(command + " scheduled in " + remaining[0] + " seconds. Cancel if needed.");
            cancelRemoteAdministration.setEnabled(true);
            frame.setVisible(true); frame.setExtendedState(Frame.NORMAL); frame.toFront();
            remoteCountdown = new javax.swing.Timer(1000, event -> {
                remaining[0]--;
                if (remaining[0] <= 0) {
                    remoteCountdown.stop();
                    remoteAdministration.setText(command + " is being executed.");
                    cancelRemoteAdministration.setEnabled(false);
                } else remoteAdministration.setText(command + " scheduled in " + remaining[0] + " seconds. Cancel if needed.");
            });
            remoteCountdown.start();
        });
    }
    @Override public void clearCountdown(String status) {
        onEdt(() -> {
            if (remoteCountdown != null) remoteCountdown.stop();
            remoteAdministration.setText(status == null || status.isBlank() ? "No remote administration operation pending" : status);
            cancelRemoteAdministration.setEnabled(false);
            cancelRemoteAction = () -> { };
        });
    }
    @Override public void connectionChanged(boolean connected) {
        onEdt(() -> connection.setText(connected ? "Connected to Admin" : "Disconnected from Admin"));
    }
    @Override public void connectionFailed(String reason) {
        onEdt(() -> connection.setText("Connection failed; retrying: " + reason));
    }
    @Override public void clientIdentity(String name, String clientId) {
        onEdt(() -> identity.setText("Client: " + name + "  |  ID: " + clientId));
    }
    public void setChatSender(Consumer<String> action) { chatSender = Objects.requireNonNull(action); }
    public void chatReceived(ClientChatUseCases.ChatMessage message) {
        onEdt(() -> chat.append("[" + DateTimeFormatter.ofPattern("HH:mm:ss").withZone(ZoneId.systemDefault()).format(message.sentAt())
                + "] " + message.sender() + ": " + message.text() + "\n"));
    }
    private void sendChat() {
        String value = chatInput.getText(); if (value == null || value.isBlank()) return;
        try { chatSender.accept(value); chatInput.setText(""); }
        catch (RuntimeException error) { JOptionPane.showMessageDialog(frame, error.getMessage(), "Chat error", JOptionPane.WARNING_MESSAGE); }
    }
    private static void onEdt(Runnable action) {
        if (GraphicsEnvironment.isHeadless()) return;
        if (SwingUtilities.isEventDispatchThread()) action.run(); else SwingUtilities.invokeLater(action);
    }
}
