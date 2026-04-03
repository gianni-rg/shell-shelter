using System.ComponentModel;
using ShellShelter.Core.Config;
using ShellShelter.Core.PowerShell;

namespace ShellShelter.Core.Tools;

/// <summary>
/// LLM-callable tools for safely executing PowerShell commands against an allowlist policy.
/// </summary>
/// <remarks>
/// All public methods are designed to be used as MEAI tool functions and are annotated with
/// <see cref="DescriptionAttribute"/> for LLM tool discovery.
/// Alias resolution via <see cref="PsAliasResolver"/> is applied automatically by
/// <see cref="PsExtractor"/> before allowlist validation, so the allowlist only needs canonical names.
/// </remarks>
public static class PsTools
{
    private static readonly ConfigLoader _configLoader = new();

    /// <summary>
    /// Run a PowerShell command safely and return the combined stdout and stderr.
    /// </summary>
    /// <remarks>
    /// The command is parsed using the PowerShell language parser and all commands are validated
    /// against an allowlist before execution. Aliases are resolved to canonical cmdlet names
    /// during validation. If the command is not allowed, STOP and inform the user of the command
    /// run and error details so they can decide whether to add it to the allowlist.
    /// <para><c>rmCmds</c>/<c>rmDests</c> params are comma-separated strings.</para>
    /// </remarks>
    [Description("Run a PowerShell command safely against an allowlist. Parse and validate all commands before execution. " +
                 "Aliases are resolved automatically. On policy failure return error details.")]
    public static async Task<ToolResult> Pwsh(
        [Description("PowerShell command string to execute.")] string cmd,
        [Description("Comma-separated cmdlet names to remove from the allowlist for this call only.")] string? rmCmds = null,
        [Description("Comma-separated destination patterns to remove from the allowlist for this call only.")] string? rmDests = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(cmd);

        ShellPolicy policy = LoadPsPolicy().WithRemove(rmCmds, rmDests);

        try
        {
            string output = await PsShell.SafeRunAsync(cmd, policy);
            return ToolResult.Success(output);
        }
        catch (DisallowedCmdException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (DisallowedDestException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (Exception ex)
        {
            return ToolResult.ExecutionFailed(ex.Message);
        }
    }

    /// <summary>
    /// Run a PowerShell command safely with explicit or modified policy overrides.
    /// </summary>
    /// <remarks>
    /// Use only with explicit user permission when the default allowlist is insufficient.
    /// The <c>cmds</c> and <c>dests</c> parameters replace the entire policy.
    /// <c>addCmds</c>/<c>addDests</c> extend it; <c>rmCmds</c>/<c>rmDests</c> shrink it.
    /// </remarks>
    [Description("Run a PowerShell command with explicit allowlist overrides. " +
                 "DO NOT USE without prior user permission for the specific override being applied.")]
    public static async Task<ToolResult> UnsafePwsh(
        [Description("PowerShell command string to execute.")] string cmd,
        [Description("Full replacement command allowlist (comma-separated cmdlet names). Defaults to ok_cmds when null.")] string? cmds = null,
        [Description("Full replacement destination allowlist (comma-separated). Defaults to ok_dests when null.")] string? dests = null,
        [Description("Comma-separated cmdlet names to add to the effective allowlist.")] string? addCmds = null,
        [Description("Comma-separated destination patterns to add to the effective allowlist.")] string? addDests = null,
        [Description("Comma-separated cmdlet names to remove from the effective allowlist.")] string? rmCmds = null,
        [Description("Comma-separated destination patterns to remove from the effective allowlist.")] string? rmDests = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(cmd);

        ShellPolicy basePolicy = cmds is not null || dests is not null
            ? BuildOverridePolicy(cmds, dests)
            : LoadPsPolicy();

        ShellPolicy policy = basePolicy.WithAdd(addCmds, addDests).WithRemove(rmCmds, rmDests);

        try
        {
            string output = await PsShell.SafeRunAsync(cmd, policy);
            return ToolResult.Success(output);
        }
        catch (DisallowedCmdException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (DisallowedDestException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (Exception ex)
        {
            return ToolResult.ExecutionFailed(ex.Message);
        }
    }

    /// <summary>
    /// Read file content with optional line numbers using PowerShell <c>Get-Content</c>.
    /// </summary>
    /// <remarks>
    /// Validates the path is accessible (must be an allowed destination or already exist on disk).
    /// Line numbers are prepended using right-aligned formatting.
    /// </remarks>
    [Description("Read a file's content using PowerShell Get-Content. Optionally prepend line numbers.")]
    public static async Task<ToolResult> ReadFile(
        [Description("Path to the file to read.")] string path,
        [Description("When true, prepend each line with its line number.")] bool lineNumbers = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        ShellPolicy policy = LoadPsPolicy().WithAdd(addCmds: "Get-Content, ForEach-Object");
        string normalizedPath = ShellValidator.NormalizeDestination(path);
        bool destAllowed = ShellValidator.ValidateDestination(path, policy.OkDests);
        bool existsOnDisk = File.Exists(normalizedPath);

        if (!destAllowed && !existsOnDisk)
        {
            var denyEx = new DisallowedDestException(path);
            return ToolResult.Denied(denyEx.Message, policy.OkCmds, policy.OkDests);
        }

        string cmd = lineNumbers
            ? $"Get-Content -Path {QuotePsArg(path)} | ForEach-Object -Begin {{ $i = 0 }} -Process {{ $i++; \"{{0:D}} {{1}}\" -f $i, $_ }}"
            : $"Get-Content -Path {QuotePsArg(path)}";

        try
        {
            string output = await PsShell.SafeRunAsync(cmd, policy);
            return ToolResult.Success(output);
        }
        catch (DisallowedCmdException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (Exception ex)
        {
            return ToolResult.ExecutionFailed(ex.Message);
        }
    }

    /// <summary>
    /// Replace text in a file using PowerShell <c>Get-Content</c> and <c>Set-Content</c>.
    /// </summary>
    /// <remarks>
    /// Uses PowerShell regex replacement. The file path must match an allowed destination.
    /// The operation is equivalent to <c>Get-Content file | ForEach-Object { $_ -replace pattern, replacement } | Set-Content file</c>.
    /// </remarks>
    [Description("Replace text matching a regex pattern in a file using PowerShell Set-Content. " +
                 "The file path must be in an allowed destination.")]
    public static async Task<ToolResult> ReplaceInFile(
        [Description("Path to the file to modify.")] string path,
        [Description("PowerShell regex pattern to match.")] string pattern,
        [Description("Replacement string (supports PowerShell capture group references like $1).")] string replacement)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(replacement);

        ShellPolicy policy = LoadPsPolicy();

        // Validate destination before running
        bool destAllowed = ShellValidator.ValidateDestination(path, policy.OkDests);

        if (!destAllowed)
        {
            var denyEx = new DisallowedDestException(path);
            return ToolResult.Denied(denyEx.Message, policy.OkCmds, policy.OkDests);
        }

        // Add Set-Content to the policy for this call (it's a write operation)
        ShellPolicy writePolicy = policy.WithAdd(addCmds: "Set-Content, Get-Content, ForEach-Object");

        string cmd = $"(Get-Content -Path {QuotePsArg(path)}) | " +
                     $"ForEach-Object {{ $_ -replace {QuotePsArg(pattern)}, {QuotePsArg(replacement)} }} | " +
                     $"Set-Content -Path {QuotePsArg(path)}";

        try
        {
            string output = await PsShell.SafeRunAsync(cmd, writePolicy);
            return ToolResult.Success(output.Trim().Length > 0 ? output : $"Applied to {path}");
        }
        catch (DisallowedCmdException ex)
        {
            return ToolResult.Denied(ex.Message, writePolicy.OkCmds, writePolicy.OkDests);
        }
        catch (DisallowedDestException ex)
        {
            return ToolResult.Denied(ex.Message, writePolicy.OkCmds, writePolicy.OkDests);
        }
        catch (Exception ex)
        {
            return ToolResult.ExecutionFailed(ex.Message);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ShellPolicy LoadPsPolicy()
    {
        string configPath = ConfigLoader.GetDefaultConfigPath();
        if (File.Exists(configPath))
        {
            try
            {
                return _configLoader.Load(configPath).PsPolicy;
            }
            catch
            {
                // Fall back to built-in defaults if config is malformed
            }
        }

        return _configLoader.LoadFromText(DefaultConfigs.BashDefaultIni + "\n" + DefaultConfigs.PsDefaultIni).PsPolicy;
    }

    private static ShellPolicy BuildOverridePolicy(string? cmds, string? dests)
    {
        var okCmds = new HashSet<CmdSpec>();
        var okDests = new HashSet<string>();

        if (cmds is not null)
        {
            foreach (string spec in cmds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                okCmds.Add(CmdSpec.FromStr(spec));
        }

        if (dests is not null)
        {
            foreach (string dest in dests.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                okDests.Add(dest);
        }

        return new ShellPolicy(okCmds, okDests);
    }

    /// <summary>
    /// Wraps a string in PowerShell single-quote safety. Single quotes cannot appear inside
    /// PS single-quoted strings, so they are doubled: <c>'</c> → <c>''</c>.
    /// </summary>
    private static string QuotePsArg(string arg) =>
        "'" + arg.Replace("'", "''") + "'";
}
