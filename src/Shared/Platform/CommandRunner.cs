using System.Diagnostics;

namespace Axorith.Shared.Platform;

internal readonly record struct CommandResult(int ExitCode, string Output, string Error);

internal static class CommandRunner
{
    public static CommandResult Run(string fileName, IEnumerable<string>? arguments = null, string? input = null,
        int timeoutMs = 5000)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardInput = input != null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (arguments != null)
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start {fileName}");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (input != null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} did not exit within {timeoutMs}ms");
        }

        return new CommandResult(process.ExitCode, outputTask.GetAwaiter().GetResult(),
            errorTask.GetAwaiter().GetResult());
    }

    public static string RunChecked(string fileName, IEnumerable<string>? arguments = null, string? input = null,
        int timeoutMs = 5000)
    {
        var result = Run(fileName, arguments, input, timeoutMs);
        return result.ExitCode == 0
            ? result.Output
            : throw new InvalidOperationException($"{fileName} failed: {result.Error}");
    }
}
