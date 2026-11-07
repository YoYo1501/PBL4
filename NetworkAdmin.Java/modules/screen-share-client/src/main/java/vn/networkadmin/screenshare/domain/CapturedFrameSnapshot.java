package vn.networkadmin.screenshare.domain;

import java.util.Objects;

/** Latest captured display image and its monotonically increasing capture sequence. */
public record CapturedFrameSnapshot(long sequence, CapturedFrame frame) {
    public CapturedFrameSnapshot {
        if (sequence <= 0) throw new IllegalArgumentException("Capture sequence must be positive.");
        Objects.requireNonNull(frame, "frame");
    }
}
