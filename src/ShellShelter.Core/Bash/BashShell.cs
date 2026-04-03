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
        return SafeRunAsync(cmd, policy, bashPath, execFlags, destFlags, execPos, destPos).Result;
    }
}
