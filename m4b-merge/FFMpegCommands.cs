using FFMpegCore;
using FFMpegCore.Builders.MetaData;
using FFMpegCore.Enums;
using FFMpegCodec = FFMpegCore.Enums.Codec;

namespace M4BMerge;

public static class FFMpegCommands
{
    public static FFMpegArgumentProcessor ConvertFile(string inputPath,
        string outputPath,
        FFMpegCodec codec,
        int? bitrate)
    {
        return FFMpegArguments.FromFileInput(inputPath)
            .MapMetaData(0)
            .OutputToFile(outputPath, true,
                options =>
                {
                    options.WithCopyCodec()
                        .WithAudioCodec(codec);

                    if (bitrate.HasValue)
                    {
                        options.WithAudioBitrate(bitrate.Value);
                    }
                }
            );
    }

    public static FFMpegArgumentProcessor MergeFiles(List<string> files,
        ReadOnlyMetaData metadata,
        string outputPath)
    {
        return FFMpegArguments.FromDemuxConcatInput(files)
            .AddFileInput(files[0]) // add first file to take metadata from - concat strips all metadata
            .AddMetaData(metadata) // add chapters and custom metadata
            .MapMetaData(
                1, // metadata from first file
                options =>
                    options
                        // remove track number - not relevant for merged file
                        .WithCustomArgument(@"-metadata track=""""")

                        // use chapters from new metadata in case first file also has chapters
                        .WithCustomArgument("-map_chapters 2")
            )
            .OutputToFile(outputPath,
                true,
                options => options
                    .WithCopyCodec()
                    .ForceFormat(VideoType.Mp4)
                    .SelectStream(0, 0, Channel.Audio) // audio from concat files
                    .WithCustomArgument("-map 1:v:0?") // video (artwork) from first file, if present
            );
    }
}