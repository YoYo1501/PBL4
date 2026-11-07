package vn.networkadmin.protocol.control;

import org.junit.jupiter.api.Test;
import java.util.Map;
import static org.junit.jupiter.api.Assertions.*;

class ControlInputEventTest {
    @Test void payloadRoundTripsValidatedInput() {
        var event = new ControlInputEvent(ControlInputEvent.Kind.MOVE, .3, .8, 0, 0, 0, 0);
        assertEquals(event, ControlInputEvent.fromPayload(event.toPayload("session-1")));
    }
    @Test void rejectsOutOfRangeCoordinatesAndMalformedKeyEvents() {
        assertThrows(IllegalArgumentException.class, () -> new ControlInputEvent(ControlInputEvent.Kind.MOVE, 2, 0, 0, 0, 0, 0));
        assertThrows(IllegalArgumentException.class, () -> ControlInputEvent.fromPayload(Map.of(
                "kind", "KEY_DOWN", "x", 0, "y", 0, "button", 0, "keyCode", 0, "modifiers", 0, "wheel", 0)));
    }
}
