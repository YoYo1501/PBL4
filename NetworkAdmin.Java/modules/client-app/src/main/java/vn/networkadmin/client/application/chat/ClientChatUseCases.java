package vn.networkadmin.client.application.chat;

import java.time.Instant;

public interface ClientChatUseCases {
    void receive(ChatMessage message);
    record ChatMessage(String messageId, String sender, String text, Instant sentAt) { }
}
