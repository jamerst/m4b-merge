using FFMpegCodec = FFMpegCore.Enums.Codec;

namespace M4BMerge;

public record InputFile
{
    public required string Path { get; init; }

    public TimeSpan Duration { get; init; }

    public required FFMpegCodec Codec { get; init; }

    public int Bitrate { get; init; }

    public bool IsM4B { get; init; }

    public bool IsTemporary { get; init; }

    public List<(TimeSpan, string)>? Chapters { get; set; }
}