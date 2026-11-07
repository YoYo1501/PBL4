package vn.networkadmin.admin.application.chat;

import org.junit.jupiter.api.Test;
import vn.networkadmin.client.application.chat.ClientChatService;
import vn.networkadmin.client.application.chat.ClientChatUseCases;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.infrastructure.ClientSessionRegistry;
import vn.networkadmin.transport.ports.ClientControlChannel;
import vn.networkadmin.transport.ports.SessionMessageSender;
import java.util.*;
import java.util.function.Consumer;
import static org.junit.jupiter.api.Assertions.*;

class AdminChatServiceTest {
    @Test void messagesTravelBothWaysAndAreScopedToTheirClientSession() throws Exception {
        var sessions = new ClientSessionRegistry(); var session = new ClientSession("client-1", "Client", "session-1", "localhost");
        sessions.register(session); session.markReady();
        var adminWire = new RecordingSender(); var admin = new AdminChatService(sessions, adminWire);
        var received = new ArrayList<ClientChatUseCases.ChatMessage>(); var clientWire = new FakeChannel();
        var client = new ClientChatService("client-1", "Client", received::add); client.connected(clientWire);

        admin.send("client-1", "session-1", "hello");
        var outbound = adminWire.last();
        client.handle(clientWire, new MessageEnvelope(outbound.type, outbound.requestId, outbound.payload));
        assertEquals("hello", received.getFirst().text());
        client.send("reply");
        var reply = clientWire.sent.getFirst();
        admin.handle(session, new MessageEnvelope(reply.type, reply.requestId, reply.payload));
        assertEquals(2, admin.messages("session-1").size());
        assertEquals("reply", admin.messages("session-1").getLast().text());

        ClientSession other = new ClientSession("client-2", "Other", "session-2", "localhost"); other.markReady();
        admin.handle(session, new MessageEnvelope(MessageType.REMOTE_CHAT_MESSAGE, "bad",
                Map.of("clientId", "client-2", "sessionId", "session-2", "direction", "CLIENT_TO_ADMIN", "text", "wrong")));
        assertEquals(0, admin.messages("session-2").size());
        client.disconnected(clientWire); sessions.close();
    }

    private static final class RecordingSender implements SessionMessageSender {
        final List<Sent> sent = new ArrayList<>();
        @Override public void send(String sid, MessageType type, String id, Object payload) { sent.add(new Sent(sid,type,id,payload)); }
        @Override public void closeSession(String sid) { }
        Sent last() { return sent.getLast(); }
    }
    private static final class FakeChannel implements ClientControlChannel {
        final List<Sent> sent = new ArrayList<>();
        @Override public void connect() { }
        @Override public String sessionId() { return "session-1"; }
        @Override public boolean isConnected() { return true; }
        @Override public void send(MessageType type, Object payload) { }
        @Override public void send(MessageType type, String id, Object payload) { sent.add(new Sent("session-1", type, id, payload)); }
        @Override public void runReadLoop(Consumer<MessageEnvelope> handler) { }
        @Override public void close() { }
    }
    private record Sent(String sessionId, MessageType type, String requestId, Object payload) { }
}
