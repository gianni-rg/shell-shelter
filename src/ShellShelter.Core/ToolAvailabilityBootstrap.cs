using System.Collections.Concurrent;
using System.Diagnostics;

namespace ShellShelter.Core;

/// <summary>
/// Handles runtime checks for required external tools with a first-use install prompt.
/// </summary>
internal static class ToolAvailabilityBootstrap
{
    private static readonly ConcurrentDictionary<string, int> MissingToolCounts =
        new(StringComparer.OrdinalIgnoreCase);

    private const string ShfmtInstallHint =
        "Install shfmt: winget install -e --id mvdan.shfmt (Windows), brew install shfmt (macOS), apt-get install shfmt (Linux).";

    private const string PwshInstallHint =
        "Install PowerShell: winget install -e --id Microsoft.PowerShell (Windows), brew install --cask powershell (macOS), https://aka.ms/install-powershell (Linux).";

    /// <summary>
    /// Ensures <c>shfmt</c> is available. On first absence, optionally prompts for auto-install.
    /// </summary>
    internal static void EnsureShfmtAvailable(string shfmtPath)
    {
        EnsureToolAvailable(
            toolPath: shfmtPath,
            toolKeyPrefix: "shfmt",
            toolDisplayName: "shfmt",
            installHint: ShfmtInstallHint,
            tryInstall: TryInstallShfmt,
            exceptionFactory: message => new ShfmtNotFoundException(message));
    }

    /// <summary>
    /// Ensures <c>pwsh</c> is available. On first absence, optionally prompts for auto-install.
    /// </summary>
    internal static void EnsurePwshAvailable(string pwshPath)
    {
        EnsureToolAvailable(
            toolPath: pwshPath,
            toolKeyPrefix: "pwsh",
            toolDisplayName: "pwsh",
            installHint: PwshInstallHint,
            tryInstall: TryInstallPwsh,
            exceptionFactory: message => new PwshNotFoundException(pwshPath, message));
    }

    private static void EnsureToolAvailable(
        string toolPath,
        string toolKeyPrefix,
        string toolDisplayName,
        string installHint,
        Func<bool> tryInstall,
        Func<string, Exception> exceptionFactory)
    {
        if (string.IsNullOrWhiteSpace(toolPath))
            throw new ArgumentException("Tool path cannot be null or whitespace.", nameof(toolPath));

        if (IsExecutableAvailable(toolPath))
            return;

        string key = $"{toolKeyPrefix}|{toolPath}";
        int missCount = MissingToolCounts.AddOrUpdate(key, 1, (_, current) => current + 1);

        bool firstMiss = missCount == 1;
        if (firstMiss && CanPrompt() && PromptInstall(toolDisplayName))
        {
            _ = tryInstall();
            if (IsExecutableAvailable(toolPath))
            {
                MissingToolCounts.TryRemove(key, out _);
                return;
            }
        }

        string prefix = firstMiss
            ? $"The '{toolPath}' executable was not found in PATH."
            : $"The '{toolPath}' executable is still unavailable after a previous install prompt.";

        throw exceptionFactory($"{prefix} {installHint}");
    }

    private static bool IsExecutableAvailable(string toolPath)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = toolPath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null)
                return false;

            process.WaitForExit(1500);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool CanPrompt()
    {
        return Environment.UserInteractive && !Console.IsInputRedirected && !Console.IsOutputRedirected;
    }

    private static bool PromptInstall(string toolDisplayName)
    {
        Console.Write($"ShellShelter requires '{toolDisplayName}'. Attempt automatic installation now? [y/N]: ");
        string? answer = Console.ReadLine();
        return string.Equals(answer?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryInstallShfmt()
    {
        if (OperatingSystem.IsWindows())
            return RunInstaller("winget", "install -e --id mvdan.shfmt --accept-source-agreements --accept-package-agreements");

        if (OperatingSystem.IsMacOS())
            return RunInstaller("brew", "install shfmt");

        if (OperatingSystem.IsLinux())
            return RunInstaller("apt-get", "install -y shfmt");

        return false;
    }

    private static bool TryInstallPwsh()
    {
        if (OperatingSystem.IsWindows())
            return RunInstaller("winget", "install -e --id Microsoft.PowerShell --accept-source-agreements --accept-package-agreements");

        if (OperatingSystem.IsMacOS())
            return RunInstaller("brew", "install --cask powershell");

        // Linux installs vary heavily by distro/repo setup. Keep this best-effort and non-destructive.
        return false;
    }

    private static bool RunInstaller(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
            });

            if (process is null)
                return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
