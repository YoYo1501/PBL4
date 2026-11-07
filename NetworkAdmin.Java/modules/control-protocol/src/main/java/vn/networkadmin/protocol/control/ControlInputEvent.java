package vn.networkadmin.protocol.control;

import java.util.LinkedHashMap;
import java.util.Map;

/** Validated, bounded remote input event. Coordinates are normalized to [0,1]. */
public record ControlInputEvent(Kind kind, double x, double y, int button, int keyCode, int modifiers, int wheel) {
    public enum Kind { MOVE, BUTTON_DOWN, BUTTON_UP, KEY_DOWN, KEY_UP, WHEEL }

    public ControlInputEvent {
        if (kind == null) throw new IllegalArgumentException("Input event kind is required.");
        if (!Double.isFinite(x) || !Double.isFinite(y) || x < 0 || x > 1 || y < 0 || y > 1)
            throw new IllegalArgumentException("Input coordinates must be normalized to [0,1].");
        if (button < 0 || button > 3 || keyCode < 0 || keyCode > 0xffff || modifiers < 0 || modifiers > 0xffff)
            throw new IllegalArgumentException("Input event contains an invalid button or key code.");
        if (wheel < -20 || wheel > 20) throw new IllegalArgumentException("Wheel delta is outside the allowed range.");
        if ((kind == Kind.BUTTON_DOWN || kind == Kind.BUTTON_UP) && button == 0)
            throw new IllegalArgumentException("Mouse button is required.");
        if ((kind == Kind.KEY_DOWN || kind == Kind.KEY_UP) && keyCode == 0)
            throw new IllegalArgumentException("Key code is required.");
        if (kind == Kind.WHEEL && wheel == 0) throw new IllegalArgumentException("Wheel delta is required.");
    }

    public Map<String, Object> toPayload(String sessionId) {
        Map<String, Object> result = new LinkedHashMap<>();
        result.put("sessionId", sessionId); result.put("kind", kind.name()); result.put("x", x); result.put("y", y);
        result.put("button", button); result.put("keyCode", keyCode); result.put("modifiers", modifiers); result.put("wheel", wheel);
        return Map.copyOf(result);
    }

    public static ControlInputEvent fromPayload(Map<?, ?> payload) {
        try {
            Kind kind = Kind.valueOf(string(payload.get("kind")));
            return new ControlInputEvent(kind, number(payload.get("x")), number(payload.get("y")),
                    integer(payload.get("button")), integer(payload.get("keyCode")), integer(payload.get("modifiers")),
                    integer(payload.get("wheel")));
        } catch (RuntimeException error) {
            if (error instanceof IllegalArgumentException) throw error;
            throw new IllegalArgumentException("Malformed remote input event.", error);
        }
    }

    private static String string(Object value) {
        if (value instanceof String text) return text;
        throw new IllegalArgumentException("Input event kind is invalid.");
    }
    private static double number(Object value) {
        if (value instanceof Number number) return number.doubleValue();
        throw new IllegalArgumentException("Input coordinate is invalid.");
    }
    private static int integer(Object value) {
        if (value instanceof Number number && number.doubleValue() == number.intValue()) return number.intValue();
        throw new IllegalArgumentException("Input event value is invalid.");
    }
}
