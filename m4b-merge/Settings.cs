using System.ComponentModel;
using Spectre.Console.Cli;

namespace M4BMerge;

public class Settings : CommandSettings
{
    [CommandArgument(0, "[paths]")]
    [Description("Files to process")]
    public required string[]? Paths { get; init; }

    [CommandOption("-o|--output")]
    [Description("Output file path")]
    public required string OutputPath { get; init; }

    [CommandOption("-c|--codec")]
    [Description("Output file audio codec override (aac|mp3|flac)")]
    public Codec? Codec { get; init; }

    [CommandOption("-b|--bitrate")]
    [Description("Output file bitrate (in kb) - required if lossy codec specified")]
    public int? Bitrate { get; init; }

    [CommandOption("-m|--metadata <key=value>")]
    [Description(
        "Additional metadata values to set on output file (note: custom tag keys are not supported, only those known to ffmpeg)")]
    public IDictionary<string, string>? Metadata { get; init; }

    [CommandOption("--debug")]
    [Description("Enable debugging output mode")]
    public bool Debug { get; init; }

    [CommandOption("--version")]
    [Description("Print version and exit")]
    public bool PrintVersion { get; init; }
}