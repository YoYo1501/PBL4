package vn.networkadmin.admin.presentation.screenshare;

import vn.networkadmin.admin.application.remotecontrol.AdminRemoteControlUseCases;
import vn.networkadmin.admin.application.screenshare.AdminScreenShareUseCases;
import vn.networkadmin.protocol.control.ControlInputEvent;
import vn.networkadmin.screenshare.server.domain.ScreenCanvas;

import javax.swing.*;
import java.awt.*;
import java.awt.event.*;
import java.awt.image.BufferedImage;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/** Live Screen Viewer with a separate, explicitly consented remote-control capability. */
public final class ScreenViewerFrame extends JFrame {
    private final String clientId, sessionId;
    private final AdminScreenShareUseCases screenShare;
    private final AdminRemoteControlUseCases remoteControl;
    private final JLabel status = new JLabel("Waiting for Client consent...", SwingConstants.CENTER);
    private final JButton requestControl = new JButton("Request Control");
    private final JButton stopControl = new JButton("Stop Control");
    private final CanvasView canvas = new CanvasView();
    private final Timer refresh;
    private final Timer pointerDispatch;
    private final java.util.concurrent.atomic.AtomicReference<Point> pendingPointer = new java.util.concurrent.atomic.AtomicReference<>();
    private Object lastCanvas;

    public ScreenViewerFrame(String clientName, String clientId, String sessionId,
                             AdminScreenShareUseCases screenShare, AdminRemoteControlUseCases remoteControl) {
        super("Screen Viewer - " + clientName);
        setIconImage(vn.networkadmin.admin.presentation.components.DashboardIcon.createGlobeImage(32, vn.networkadmin.admin.presentation.theme.AdminTheme.BLUE));
        this.clientId = clientId; this.sessionId = sessionId;
        this.screenShare = screenShare; this.remoteControl = remoteControl;
        setDefaultCloseOperation(WindowConstants.DISPOSE_ON_CLOSE);
        setMinimumSize(new Dimension(800, 500)); setSize(1200, 760); setLocationRelativeTo(null);
        status.setBorder(BorderFactory.createEmptyBorder(8, 12, 8, 12));
        JButton stopView = new JButton("Stop Viewing");
        stopView.addActionListener(event -> { remoteControl.stopControl(sessionId); screenShare.stopViewing(sessionId); });
        requestControl.addActionListener(event -> requestControl());
        stopControl.setEnabled(false); stopControl.addActionListener(event -> remoteControl.stopControl(sessionId));
        JPanel actions = new JPanel(new FlowLayout(FlowLayout.RIGHT));
        actions.add(requestControl); actions.add(stopControl); actions.add(stopView);
        JPanel footer = new JPanel(new BorderLayout()); footer.add(status, BorderLayout.CENTER); footer.add(actions, BorderLayout.EAST);
        add(canvas, BorderLayout.CENTER); add(footer, BorderLayout.SOUTH);
        refresh = new Timer(40, event -> refreshState());
        pointerDispatch = new Timer(33, event -> {
            Point point = pendingPointer.getAndSet(null);
            if (point != null) send(ControlInputEvent.Kind.MOVE, point.x, point.y, 0, 0, 0);
        });
        addWindowListener(new java.awt.event.WindowAdapter() {
            @Override public void windowClosing(java.awt.event.WindowEvent event) {
                screenShare.stopViewing(sessionId); remoteControl.stopControl(sessionId);
            }
            @Override public void windowClosed(java.awt.event.WindowEvent event) { refresh.stop(); pointerDispatch.stop(); }
        });
        installInputListeners(); refresh.start(); pointerDispatch.start();
    }

    private void requestControl() {
        if (!screenShare.snapshot(sessionId).status().equals("Viewing")) {
            status.setText("Wait until the Client has accepted screen sharing before requesting control."); return;
        }
        try { remoteControl.requestControl(clientId, sessionId); status.setText("Waiting for Client remote-control consent..."); }
        catch (Exception error) { status.setText("Control request failed: " + error.getMessage()); }
    }

    private void refreshState() {
        var view = screenShare.snapshot(sessionId);
        var control = remoteControl.state(sessionId);
        status.setText(view.status() + (view.detail().isBlank() ? "" : " - " + view.detail())
                + " | Control: " + control.status());
        requestControl.setEnabled(view.status().equals("Viewing")
                && (control.status().equals("Stopped") || control.status().equals("Rejected")));
        stopControl.setEnabled(control.status().equals("Pending") || control.status().equals("Granted"));
        if (view.canvas() != null && view.canvas() != lastCanvas) {
            lastCanvas = view.canvas(); canvas.setImage(toImage(view.canvas()));
        }
    }

    private void installInputListeners() {
        Set<Integer> pressedKeys = ConcurrentHashMap.newKeySet();
        Set<Integer> pressedButtons = ConcurrentHashMap.newKeySet();
        canvas.setFocusable(true);
        canvas.addMouseListener(new MouseAdapter() {
            @Override public void mousePressed(MouseEvent event) {
                canvas.requestFocusInWindow(); int button = event.getButton(); pressedButtons.add(button);
                send(ControlInputEvent.Kind.BUTTON_DOWN, event.getX(), event.getY(), button, 0, 0);
            }
            @Override public void mouseReleased(MouseEvent event) {
                int button = event.getButton(); pressedButtons.remove(button);
                send(ControlInputEvent.Kind.BUTTON_UP, event.getX(), event.getY(), button, 0, 0);
            }
        });
        canvas.addMouseMotionListener(new MouseMotionAdapter() {
            @Override public void mouseMoved(MouseEvent event) { pendingPointer.set(new Point(event.getX(), event.getY())); }
            @Override public void mouseDragged(MouseEvent event) { pendingPointer.set(new Point(event.getX(), event.getY())); }
        });
        canvas.addMouseWheelListener(event -> send(ControlInputEvent.Kind.WHEEL, event.getX(), event.getY(), 0, 0,
                Math.max(-20, Math.min(20, event.getWheelRotation()))));
        canvas.addKeyListener(new KeyAdapter() {
            @Override public void keyPressed(KeyEvent event) {
                if (pressedKeys.add(event.getKeyCode())) send(ControlInputEvent.Kind.KEY_DOWN, 0, 0, 0, event.getKeyCode(), 0);
            }
            @Override public void keyReleased(KeyEvent event) {
                pressedKeys.remove(event.getKeyCode()); send(ControlInputEvent.Kind.KEY_UP, 0, 0, 0, event.getKeyCode(), 0);
            }
        });
        canvas.addFocusListener(new FocusAdapter() {
            @Override public void focusLost(FocusEvent event) {
                pressedKeys.forEach(key -> send(ControlInputEvent.Kind.KEY_UP, 0, 0, 0, key, 0)); pressedKeys.clear();
                pressedButtons.forEach(button -> send(ControlInputEvent.Kind.BUTTON_UP, 0, 0, button, 0, 0)); pressedButtons.clear();
            }
        });
    }

    private void send(ControlInputEvent.Kind kind, int viewX, int viewY, int button, int keyCode, int wheel) {
        if (!remoteControl.state(sessionId).status().equals("Granted")) return;
        Point normalized = canvas.normalized(viewX, viewY);
        if (normalized == null) return;
        try { remoteControl.sendInput(sessionId, new ControlInputEvent(kind,
                normalized.x / 1_000_000d, normalized.y / 1_000_000d, button, keyCode, 0, wheel)); }
        catch (Exception ignored) { }
    }

    private static BufferedImage toImage(ScreenCanvas source) {
        int[] pixels = new int[source.width() * source.height()]; source.pixels().get(pixels);
        BufferedImage image = new BufferedImage(source.width(), source.height(), BufferedImage.TYPE_INT_RGB);
        image.setRGB(0, 0, source.width(), source.height(), pixels, 0, source.width()); return image;
    }

    private static final class CanvasView extends JComponent {
        private volatile BufferedImage image;
        void setImage(BufferedImage image) { this.image = image; repaint(); }
        Point normalized(int x, int y) {
            BufferedImage current = image; if (current == null || getWidth() <= 0 || getHeight() <= 0) return null;
            double scale = Math.min((double) getWidth() / current.getWidth(), (double) getHeight() / current.getHeight());
            int width = Math.max(1, (int) (current.getWidth() * scale)); int height = Math.max(1, (int) (current.getHeight() * scale));
            int offsetX = (getWidth() - width) / 2, offsetY = (getHeight() - height) / 2;
            int px = x - offsetX, py = y - offsetY;
            if (px < 0 || py < 0 || px >= width || py >= height) return null;
            return new Point((int) Math.round((double) px / Math.max(1, width - 1) * 1_000_000),
                    (int) Math.round((double) py / Math.max(1, height - 1) * 1_000_000));
        }
        @Override protected void paintComponent(Graphics graphics) {
            super.paintComponent(graphics); BufferedImage current = image;
            if (current == null) {
                graphics.setColor(new Color(20, 30, 40)); graphics.fillRect(0, 0, getWidth(), getHeight());
                graphics.setColor(Color.LIGHT_GRAY); graphics.drawString("Waiting for screen frames...", 24, 32); return;
            }
            double scale = Math.min((double) getWidth() / current.getWidth(), (double) getHeight() / current.getHeight());
            int width = (int) (current.getWidth() * scale), height = (int) (current.getHeight() * scale);
            graphics.drawImage(current, (getWidth() - width) / 2, (getHeight() - height) / 2, width, height, null);
        }
    }
}
