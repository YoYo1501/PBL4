package vn.networkadmin.admin.presentation.chat;

import vn.networkadmin.admin.application.chat.AdminChatUseCases;

import java.util.List;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executor;

/** Keeps Admin chat socket writes and transcript reads off Swing's event thread. */
public final class AdminChatPresenter {
    private final AdminChatUseCases chat;
    private final Executor executor;
    public AdminChatPresenter(AdminChatUseCases chat, Executor executor) {
        this.chat = Objects.requireNonNull(chat); this.executor = Objects.requireNonNull(executor);
    }
    public CompletableFuture<Void> send(String clientId, String sessionId, String text) {
        return CompletableFuture.runAsync(() -> {
            try { chat.send(clientId, sessionId, text); }
            catch (Exception error) { throw new java.util.concurrent.CompletionException(error); }
        }, executor);
    }
    public CompletableFuture<List<AdminChatUseCases.ChatMessage>> messages(String sessionId) {
        return CompletableFuture.supplyAsync(() -> chat.messages(sessionId), executor);
    }
}
