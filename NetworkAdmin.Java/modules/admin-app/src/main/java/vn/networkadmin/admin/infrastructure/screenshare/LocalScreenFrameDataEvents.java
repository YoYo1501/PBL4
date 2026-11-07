package vn.networkadmin.admin.infrastructure.screenshare;

import vn.networkadmin.admin.application.screenshare.ScreenFrameDataEvents;
import java.util.concurrent.CopyOnWriteArrayList;
import java.util.function.Consumer;

/** App-scoped event bridge with typed publish/subscribe; it performs no dependency lookup. */
public final class LocalScreenFrameDataEvents implements ScreenFrameDataEvents {
    private final CopyOnWriteArrayList<Consumer<Event>> listeners = new CopyOnWriteArrayList<>();
    @Override public AutoCloseable subscribe(Consumer<Event> listener) { listeners.add(listener); return () -> listeners.remove(listener); }
    @Override public void publish(Event event) {
        for (Consumer<Event> listener : listeners) try { listener.accept(event); }
        catch (RuntimeException error) { System.getLogger(getClass().getName()).log(System.Logger.Level.WARNING, "Screen event listener failed.", error); }
    }
}
