using System.Diagnostics;

namespace ShellShelter.Core.Bash;

/// <summary>
/// Executes validated bash commands using an explicit bash process.
/// </summary>
/// <remarks>
/// This runner executes commands via an explicit `bash -c` invocation,
/// never using implicit shell execution (shell=true). This prevents
/// accidental execution with unexpected shell environments.
/// </remarks>
public static class BashRunner
{
    private const string BashDefault = "bash";

    /// <summary>
    /// Executes a bash command and returns stdout + stderr combined.
    /// </summary>
    /// <remarks>
    /// The command is executed via `bash -c "cmd"` with stdout and stderr captured and combined.
    /// </remarks>
    /// <param name="cmd">The bash command to execute (should be pre-validated).</param>
    /// <param name="bashPath">Path to the bash executable. Defaults to "bash".</param>
    /// <returns>The combined stdout and stderr output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when bash execution fails or returns non-zero exit code.</exception>
    public static string Run(string cmd, string bashPath = BashDefault)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var (exitCode, output) = RunInternal(cmd, bashPath, throwOnError: true);
        return output;
    }

    /// <summary>
    /// Executes a bash command asynchronously and returns the output.
    /// </summary>
    /// <param name="cmd">The bash command to execute (should be pre-validated).</param>
    /// <param name="bashPath">Path to the bash executable. Defaults to "bash".</param>
    /// <returns>The combined stdout and stderr output.</returns>
    /// <exception cref="InvalidOperationException">Thrown when bash execution fails or returns non-zero exit code.</exception>
    public static async Task<string> RunAsync(string cmd, string bashPath = BashDefault)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var (exitCode, output) = await RunInternalAsync(cmd, bashPath, throwOnError: true);
        return output;
    }

    /// <summary>
    /// Executes a bash command and returns the exit code and output without throwing on non-zero exit.
    /// </summary>
    /// <param name="cmd">The bash command to execute (should be pre-validated).</param>
    /// <param name="bashPath">Path to the bash executable. Defaults to "bash".</param>
    /// <returns>A tuple of (exit code, output).</returns>
    public static (int ExitCode, string Output) RunWithExit(string cmd, string bashPath = BashDefault)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return RunInternal(cmd, bashPath, throwOnError: false);
    }

    /// <summary>
    /// Executes a bash command asynchronously and returns the exit code and output without throwing on non-zero exit.
    /// </summary>
    /// <param name="cmd">The bash command to execute (should be pre-validated).</param>
    /// <param name="bashPath">Path to the bash executable. Defaults to "bash".</param>
    /// <returns>A tuple of (exit code, output).</returns>
    public static async Task<(int ExitCode, string Output)> RunWithExitAsync(
        string cmd,
        string bashPath = BashDefault)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return await RunInternalAsync(cmd, bashPath, throwOnError: false);
    }

    /// <summary>
    /// Internal synchronous execution.
    /// </summary>
    private static (int ExitCode, string Output) RunInternal(string cmd, string bashPath, bool throwOnError)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = bashPath,
                Arguments = $"-c \"{EscapeBashArg(cmd)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        string combinedOutput = stdout + stderr;

        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Bash command failed with exit code {process.ExitCode}. Output: {combinedOutput}");

        return (process.ExitCode, combinedOutput);
    }

    /// <summary>
    /// Internal asynchronous execution.
    /// </summary>
    private static async Task<(int ExitCode, string Output)> RunInternalAsync(
        string cmd,
        string bashPath,
        bool throwOnError)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = bashPath,
                Arguments = $"-c \"{EscapeBashArg(cmd)}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        string combinedOutput = stdout + stderr;

        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Bash command failed with exit code {process.ExitCode}. Output: {combinedOutput}");

        return (process.ExitCode, combinedOutput);
    }

    /// <summary>
    /// Executes a bash command asynchronously and returns exit code with separate stdout and stderr.
    /// </summary>
    /// <param name="cmd">The bash command to execute (should be pre-validated).</param>
    /// <param name="bashPath">Path to the bash executable. Defaults to "bash".</param>
    /// <returns>A tuple of (exit code, stdout, stderr).</returns>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunWithSplitAsync(
        string cmd,
        string bashPath = BashDefault)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = bashPath,
                Arguments = $"-c {EscapeBashArg(cmd)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>
    /// Executes a multi-line bash script by piping it to bash via stdin.
    /// </summary>
    /// <remarks>
    /// Preferred over <c>RunWithSplitAsync</c> for scripts that contain heredocs or newlines,
    /// because no shell-quoting of the script body is needed.
    /// </remarks>
    /// <param name="script">The bash script to execute.</param>
    /// <param name="bashPath">Path to the bash executable. Defaults to "bash".</param>
    /// <returns>A tuple of (exit code, stdout, stderr).</returns>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunScriptAsync(
        string script,
        string bashPath = BashDefault)
    {
        ArgumentNullException.ThrowIfNull(script);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = bashPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        await process.StandardInput.WriteAsync(script);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>
    /// Escapes a string for safe use as a bash argument.
    /// </summary>
    /// <remarks>
    /// This uses single-quote escaping for maximum safety: replaces ' with '\''
    /// (end quote, escaped quote, start quote).
    /// </remarks>
    private static string EscapeBashArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }
}
