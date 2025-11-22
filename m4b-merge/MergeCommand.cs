using System.Reflection;
using FFMpegCore;
using FFMpegCore.Builders.MetaData;
using FFMpegCore.Enums;
using FFMpegCore.Exceptions;
using Spectre.Console;
using Spectre.Console.Cli;
using FFMpegCodec = FFMpegCore.Enums.Codec;

namespace M4BMerge;

public class MergeCommand : AsyncCommand<Settings>
{
    private static readonly string[] SupportedCodecs =
        [AudioCodec.Aac.Name, AudioCodec.LibMp3Lame.Name, AudioCodec.Mp3.Name, AudioCodec.Flac.Name];

    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken token)
    {
        if (settings.PrintVersion)
        {
            AnsiConsole.WriteLine(GetVersion());
            return 0;
        }

        AnsiConsole.WriteLine($"m4b-merge v{GetVersion()}");
        AnsiConsole.MarkupLine("[link]https://jtattersall.net[/]");

        if (settings.Paths is null || settings.Paths.Length < 2)
        {
            AnsiConsole.MarkupLine("[bold red]ERR:[/] [red]No input files provided[/]");
            return 1;
        }

        if (string.IsNullOrEmpty(settings.OutputPath))
        {
            AnsiConsole.MarkupLine("[bold red]ERR:[/] [red]No output path provided[/]");
            return 1;
        }

        if (settings.Paths.Contains(settings.OutputPath))
        {
            AnsiConsole.MarkupLine("[bold red]ERR:[/] [red]Output path cannot be the same as an input file[/]");
            return 1;
        }

        if (File.Exists(settings.OutputPath))
        {
            var confirmation = AnsiConsole.Prompt(
                new TextPrompt<bool>(
                        $"[bold yellow]WARN:[/] [yellow]Output file {settings.OutputPath} already exists, overwrite?[/]")
                    .AddChoice(true)
                    .AddChoice(false)
                    .DefaultValue(false)
                    .WithConverter(c => c ? "y" : "n")
            );

            if (!confirmation)
            {
                return 0;
            }
        }

        return await AnsiConsole.Status()
            .StartAsync("Loading input files", async ctx =>
            {
                var loadedFiles = await LoadFilesAsync(settings.Paths, settings, token);
                if (!loadedFiles.Ok)
                {
                    return 1;
                }

                var totalLength = loadedFiles.Value.Aggregate(TimeSpan.Zero, (acc, f) => acc.Add(f.Duration));

                var chapters = GenerateMetadata(loadedFiles.Value, settings);

                AnsiConsole.MarkupLineInterpolated(
                    $"[bold]INFO:[/] Loaded {settings.Paths.Length} files, total length {FormatTimeSpan(totalLength)}");

                var convertedFiles = await ConvertFilesAsync(loadedFiles.Value, settings, ctx, token);
                if (!convertedFiles.Ok)
                {
                    return 1;
                }

                var convertedFileCount = convertedFiles.Value.Count(f => f.IsTemporary);
                if (convertedFileCount > 0)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[bold]INFO:[/] Converted {convertedFileCount} file{(convertedFileCount != 1 ? "s" : "")}");
                }

                var mergeSuccess =
                    await MergeFilesAsync(convertedFiles.Value, chapters, totalLength, settings, ctx, token);
                return mergeSuccess ? 0 : 1;
            });
    }

    private async Task<Result<List<InputFile>>> LoadFilesAsync(IEnumerable<string> paths,
        Settings settings, CancellationToken token)
    {
        async Task<Result<InputFile>> LoadFileAsync(string path)
        {
            var fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                return Result<InputFile>.Fail($"[bold red]ERR:[/] [red]File not found: {fullPath}[/]");
            }

            try
            {
                var fileInfo = await FFProbe.AnalyseAsync(fullPath, null, token);

                var result = new InputFile
                {
                    Path = fullPath,
                    Duration = fileInfo.Duration,
                    Codec = fileInfo.AudioStreams[0].GetCodecInfo(),
                    IsM4B = Path.GetExtension(fullPath) == ".m4b",
                    Bitrate = (int)fileInfo.AudioStreams[0].BitRate / 1000
                };

                if (fileInfo.Chapters.Count > 0)
                {
                    result.Chapters = fileInfo.Chapters.Select(c => (c.Duration, c.Title)).ToList();
                }

                return Result<InputFile>.Success(result);
            }
            catch (Exception e)
            {
                return Result<InputFile>.Fail($"[bold red]ERR:[/] [red]Unable to read file: {fullPath}[/]", e);
            }
        }

        var files = await RunInParallelAsync(paths.Select(LoadFileAsync));

        if (files.All(f => f.Ok))
        {
            return Result<List<InputFile>>.Success(files.Select(f => f.Value!).ToList());
        }

        foreach (var failedFile in files.Where(f => !f.Ok))
        {
            failedFile.PrintError(settings.Debug);
        }

        return Result<List<InputFile>>.Fail();
    }

    private ReadOnlyMetaData GenerateMetadata(List<InputFile> files, Settings settings)
    {
        var builder = new MetaDataBuilder();

        var chapterNumber = 1;
        foreach (var file in files)
        {
            if (file.Chapters is not null)
            {
                builder.AddChapters(file.Chapters, c => c);
                chapterNumber += file.Chapters.Count;
            }
            else
            {
                builder.AddChapter(file.Duration, $"Chapter {chapterNumber++}");
            }
        }

        if (settings.Metadata is not null)
        {
            foreach (var metadata in settings.Metadata)
            {
                builder.WithEntry(metadata.Key, metadata.Value);
            }
        }

        return builder.Build();
    }

    private async Task<Result<List<InputFile>>> ConvertFilesAsync(List<InputFile> inputFiles,
        Settings settings,
        StatusContext ctx,
        CancellationToken token)
    {
        var targetCodec = GetTargetCodec(inputFiles, settings);
        if (!targetCodec.Ok)
        {
            return Result<List<InputFile>>.Fail();
        }

        var completedFiles = 0;
        var filesToConvert = inputFiles.Count(f => f.Codec.Name != targetCodec.Value.Codec.Name);
        if (filesToConvert > 0)
        {
            ctx.Status($"Converting files (0/{filesToConvert})");
        }

        async Task<Result<InputFile>> ConvertFileAsync(InputFile file)
        {
            if (file.Codec.Name == targetCodec.Value.Codec.Name)
            {
                return Result<InputFile>.Success(file);
            }

            var tempOutputPath = Path.Join(Path.GetTempPath(),
                Path.GetRandomFileName() + GetTempFileExtension(file, targetCodec.Value.Codec)
            );

            if (settings.Debug)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[bold grey]DBG:[/] [grey]re-encoding {file.Path} as {targetCodec.Value.Bitrate}k {targetCodec.Value.Codec.Name} ({tempOutputPath})[/]");
            }

            try
            {
                var cmd = FFMpegCommands.ConvertFile(file.Path, tempOutputPath, targetCodec.Value.Codec,
                    targetCodec.Value.Bitrate);

                cmd.CancellableThrough(token);

                if (settings.Debug)
                {
                    AnsiConsole.MarkupLineInterpolated($"[bold grey]DBG:[/] [grey]ffmpeg args: {cmd.Arguments}[/]");
                }

                await cmd.ProcessAsynchronously();

                completedFiles++;
                ctx.Status($"Converting files ({completedFiles}/{filesToConvert})");
            }
            catch (Exception e)
            {
                return Result<InputFile>.Fail($"[bold red]ERR:[/] [red]Failed to convert file: {file.Path}[/]", e);
            }

            return Result<InputFile>.Success(file with
            {
                Path = tempOutputPath,
                Codec = targetCodec.Value.Codec,
                IsTemporary = true
            });
        }

        var convertedFiles = await RunInParallelAsync(inputFiles.Select(ConvertFileAsync));

        if (convertedFiles.All(f => f.Ok))
        {
            return Result<List<InputFile>>.Success(convertedFiles.Select(f => f.Value!).ToList());
        }

        foreach (var failedFile in convertedFiles.Where(f => !f.Ok))
        {
            failedFile.PrintError(settings.Debug);
        }

        RemoveTemporaryFiles(convertedFiles.Where(f => f.Ok).Select(f => f.Value!));

        return Result<List<InputFile>>.Fail();
    }

    private async Task<bool> MergeFilesAsync(List<InputFile> files,
        ReadOnlyMetaData metadata,
        TimeSpan totalLength,
        Settings settings,
        StatusContext ctx,
        CancellationToken token)
    {
        ctx.Status("Merging files");

        try
        {
            var cmd = FFMpegCommands.MergeFiles(files.Select(f => f.Path).ToList(), metadata, settings.OutputPath);

            cmd.CancellableThrough(token);
            cmd.NotifyOnProgress(t => ctx.Status($"Merging files ({FormatTimeSpan(t)}/{FormatTimeSpan(totalLength)})"));

            if (settings.Debug)
            {
                AnsiConsole.MarkupLineInterpolated($"[bold grey]DBG:[/] [grey]ffmpeg args: {cmd.Arguments}[/]");
            }

            await cmd.ProcessAsynchronously();
        }
        catch (Exception e)
        {
            AnsiConsole.MarkupLineInterpolated($"[bold red]ERR:[/] [red]Failed to merge files[/]");

            if (settings.Debug)
            {
                if (e is FFMpegException fe)
                {
                    AnsiConsole.MarkupLineInterpolated(
                        $"[bold grey]DBG:[/] [grey]ffmpeg output: {fe.FFMpegErrorOutput}[/]");
                }
                else
                {
                    AnsiConsole.WriteException(e);
                }
            }

            return false;
        }
        finally
        {
            ctx.Status("Removing temporary files");

            RemoveTemporaryFiles(files);
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[bold green]Successfully merged {files.Count} files to {settings.OutputPath}[/]");

        return true;
    }

    private void RemoveTemporaryFiles(IEnumerable<InputFile> files)
    {
        foreach (var tempFile in files.Where(f => f.IsTemporary))
        {
            File.Delete(tempFile.Path);
        }
    }

    private static Result<EncodingSettings> GetTargetCodec(List<InputFile> files, Settings settings)
    {
        // use specified codec if set via options
        if (settings.Codec.HasValue)
        {
            var codec = settings.Codec switch
            {
                Codec.Aac => AudioCodec.Aac,
                Codec.Mp3 => AudioCodec.LibMp3Lame,
                Codec.Flac => AudioCodec.Flac,
                _ => throw new ArgumentException($"Unknown codec {settings.Codec}")
            };

            if (!settings.Bitrate.HasValue && !codec.IsLossless)
            {
                AnsiConsole.MarkupLine(
                    "[bold red]ERR:[/] [red]Bitrate must be specified when specifying lossy codecs[/]");
                return Result<EncodingSettings>.Fail();
            }

            return Result<EncodingSettings>.Success(new EncodingSettings(codec,
                !codec.IsLossless ? settings.Bitrate : null));
        }

        // otherwise use codec with the most files
        var majorityCodec = files
            .Where(f => SupportedCodecs.Contains(f.Codec.Name))
            .GroupBy(f => f.Codec.Name)
            .OrderByDescending(g => g.Count())
            .Select(g =>
            {
                var codec = g.First().Codec;

                return new EncodingSettings(codec, codec.IsLossy ? g.Max(f => f.Bitrate) : null);
            })
            .Take(1)
            .ToList();

        if (majorityCodec.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated(
                $"[bold red]ERR:[/] [red]Input codec is not supported in output container, specify a codec to use[/]");
            return Result<EncodingSettings>.Fail();
        }

        return Result<EncodingSettings>.Success(majorityCodec[0]);
    }

    private static string GetTempFileExtension(InputFile file, FFMpegCodec codec)
    {
        if (file.IsM4B) return ".mp4";

        if (codec.Name == AudioCodec.Aac.Name) return ".m4a";

        if (codec.Name == AudioCodec.LibMp3Lame.Name || codec.Name == AudioCodec.Mp3.Name) return ".mp3";

        if (codec.Name == AudioCodec.Flac.Name) return ".flac";

        throw new ArgumentException($"Unsupported codec {codec.Name}");
    }

    private static async Task<List<T>> RunInParallelAsync<T>(IEnumerable<Task<T>> tasks)
    {
        return (await Task.WhenAll(tasks.Select(t => Task.Run(async () => await t)))).ToList();
    }

    private static string FormatTimeSpan(TimeSpan t)
    {
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    private static string GetVersion()
    {
        return Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString(3)!;
    }
}