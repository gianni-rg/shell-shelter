namespace ShellShelter.Core.Bash;

/// <summary>
/// High-level API for safely running bash commands.
/// </summary>
/// <remarks>
/// This class provides the main entry points for validating and executing bash commands
/// against an allowed-list policy. It orchestrates extraction, validation, and execution.
/// </remarks>
public static class BashShell
{
    /// <summary>
    /// Validates a bash command against a shell policy without executing it.
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Extracts commands, operators, and redirects using shfmt AST parsing
    /// 2. Validates each command against allowed commands
    /// 3. Validates all redirect destinations against allowed patterns
    /// 4. Throws if any command or destination is not allowed
    /// </remarks>
    /// <param name="cmd">The bash command string to validate.</param>
    /// <param name="policy">The shell policy containing allowed commands and destinations.</param>
    /// <param name="execFlags">Optional exec flags mapping for recursive command extraction.</param>
    /// <param name="destFlags">Optional dest flags mapping for redirect destination extraction.</param>
    /// <param name="execPos">Optional exec position mapping for recursive command extraction.</param>
    /// <param name="destPos">Optional dest position mapping for destination extraction.</param>
    /// <exception cref="DisallowedCmdException">Thrown when a command is not allowed.</exception>
    /// <exception cref="DisallowedDestException">Thrown when a redirect destination is not allowed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when extraction fails (e.g., shfmt not found).</exception>
    public static async Task ValidateAsync(
        string cmd,
        ShellPolicy policy,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? execFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? destFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? execPos = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? destPos = null)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(policy);

        var result = await BashExtractor.ExtractAsync(cmd, execFlags: execFlags, destFlags: destFlags, execPos: execPos, destPos: destPos);
        ShellValidator.Validate(result, policy);
    }

    /// <summary>
    /// Extracts commands, operators, and redirects from a bash command without validation.
    /// </summary>
    /// <remarks>
    /// This method uses shfmt to parse the bash AST and extract all components.
    /// No allow-list validation is performed.
    /// </remarks>
    /// <param name="cmd">The bash command string to extract from.</param>
    /// <param name="execFlags">Optional exec flags mapping for recursive command extraction.</param>
    /// <param name="destFlags">Optional dest flags mapping for redirect destination extraction.</param>
    /// <param name="execPos">Optional exec position mapping for recursive command extraction.</param>
    /// <param name="destPos">Optional dest position mapping for destination extraction.</param>
    /// <returns>An ExtractionResult containing all extracted commands, operators, and redirects.</returns>
    /// <exception cref="InvalidOperationException">Thrown when extraction fails (e.g., shfmt not found).</exception>
    public static async Task<ExtractionResult> ExtractAsync(
        string cmd,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? execFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? destFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? execPos = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? destPos = null)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return await BashExtractor.ExtractAsync(cmd, execFlags: execFlags, destFlags: destFlags, execPos: execPos, destPos: destPos);
    }

    /// <summary>
    /// Safely runs a bash command: validates it, then executes if allowed.
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Validates the command using ValidateAsync
    /// 2. If validation passes, executes via BashRunner
    /// 3. Returns the combined stdout/stderr output
    /// 4. Throws on validation failure or execution failure
    /// </remarks>
    /// <param name="cmd">The bash command string to execute.</param>
    /// <param name="policy">The shell policy for validation.</param>
    /// <param name="bashPath">Optional path to bash executable. Defaults to "bash".</param>
    /// <param name="execFlags">Optional exec flags mapping.</param>
    /// <param name="destFlags">Optional dest flags mapping.</param>
    /// <param name="execPos">Optional exec position mapping.</param>
    /// <param name="destPos">Optional dest position mapping.</param>
    /// <returns>The combined stdout + stderr from command execution.</returns>
    /// <exception cref="DisallowedCmdException">Thrown when a command is not allowed.</exception>
    /// <exception cref="DisallowedDestException">Thrown when a destination is not allowed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when bash execution fails.</exception>
    public static async Task<string> SafeRunAsync(
        string cmd,
        ShellPolicy policy,
        string bashPath = "bash",
        IReadOnlyDictionary<string, IReadOnlySet<string>>? execFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? destFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? execPos = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? destPos = null)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(policy);

        // Validate first
        await ValidateAsync(cmd, policy, execFlags, destFlags, execPos, destPos);

        // Execute if validation passed
        return await BashRunner.RunAsync(cmd, bashPath);
    }

    /// <summary>
    /// Validates a bash script, then executes it via stdin-piped bash, returning split stdout/stderr.
    /// </summary>
    /// <remarks>
    /// Intended for multi-line scripts (e.g. heredoc-based ex commands) where shell-quoting of
    /// the entire body would be unsafe. The script is piped to bash via stdin rather than passed
    /// as a <c>-c</c> argument so heredoc syntax is preserved without escaping.
    /// </remarks>
    /// <param name="script">The bash script to validate and execute.</param>
    /// <param name="policy">The shell policy for validation.</param>
    /// <param name="bashPath">Optional path to bash executable. Defaults to "bash".</param>
    /// <returns>A tuple of (exit code, stdout, stderr).</returns>
    public static async Task<(int ExitCode, string StdOut, string StdErr)> RunCapturedAsync(
        string script,
        ShellPolicy policy,
        string bashPath = "bash")
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentNullException.ThrowIfNull(policy);

        await ValidateAsync(script, policy);
        return await BashRunner.RunScriptAsync(script, bashPath);
    }

    /// <summary>
    /// Builds the exec/dest flag and positional extraction maps from a policy's command specs.
    /// </summary>
    /// <remarks>
    /// These maps drive <see cref="BashExtractor"/>'s recursive command and redirect-destination
    /// discovery (e.g. <c>curl -o</c>, <c>find ... -exec</c>). Without them, nested commands and
    /// flag-based destinations declared in the policy's <see cref="CmdSpec"/> entries are not
    /// recursively validated.
    /// </remarks>
    /// <param name="policy">The shell policy whose command specs supply the extraction metadata.</param>
    public static (
        IReadOnlyDictionary<string, IReadOnlySet<string>> ExecFlags,
        IReadOnlyDictionary<string, IReadOnlySet<string>> DestFlags,
        IReadOnlyDictionary<string, IReadOnlySet<int>> ExecPos,
        IReadOnlyDictionary<string, IReadOnlySet<int>> DestPos)
        BuildExtractionMaps(ShellPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var execFlags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var destFlags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var execPos = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var destPos = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (CmdSpec spec in policy.OkCmds)
        {
            if (spec.Name.Count == 0)
                continue;

            string cmdName = string.Join(" ", spec.Name);

            AddStringMapValues(execFlags, cmdName, spec.ExecFlags);
            AddStringMapValues(destFlags, cmdName, spec.DestFlags);
            AddIntMapValues(execPos, cmdName, spec.ExecPos);
            AddIntMapValues(destPos, cmdName, spec.DestPos);
        }

        return (
            execFlags.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<string>)kvp.Value,
                StringComparer.Ordinal),
            destFlags.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<string>)kvp.Value,
                StringComparer.Ordinal),
            execPos.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<int>)kvp.Value,
                StringComparer.Ordinal),
            destPos.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<int>)kvp.Value,
                StringComparer.Ordinal));
    }

    private static void AddStringMapValues(
        IDictionary<string, HashSet<string>> map,
        string key,
        IReadOnlySet<string> values)
    {
        if (values.Count == 0)
            return;

        if (!map.TryGetValue(key, out var existing))
        {
            existing = new HashSet<string>(StringComparer.Ordinal);
            map[key] = existing;
        }

        existing.UnionWith(values);
    }

    private static void AddIntMapValues(
        IDictionary<string, HashSet<int>> map,
        string key,
        IReadOnlySet<int> values)
    {
        if (values.Count == 0)
            return;

        if (!map.TryGetValue(key, out var existing))
        {
            existing = new HashSet<int>();
            map[key] = existing;
        }

        existing.UnionWith(values);
    }

    /// <summary>
    /// Synchronous wrapper for SafeRunAsync.
    /// </summary>
    public static string SafeRun(
        string cmd,
        ShellPolicy policy,
        string bashPath = "bash",
        IReadOnlyDictionary<string, IReadOnlySet<string>>? execFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? destFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? execPos = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? destPos = null)
    {
        return SafeRunAsync(cmd, policy, bashPath, execFlags, destFlags, execPos, destPos)
            .GetAwaiter()
            .GetResult();
    }
}
