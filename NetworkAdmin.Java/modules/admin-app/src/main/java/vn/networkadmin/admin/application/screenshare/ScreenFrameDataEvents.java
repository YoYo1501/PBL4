package vn.networkadmin.admin.application.screenshare;

import vn.networkadmin.protocol.screenshare.ScreenFramePacket;
import java.util.function.Consumer;

/** Typed in-process event port for the separate screen-frame transport. */
public interface ScreenFrameDataEvents {
    ScreenFrameDataEvents NO_OP = new ScreenFrameDataEvents() {
        @Override public AutoCloseable subscribe(Consumer<Event> listener) { return () -> { }; }
        @Override public void publish(Event event) { }
    };
    AutoCloseable subscribe(Consumer<Event> listener);
    void publish(Event event);
    sealed interface Event permits FrameReceived, ChannelClosed { }
    record FrameReceived(ScreenFrameDataPort.Identity identity, ScreenFramePacket frame) implements Event { }
    record ChannelClosed(ScreenFrameDataPort.Identity identity) implements Event { }
}
