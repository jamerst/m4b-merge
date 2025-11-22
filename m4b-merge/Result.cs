using System.Diagnostics.CodeAnalysis;
using FFMpegCore.Exceptions;
using Spectre.Console;

namespace M4BMerge;

public class Result<T>
{
    private Result()
    {
    }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool Ok { get; init; }

    public T? Value { get; init; }

    private FormattableString? Message { get; init; }

    private Exception? Exception { get; init; }

    public void PrintError(bool debug)
    {
        if (Message is not null)
        {
            AnsiConsole.MarkupLineInterpolated(Message);
        }

        if (debug && Exception is not null)
        {
            if (Exception is FFMpegException fe)
            {
                AnsiConsole.MarkupLineInterpolated(
                    $"[bold grey]DBG:[/] [grey] ffmpeg output: {fe.FFMpegErrorOutput}[/]");
            }
            else
            {
                AnsiConsole.WriteException(Exception);
            }
        }
    }

    public static Result<T> Success(T result)
    {
        return new Result<T>
        {
            Ok = true,
            Value = result
        };
    }

    public static Result<T> Fail()
    {
        return new Result<T>
        {
            Ok = false
        };
    }

    public static Result<T> Fail(FormattableString message)
    {
        return Fail(message, null);
    }

    public static Result<T> Fail(FormattableString message, Exception? exception)
    {
        return new Result<T>
        {
            Ok = false,
            Message = message,
            Exception = exception
        };
    }
}