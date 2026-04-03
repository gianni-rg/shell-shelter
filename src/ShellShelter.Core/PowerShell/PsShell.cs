namespace ShellShelter.Core.PowerShell;

/// <summary>
/// High-level API for safely running PowerShell commands.
/// </summary>
/// <remarks>
/// This class provides the main entry points for validating and executing PowerShell commands
/// against an allowed-list policy, mirroring the role of <c>BashShell</c> for the PS stack.
/// </remarks>
public static class PsShell
{
    private static readonly PsExtractor _extractor = new();

    /// <summary>
    /// Validates a PowerShell command against a shell policy without executing it.
    /// </summary>
    /// <param name="cmd">The PowerShell command string to validate.</param>
    /// <param name="policy">The shell policy containing allowed commands and destinations.</param>
    /// <exception cref="DisallowedCmdException">Thrown when a command is not allowed.</exception>
    /// <exception cref="DisallowedDestException">Thrown when a redirect destination is not allowed.</exception>
    public static async Task ValidateAsync(string cmd, ShellPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(policy);

        ExtractionResult result = await _extractor.ExtractAsync(cmd);
        ShellValidator.Validate(
            result,
            policy,
            destinationFlagNamesCaseInsensitive: true,
            allowPowerShellFlagColonAssignment: true);
    }

    /// <summary>
    /// Extracts commands, operators, and redirects from a PowerShell command without validation.
    /// </summary>
    /// <param name="cmd">The PowerShell command string to extract from.</param>
    /// <returns>An <see cref="ExtractionResult"/> containing all extracted data.</returns>
    public static Task<ExtractionResult> ExtractAsync(string cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        return _extractor.ExtractAsync(cmd);
    }

    /// <summary>
    /// Validates a PowerShell command, then executes it if allowed.
    /// </summary>
    /// <param name="cmd">The PowerShell command string to validate and execute.</param>
    /// <param name="policy">The shell policy for validation.</param>
    /// <param name="pwshPath">Optional path to the pwsh executable. Defaults to <c>pwsh</c>.</param>
    /// <returns>The combined stdout + stderr output from the command.</returns>
    /// <exception cref="DisallowedCmdException">Thrown when a command is not allowed.</exception>
    /// <exception cref="DisallowedDestException">Thrown when a destination is not allowed.</exception>
    /// <exception cref="PwshNotFoundException">Thrown when pwsh is not found.</exception>
    public static async Task<string> SafeRunAsync(
        string cmd,
        ShellPolicy policy,
        string pwshPath = "pwsh")
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(policy);

        await ValidateAsync(cmd, policy);
        return await PsRunner.RunAsync(cmd, pwshPath);
    }
}
