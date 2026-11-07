package vn.networkadmin.admin.application.chat;

import vn.networkadmin.admin.ports.AdminControlMessageHandler;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.protocol.control.RemoteProtocolContract;
import vn.networkadmin.transport.domain.ClientSession;
import vn.networkadmin.transport.ports.ClientSessionStore;
import vn.networkadmin.transport.ports.SessionMessageSender;

import java.io.IOException;
import java.time.Instant;
import java.util.ArrayDeque;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;

/** In-memory, bounded, session-scoped chat. */
public final class AdminChatService implements AdminChatUseCases, AdminControlMessageHandler {
    private static final int HISTORY_LIMIT = 200;
    private final ClientSessionStore sessions;
    private final SessionMessageSender sender;
    private final ConcurrentHashMap<String, ArrayDeque<ChatMessage>> history = new ConcurrentHashMap<>();

    public AdminChatService(ClientSessionStore sessions, SessionMessageSender sender) {
        this.sessions = sessions; this.sender = sender;
    }

    @Override public void send(String clientId, String sessionId, String text) throws IOException {
        ClientSession session = sessions.get(sessionId);
        String clean = validate(text);
        if (session == null || !session.isReady() || !session.clientId().equals(clientId))
            throw new IOException("Selected Client session is no longer active.");
        String id = UUID.randomUUID().toString().replace("-", "");
        Instant now = Instant.now();
        sender.send(sessionId, MessageType.REMOTE_CHAT_MESSAGE, id, Map.of("clientId", clientId,
                "sessionId", sessionId, "messageId", id, "direction", "ADMIN_TO_CLIENT", "text", clean,
                "sentAt", now.toString()));
        add(new ChatMessage(id, clientId, sessionId, "Admin", clean, now));
    }

    @Override public void handle(ClientSession session, MessageEnvelope message) {
        if (message.type() != MessageType.REMOTE_CHAT_MESSAGE || !(message.data() instanceof Map<?, ?> payload)) return;
        if (!session.isReady() || !session.sessionId().equals(payload.get("sessionId"))
                || !session.clientId().equals(payload.get("clientId"))
                || !"CLIENT_TO_ADMIN".equals(payload.get("direction"))) return;
        try {
            String text = validate(payload.get("text") instanceof String value ? value : "");
            String id = payload.get("messageId") instanceof String value && !value.isBlank() ? value : message.requestId();
            Instant sentAt = payload.get("sentAt") instanceof String value ? Instant.parse(value) : Instant.now();
            if (id != null) add(new ChatMessage(id, session.clientId(), session.sessionId(), session.clientName(), text, sentAt));
        } catch (RuntimeException ignored) { }
    }

    @Override public List<ChatMessage> messages(String sessionId) {
        ArrayDeque<ChatMessage> entries = history.get(sessionId);
        if (entries == null) return List.of();
        synchronized (entries) { return List.copyOf(entries); }
    }

    @Override public void disconnected(ClientSession session) { history.remove(session.sessionId()); }

    private void add(ChatMessage message) {
        ArrayDeque<ChatMessage> entries = history.computeIfAbsent(message.sessionId(), ignored -> new ArrayDeque<>());
        synchronized (entries) { entries.addLast(message); while (entries.size() > HISTORY_LIMIT) entries.removeFirst(); }
    }

    private static String validate(String text) {
        if (text == null || text.isBlank()) throw new IllegalArgumentException("Chat message cannot be empty.");
        String clean = text.strip();
        if (clean.length() > RemoteProtocolContract.MAX_CHAT_TEXT_LENGTH)
            throw new IllegalArgumentException("Chat message exceeds the 4096-character limit.");
        return clean;
    }
}
