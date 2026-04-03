using System.Diagnostics;

namespace ShellShelter.Core.PowerShell;

/// <summary>
/// Executes validated PowerShell commands using an out-of-process <c>pwsh</c> invocation.
/// </summary>
/// <remarks>
/// All commands are executed via <c>pwsh -NonInteractive -Command &lt;cmd&gt;</c>. The command string
/// is passed as a separate argument, never interpolated into a shell string, to prevent injection.
/// If the legacy Windows PowerShell (<c>powershell.exe</c>, v5) is detected instead of PowerShell
/// Core (&gt;= 7), a <see cref="PwshNotFoundException"/> is thrown with an upgrade hint.
/// </remarks>
public static class PsRunner
{
    private const string PwshDefault = "pwsh";

    /// <summary>
    /// Executes a PowerShell command and returns the combined stdout + stderr.
    /// </summary>
    /// <param name="cmd">The PowerShell command to execute (should be pre-validated).</param>
    /// <param name="pwshPath">Path to the pwsh executable. Defaults to <c>pwsh</c>.</param>
    /// <returns>The combined stdout and stderr output.</returns>
    /// <exception cref="PwshNotFoundException">Thrown when pwsh is not found in PATH.</exception>
    /// <exception cref="InvalidOperationException">Thrown when pwsh returns a non-zero exit code.</exception>
    public static async Task<string> RunAsync(string cmd, string pwshPath = PwshDefault)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        EnsurePwsh(pwshPath);
        var (exitCode, output) = await RunInternalAsync(cmd, pwshPath, throwOnError: true);
        return output;
    }

    /// <summary>
    /// Executes a PowerShell command and returns the exit code plus combined output.
    /// Does not throw on non-zero exit.
    /// </summary>
    /// <param name="cmd">The PowerShell command to execute.</param>
    /// <param name="pwshPath">Path to the pwsh executable. Defaults to <c>pwsh</c>.</param>
    /// <returns>A tuple of (exit code, combined output).</returns>
    /// <exception cref="PwshNotFoundException">Thrown when pwsh is not found in PATH.</exception>
    public static async Task<(int ExitCode, string Output)> RunWithExitAsync(
        string cmd,
        string pwshPath = PwshDefault)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        EnsurePwsh(pwshPath);
        return await RunInternalAsync(cmd, pwshPath, throwOnError: false);
    }

    /// <summary>
    /// Executes a PowerShell command and returns the exit code with stdout and stderr separated.
    /// </summary>
    /// <param name="cmd">The PowerShell command to execute (should be pre-validated).</param>
    /// <param name="pwshPath">Path to the pwsh executable. Defaults to <c>pwsh</c>.</param>
    /// <returns>A tuple of (exit code, stdout, stderr).</returns>
    /// <exception cref="PwshNotFoundException">Thrown when pwsh is not found in PATH.</exception>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunWithSplitAsync(
        string cmd,
        string pwshPath = PwshDefault)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        EnsurePwsh(pwshPath);

        using var process = CreateProcess(cmd, pwshPath);
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    // ---------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------

    private static async Task<(int ExitCode, string Output)> RunInternalAsync(
        string cmd,
        string pwshPath,
        bool throwOnError)
    {
        using var process = CreateProcess(cmd, pwshPath);
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        await process.WaitForExitAsync();

        string output = await stdoutTask + await stderrTask;

        if (throwOnError && process.ExitCode != 0)
            throw new InvalidOperationException(
                $"pwsh command failed with exit code {process.ExitCode}. Output: {output}");

        return (process.ExitCode, output);
    }

    private static Process CreateProcess(string cmd, string pwshPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = pwshPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // Pass -NonInteractive and -Command separately to avoid shell string interpolation.
        // The command is the final argument — pwsh treats everything after -Command as the script.
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(cmd);

        return new Process { StartInfo = psi };
    }

    /// <summary>
    /// Verifies that <c>pwsh</c> (PowerShell Core 7+) is available in PATH.
    /// Throws <see cref="PwshNotFoundException"/> with an install hint if it is not found.
    /// </summary>
    private static void EnsurePwsh(string pwshPath)
    {
        ToolAvailabilityBootstrap.EnsurePwshAvailable(pwshPath);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pwshPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("$PSVersionTable.PSVersion.Major");

            using var process = Process.Start(psi)
                ?? throw new PwshNotFoundException(pwshPath);

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            if (!int.TryParse(output.Trim(), out int major) || major < 7)
                throw new PwshNotFoundException(pwshPath,
                    "PowerShell Core 7 or later is required. " +
                    "Install it via 'winget install Microsoft.PowerShell' (Windows) " +
                    "or https://aka.ms/install-powershell (other platforms).");
        }
        catch (PwshNotFoundException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PwshNotFoundException(pwshPath, hint: null, innerException: ex);
        }
    }
}
