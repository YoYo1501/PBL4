package vn.networkadmin.protocol.control;

import java.util.ArrayDeque;
import java.util.HashSet;
import java.util.Queue;
import java.util.Set;

/** Bounded duplicate/replay ledger matching the shared .NET contract. */
public final class MessageIdLedger {
    private final int capacity;
    private final Queue<String> order = new ArrayDeque<>();
    private final Set<String> ids = new HashSet<>();

    public MessageIdLedger() { this(1024); }
    public MessageIdLedger(int capacity) {
        if (capacity <= 0) throw new IllegalArgumentException("capacity must be greater than zero.");
        this.capacity = capacity;
    }
    public synchronized boolean tryRegister(String messageId) {
        if (!RemoteProtocolContract.isValidIdentifier(messageId)) return false;
        if (!ids.add(messageId)) return false;
        order.add(messageId);
        if (order.size() > capacity) ids.remove(order.remove());
        return true;
    }
    public synchronized void clear() { ids.clear(); order.clear(); }
}
