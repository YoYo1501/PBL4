package vn.networkadmin.screenshare.ports;

import vn.networkadmin.screenshare.domain.CapturedFrame;
import vn.networkadmin.protocol.screenshare.FrameRectangle;

public interface JpegEncoderPort {
    byte[] encode(CapturedFrame frame, FrameRectangle region, float quality);
}
