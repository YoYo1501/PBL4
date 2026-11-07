package vn.networkadmin.screenshare.server.ports;

import vn.networkadmin.screenshare.server.domain.DecodedJpeg;

public interface JpegDecoderPort {
    DecodedJpeg decode(byte[] jpeg);
}
