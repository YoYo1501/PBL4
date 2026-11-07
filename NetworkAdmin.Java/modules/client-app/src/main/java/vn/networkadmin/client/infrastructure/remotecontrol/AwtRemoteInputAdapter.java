package vn.networkadmin.client.infrastructure.remotecontrol;

import vn.networkadmin.client.application.remotecontrol.RemoteInputPort;
import vn.networkadmin.protocol.control.ControlInputEvent;

import java.awt.GraphicsEnvironment;
import java.awt.Robot;
import java.awt.Toolkit;
import java.awt.event.InputEvent;
import java.awt.event.KeyEvent;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;

/** AWT desktop input adapter. Robot is initialized lazily and only called after Client consent. */
public final class AwtRemoteInputAdapter implements RemoteInputPort {
    private volatile Robot robot;
    private final Set<Integer> pressedKeys = ConcurrentHashMap.newKeySet();
    private final Set<Integer> pressedButtons = ConcurrentHashMap.newKeySet();
    @Override public boolean available() { return !GraphicsEnvironment.isHeadless(); }

    @Override public void apply(ControlInputEvent event) {
        if (!available()) return;
        try {
            Robot active = robot;
            if (active == null) synchronized (this) {
                active = robot;
                if (active == null) robot = active = new Robot();
            }
            var screen = Toolkit.getDefaultToolkit().getScreenSize();
            int x = Math.min(screen.width - 1, (int) Math.round(event.x() * (screen.width - 1)));
            int y = Math.min(screen.height - 1, (int) Math.round(event.y() * (screen.height - 1)));
            switch (event.kind()) {
                case MOVE -> { active.mouseMove(x, y); }
                case BUTTON_DOWN -> { active.mouseMove(x, y); active.mousePress(buttonMask(event.button())); pressedButtons.add(event.button()); }
                case BUTTON_UP -> { active.mouseMove(x, y); active.mouseRelease(buttonMask(event.button())); pressedButtons.remove(event.button()); }
                case KEY_DOWN -> { active.keyPress(event.keyCode()); pressedKeys.add(event.keyCode()); }
                case KEY_UP -> { active.keyRelease(event.keyCode()); pressedKeys.remove(event.keyCode()); }
                case WHEEL -> { active.mouseMove(x, y); active.mouseWheel(event.wheel()); }
            }
        } catch (Exception error) {
            System.getLogger(getClass().getName()).log(System.Logger.Level.WARNING, "Could not apply remote input event.", error);
        }
    }

    @Override public void releaseAll() {
        Robot active = robot; if (active == null) return;
        for (int key : pressedKeys) try { active.keyRelease(key); } catch (RuntimeException ignored) { }
        for (int button : pressedButtons) try { active.mouseRelease(buttonMask(button)); } catch (RuntimeException ignored) { }
        pressedKeys.clear(); pressedButtons.clear();
    }

    private static int buttonMask(int button) {
        return switch (button) {
            case 1 -> InputEvent.BUTTON1_DOWN_MASK;
            case 2 -> InputEvent.BUTTON2_DOWN_MASK;
            case 3 -> InputEvent.BUTTON3_DOWN_MASK;
            default -> throw new IllegalArgumentException("Unsupported mouse button.");
        };
    }
}
