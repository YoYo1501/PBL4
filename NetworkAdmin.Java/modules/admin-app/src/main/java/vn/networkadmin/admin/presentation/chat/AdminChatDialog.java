package vn.networkadmin.admin.presentation.chat;

import vn.networkadmin.admin.application.chat.AdminChatUseCases;

import javax.swing.*;
import java.awt.*;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.List;

/** Minimal in-memory Admin chat window bound to one Client session. */
public final class AdminChatDialog extends JDialog {
    private final String clientId, sessionId;
    private final AdminChatPresenter presenter;
    private final JTextPane transcript = new JTextPane();
    private final JTextField input = new JTextField();
    private final JLabel state = new JLabel("Connected to selected Client session");
    private final DateTimeFormatter time = DateTimeFormatter.ofPattern("HH:mm:ss").withZone(ZoneId.systemDefault());
    private final Timer refresh;
    private int rendered;

    public AdminChatDialog(Window owner, String clientName, String clientId, String sessionId, AdminChatPresenter presenter) {
        super(owner, "Chat - " + clientName, ModalityType.MODELESS);
        setDefaultCloseOperation(WindowConstants.DISPOSE_ON_CLOSE);
        setIconImage(vn.networkadmin.admin.presentation.components.DashboardIcon.createGlobeImage(32, vn.networkadmin.admin.presentation.theme.AdminTheme.BLUE));
        this.clientId = clientId; this.sessionId = sessionId; this.presenter = presenter;
        transcript.setEditable(false);
        JButton send = new JButton("Send"); send.addActionListener(event -> send()); input.addActionListener(event -> send());
        JPanel bottom = new JPanel(new BorderLayout(6, 6)); bottom.add(state, BorderLayout.NORTH);
        JPanel row = new JPanel(new BorderLayout(5, 0)); row.add(input, BorderLayout.CENTER); row.add(send, BorderLayout.EAST); bottom.add(row, BorderLayout.SOUTH);
        add(new JScrollPane(transcript), BorderLayout.CENTER); add(bottom, BorderLayout.SOUTH);
        setSize(480, 420); setLocationRelativeTo(owner);
        refresh = new Timer(500, event -> refreshMessages()); refresh.start();
        addWindowListener(new java.awt.event.WindowAdapter() { @Override public void windowClosed(java.awt.event.WindowEvent event) { refresh.stop(); } });
    }

    private void send() {
        String text = input.getText(); if (text == null || text.isBlank()) return;
        input.setEnabled(false); state.setText("Sending...");
        presenter.send(clientId, sessionId, text).whenComplete((ignored, error) -> SwingUtilities.invokeLater(() -> {
            input.setEnabled(true);
            if (error == null) { input.setText(""); state.setText("Sent"); }
            else state.setText("Send failed: " + root(error));
            input.requestFocusInWindow();
        }));
    }

    private void refreshMessages() {
        presenter.messages(sessionId).whenComplete((messages, error) -> SwingUtilities.invokeLater(() -> {
            if (error != null) { state.setText("Chat update failed: " + root(error)); return; }
            List<AdminChatUseCases.ChatMessage> current = messages;
            for (int i = rendered; i < current.size(); i++) {
                var message = current.get(i);
                appendMessage("[" + time.format(message.sentAt()) + "] " + message.sender() + ": " + message.text() + "\n",
                        !message.sender().equalsIgnoreCase("Admin"));
            }
            rendered = current.size();
        }));
    }
    private void appendMessage(String text, boolean incoming) {
        javax.swing.text.StyledDocument document = transcript.getStyledDocument();
        javax.swing.text.Style style = transcript.addStyle(incoming ? "incoming" : "outgoing", null);
        javax.swing.text.StyleConstants.setForeground(style, incoming ? new Color(35, 110, 210) : UIManager.getColor("TextArea.foreground"));
        try {
            document.insertString(document.getLength(), text, style);
            transcript.setCaretPosition(document.getLength());
        } catch (javax.swing.text.BadLocationException ignored) { }
    }
    private static String root(Throwable error) {
        Throwable current = error; while (current.getCause() != null) current = current.getCause();
        return current.getMessage() == null ? current.getClass().getSimpleName() : current.getMessage();
    }
}
