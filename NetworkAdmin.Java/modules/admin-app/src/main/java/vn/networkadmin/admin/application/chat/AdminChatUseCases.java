package vn.networkadmin.admin.application.chat;

import java.io.IOException;
import java.time.Instant;
import java.util.List;

public interface AdminChatUseCases {
    void send(String clientId, String sessionId, String text) throws IOException;
    List<ChatMessage> messages(String sessionId);
    record ChatMessage(String messageId, String clientId, String sessionId, String sender, String text, Instant sentAt) { }
}
