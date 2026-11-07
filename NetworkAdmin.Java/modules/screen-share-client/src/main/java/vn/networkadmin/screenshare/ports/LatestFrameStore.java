package vn.networkadmin.screenshare.ports;

import java.util.Optional;
import vn.networkadmin.screenshare.domain.CapturedFrame;
import vn.networkadmin.screenshare.domain.CapturedFrameSnapshot;

public interface LatestFrameStore {
    void publish(CapturedFrame frame);
    Optional<CapturedFrameSnapshot> readAfter(long sequence);
}
