package vn.networkadmin.screenshare.infrastructure;

import java.util.Optional;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;
import vn.networkadmin.screenshare.domain.CapturedFrame;
import vn.networkadmin.screenshare.domain.CapturedFrameSnapshot;
import vn.networkadmin.screenshare.ports.LatestFrameStore;

/** Lock-free latest-frame handoff; newer captures replace frames the sender has not consumed. */
public final class AtomicLatestFrameStore implements LatestFrameStore {
    private final AtomicLong nextSequence = new AtomicLong();
    private final AtomicReference<CapturedFrameSnapshot> latest = new AtomicReference<>();

    @Override
    public void publish(CapturedFrame frame) {
        long sequence = nextSequence.incrementAndGet();
        latest.set(new CapturedFrameSnapshot(sequence, frame));
    }

    @Override
    public Optional<CapturedFrameSnapshot> readAfter(long sequence) {
        CapturedFrameSnapshot snapshot = latest.get();
        return snapshot == null || snapshot.sequence() <= sequence
                ? Optional.empty()
                : Optional.of(snapshot);
    }
}
