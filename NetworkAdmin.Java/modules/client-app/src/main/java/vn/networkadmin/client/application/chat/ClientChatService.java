package vn.networkadmin.client.application.chat;

import vn.networkadmin.client.ports.ClientFeatureMessageHandler;
import vn.networkadmin.protocol.control.MessageEnvelope;
import vn.networkadmin.protocol.control.MessageType;
import vn.networkadmin.protocol.control.RemoteProtocolContract;
import vn.networkadmin.transport.ports.ClientControlChannel;

import java.io.IOException;
import java.time.Instant;
import java.util.Map;
import java.util.Objects;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicReference;

/** Session-bound, in-memory Client chat endpoint. */
public final class ClientChatService implements ClientFeatureMessageHandler, ClientChatUseCases {
    private final String clientId;
    private final String clientName;
    private final AtomicReference<ClientControlChannel> active = new AtomicReference<>();
    private volatile java.util.function.Consumer<ChatMessage> receiver = ignored -> { };

    public ClientChatService(String clientId, String clientName, java.util.function.Consumer<ChatMessage> receiver) {
        this.clientId = Objects.requireNonNull(clientId); this.clientName = Objects.requireNonNull(clientName);
        this.receiver = Objects.requireNonNull(receiver);
    }
    @Override public void receive(ChatMessage message) { receiver.accept(message); }
    @Override public void connected(ClientControlChannel channel) { active.set(channel); }
    @Override public void disconnected(ClientControlChannel channel) { active.compareAndSet(channel, null); }

    @Override public void handle(ClientControlChannel channel, MessageEnvelope message) {
        if (message.type() != MessageType.REMOTE_CHAT_MESSAGE || active.get() != channel
                || !(message.data() instanceof Map<?, ?> payload)) return;
        if (!channel.isConnected() || !channel.sessionId().equals(payload.get("sessionId"))
                || !clientId.equals(payload.get("clientId")) || !"ADMIN_TO_CLIENT".equals(payload.get("direction"))) return;
        try {
            String text = clean(payload.get("text") instanceof String value ? value : "");
            String id = payload.get("messageId") instanceof String value ? value : message.requestId();
            Instant time = payload.get("sentAt") instanceof String value ? Instant.parse(value) : Instant.now();
            receive(new ChatMessage(id, "Admin", text, time));
        } catch (RuntimeException ignored) { }
    }

    public void send(String text) {
        ClientControlChannel channel = active.get();
        if (channel == null || !channel.isConnected()) throw new IllegalStateException("Admin is disconnected.");
        try {
            String clean = clean(text); String id = UUID.randomUUID().toString().replace("-", ""); Instant now = Instant.now();
            channel.send(MessageType.REMOTE_CHAT_MESSAGE, id, Map.of("clientId", clientId,
                    "sessionId", channel.sessionId(), "messageId", id, "direction", "CLIENT_TO_ADMIN",
                    "text", clean, "sentAt", now.toString()));
            receive(new ChatMessage(id, clientName, clean, now));
        } catch (IOException error) { throw new IllegalStateException("Could not send chat message.", error); }
    }

    private static String clean(String text) {
        if (text == null || text.isBlank()) throw new IllegalArgumentException("Chat message cannot be empty.");
        String result = text.strip();
        if (result.length() > RemoteProtocolContract.MAX_CHAT_TEXT_LENGTH)
            throw new IllegalArgumentException("Chat message exceeds the 4096-character limit.");
        return result;
    }
}
