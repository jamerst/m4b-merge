using FFMpegCodec = FFMpegCore.Enums.Codec;

namespace M4BMerge;

public record EncodingSettings(FFMpegCodec Codec, int? Bitrate);