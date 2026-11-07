package vn.networkadmin.screenshare.ports;

import vn.networkadmin.screenshare.domain.CapturedFrame;

public interface ScreenCapturePort {
    CapturedFrame capture();
}
