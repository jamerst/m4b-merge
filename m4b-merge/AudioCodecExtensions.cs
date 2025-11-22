using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCodec = FFMpegCore.Enums.Codec;

namespace M4BMerge;

public static class AudioCodecExtensions
{
    extension(AudioCodec)
    {
        public static FFMpegCodec Flac => FFMpeg.GetCodec("flac");

        // not sure why this is needed - ffprobe has decided to start detecting codec as "mp3" instead of "libmp3lame"
        // "mp3" isn't a defined codec in the ffmpeg docs though, presumably it's an alias for libmp3lame?
        public static FFMpegCodec Mp3 => FFMpeg.GetCodec("mp3");
    }
}