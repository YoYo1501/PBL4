package vn.networkadmin.client.application.remotecontrol;

import org.junit.jupiter.api.Test;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.ports.ClientControlChannel;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicReference;
import java.util.function.Consumer;
import static org.junit.jupiter.api.Assertions.*;

class ClientRemoteControlServiceTest {
    @Test void rejectNeverAppliesInputAndAcceptCanBeRevoked() {
        var input = new FakeInput(); var view = new FakeView(false); var control = new FakeChannel();
        var service = new ClientRemoteControlService("client-1", view, input, Runnable::run);
        service.handle(control, request("request-1", "session-1"));
        assertEquals(Boolean.FALSE, control.lastPayload.get("accepted"));
        assertEquals(0, input.events.size());

        view.accept = true;
        service.handle(control, request("request-2", "session-1"));
        assertEquals(Boolean.TRUE, control.lastPayload.get("accepted"));
        service.handle(control, event("request-2", "session-1"));
        assertEquals(1, input.events.size());
        service.handle(control, event("stale-request", "session-1"));
        assertEquals(1, input.events.size());

        view.revoke.get().run();
        service.handle(control, event("request-2", "session-1"));
        assertEquals(1, input.events.size());
        assertTrue(control.messages.stream().anyMatch(m -> m.type == MessageType.CONTROL_STOP));
    }

    @Test void disconnectRevokesAndOldChannelCannotApplyInput() {
        var view = new FakeView(true); var input = new FakeInput(); var first = new FakeChannel();
        var service = new ClientRemoteControlService("client-1", view, input, Runnable::run);
        service.handle(first, request("request-1", "session-1"));
        service.disconnected(first);
        service.handle(first, event("request-1", "session-1"));
        assertTrue(input.events.isEmpty());
        assertTrue(view.stopped.contains("revoked"));
    }

    private static MessageEnvelope request(String id, String session) {
        return new MessageEnvelope(MessageType.CONTROL_REQUEST, id,
                Map.of("clientId", "client-1", "sessionId", session));
    }
    private static MessageEnvelope event(String id, String session) {
        Map<String,Object> p = new HashMap<>(new vn.networkadmin.protocol.control.ControlInputEvent(
                vn.networkadmin.protocol.control.ControlInputEvent.Kind.MOVE, .5, .5, 0, 0, 0, 0).toPayload(session));
        return new MessageEnvelope(MessageType.CONTROL_INPUT_EVENT, id, p);
    }
    private static final class FakeInput implements RemoteInputPort {
        final List<Object> events = new ArrayList<>();
        @Override public boolean available() { return true; }
        @Override public void apply(vn.networkadmin.protocol.control.ControlInputEvent event) { events.add(event); }
        @Override public void releaseAll() { }
    }
    private static final class FakeView implements RemoteControlClientView {
        boolean accept; AtomicReference<Runnable> revoke = new AtomicReference<>(); String stopped = "";
        FakeView(boolean accept) { this.accept = accept; }
        @Override public CompletionStage<Boolean> requestControlConsent(String adminName) { return CompletableFuture.completedFuture(accept); }
        @Override public void controlStarted(Runnable action) { revoke.set(action); }
        @Override public void controlStopped(String reason) { stopped = reason; }
    }
    private static final class FakeChannel implements ClientControlChannel {
        final List<Sent> messages = new ArrayList<>(); Map<?,?> lastPayload = Map.of();
        @Override public void connect() { }
        @Override public String sessionId() { return "session-1"; }
        @Override public boolean isConnected() { return true; }
        @Override public void send(MessageType type, Object payload) { }
        @Override public void send(MessageType type, String requestId, Object payload) {
            messages.add(new Sent(type, requestId, payload)); if (payload instanceof Map<?,?> map) lastPayload = map;
        }
        @Override public void runReadLoop(Consumer<MessageEnvelope> handler) { }
        @Override public void close() { }
    }
    private record Sent(MessageType type, String requestId, Object payload) { }
}
