using System.Text.RegularExpressions;

namespace ShellShelter.Core.Bash;

using ShellShelter.Core;

/// <summary>
/// Validates bash commands against shell policies using extracted command data.
/// </summary>
/// <remarks>
/// This validator takes ExtractionResult (from BashExtractor) and a ShellPolicy,
/// and validates that all commands and redirect destinations are allowed.
/// </remarks>
public static class ShellValidator
{
    /// <summary>
    /// Validates that all commands in the extraction result are allowed.
    /// </summary>
    /// <param name="result">The extraction result from BashExtractor.</param>
    /// <param name="policy">The shell policy containing allowed commands and destinations.</param>
    /// <exception cref="DisallowedCmdException">Thrown when a command is not allowed.</exception>
    /// <exception cref="DisallowedDestException">Thrown when a redirect destination is not allowed.</exception>
    public static void Validate(ExtractionResult result, ShellPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(policy);

        // Validate each command
        foreach (var commandTokens in result.Commands)
        {
            ValidateCommand(commandTokens, policy.OkCmds);
        }

        // Validate destination arguments in each command
        foreach (var commandTokens in result.Commands)
        {
            ValidateDestinationArgs(commandTokens, policy.OkCmds, policy.OkDests);
        }

        // Validate redirect destinations
        foreach (var (_, dest) in result.Redirects)
        {
            if (!ValidateDestination(dest, policy.OkDests))
                throw new DisallowedDestException(dest);
        }
    }

    /// <summary>
    /// Validates a single command against the allowed commands set.
    /// </summary>
    /// <param name="tokens">The command tokens to validate.</param>
    /// <param name="allowedCommands">The set of allowed commands.</param>
    /// <exception cref="DisallowedCmdException">Thrown when the command is not allowed.</exception>
    public static void ValidateCommand(IReadOnlyList<string> tokens, IReadOnlySet<CmdSpec> allowedCommands)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(allowedCommands);

        if (tokens.Count == 0)
            throw new DisallowedCmdException(tokens);

        bool isAllowed = allowedCommands.Any(spec => spec.IsAllowed(tokens));
        if (!isAllowed)
            throw new DisallowedCmdException(tokens);
    }

    /// <summary>
    /// Validates destination arguments for a command based on its CmdSpec metadata.
    /// </summary>
    /// <param name="tokens">The command tokens.</param>
    /// <param name="allowedCommands">The set of allowed commands.</param>
    /// <param name="allowedDestinations">The set of allowed destination patterns.</param>
    /// <exception cref="DisallowedDestException">Thrown when a destination is not allowed.</exception>
    private static void ValidateDestinationArgs(
        IReadOnlyList<string> tokens,
        IReadOnlySet<CmdSpec> allowedCommands,
        IReadOnlySet<string> allowedDestinations)
    {
        // Find the matching CmdSpec for this command
        var matchingSpec = allowedCommands.FirstOrDefault(spec => spec.IsAllowed(tokens));
        if (matchingSpec is null)
            return; // Command validation has already failed elsewhere

        // Get the arguments (skip the command name prefix)
        var args = tokens.Skip(matchingSpec.Name.Count).ToList();

        // Validate positional destination arguments
        foreach (int pos in matchingSpec.DestPos)
        {
            int actualIdx = pos < 0 ? args.Count + pos : pos;
            if (actualIdx < 0 || actualIdx >= args.Count)
                throw new DisallowedDestException(tokens[actualIdx < 0 ? 0 : actualIdx]);

            string dest = args[actualIdx];
            if (!ValidateDestination(dest, allowedDestinations))
                throw new DisallowedDestException(dest);
        }
    }

    /// <summary>
    /// Validates a destination path against allowed destination patterns.
    /// </summary>
    /// <param name="destination">The destination path to validate.</param>
    /// <param name="allowedPatterns">The set of allowed destination patterns.</param>
    /// <returns>True if the destination matches an allowed pattern; otherwise, false.</returns>
    public static bool ValidateDestination(string destination, IReadOnlySet<string> allowedPatterns)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(allowedPatterns);

        string normalizedDest = NormalizeDestination(destination);

        foreach (string pattern in allowedPatterns)
        {
            string normalizedPattern = NormalizeDestination(pattern);
            if (normalizedDest.StartsWith(normalizedPattern, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Normalizes a destination path by expanding environment variables, home directory,
    /// and converting to absolute paths.
    /// </summary>
    /// <remarks>
    /// Process:
    /// 1. Expand ~ to user's home directory
    /// 2. Expand environment variables ($VAR on Unix, %VAR% on Windows)
    /// 3. Convert to absolute path using Path.GetFullPath
    /// 4. Normalize path separators based on OS
    /// </remarks>
    /// <param name="destination">The destination path to normalize.</param>
    /// <returns>The normalized absolute path.</returns>
    public static string NormalizeDestination(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // Expand ~ to home directory
        if (destination.StartsWith("~"))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (destination == "~")
                destination = home;
            else if (destination.StartsWith("~/") || destination.StartsWith("~\\"))
                destination = Path.Combine(home, destination[2..]);
        }

        // Expand environment variables (supports both $VAR and %VAR% formats)
        destination = ExpandEnvironmentVariables(destination);

        // Convert to absolute path
        string absolutePath = Path.GetFullPath(destination);

        // Ensure consistent path separator
        return absolutePath;
    }

    /// <summary>
    /// Expands environment variables in a string.
    /// Supports both Unix ($VAR) and Windows (%VAR%) formats.
    /// </summary>
    private static string ExpandEnvironmentVariables(string input)
    {
        // Windows-style expansion: %VAR%
        string expanded = Environment.ExpandEnvironmentVariables(input);

        // Unix-style expansion: $VAR or ${VAR}
        // Match $VAR or ${VAR} but not $$ (which is literal $)
        expanded = Regex.Replace(expanded, @"\$\{([A-Za-z_][A-Za-z0-9_]*)\}|\$([A-Za-z_][A-Za-z0-9_]*)", match =>
        {
            string varName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return Environment.GetEnvironmentVariable(varName) ?? match.Value;
        });

        return expanded;
    }
}
