using System.Text.RegularExpressions;

namespace ShellShelter.Core;

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
        // Use the most specific matching specs (longest command prefix) for deterministic validation.
        var matchingSpecs = allowedCommands.Where(spec => spec.IsAllowed(tokens)).ToList();
        if (matchingSpecs.Count == 0)
            return; // Command validation has already failed elsewhere

        int maxPrefixLength = matchingSpecs.Max(spec => spec.Name.Count);
        var relevantSpecs = matchingSpecs.Where(spec => spec.Name.Count == maxPrefixLength).ToList();

        // Get the arguments (skip the command name prefix)
        var args = tokens.Skip(maxPrefixLength).ToList();

        foreach (CmdSpec spec in relevantSpecs)
        {
            ValidatePositionalDestinations(spec, args, tokens, allowedDestinations);
            ValidateFlagDestinations(spec, args, tokens, allowedDestinations);
        }
    }

    private static void ValidatePositionalDestinations(
        CmdSpec spec,
        IReadOnlyList<string> args,
        IReadOnlyList<string> tokens,
        IReadOnlySet<string> allowedDestinations)
    {
        foreach (int pos in spec.DestPos)
        {
            int actualIdx = pos < 0 ? args.Count + pos : pos;
            if (actualIdx < 0 || actualIdx >= args.Count)
            {
                throw new DisallowedDestException(
                    string.Join(" ", tokens),
                    $"Missing or invalid destination argument at position {pos} for command: {string.Join(" ", tokens)}");
            }

            string dest = args[actualIdx];
            if (!ValidateDestination(dest, allowedDestinations))
                throw new DisallowedDestException(dest);
        }
    }

    private static void ValidateFlagDestinations(
        CmdSpec spec,
        IReadOnlyList<string> args,
        IReadOnlyList<string> tokens,
        IReadOnlySet<string> allowedDestinations)
    {
        foreach (string flag in spec.DestFlags)
        {
            for (int idx = 0; idx < args.Count; idx++)
            {
                if (!TryGetDestinationForFlag(flag, args, idx, tokens, out string destination))
                    continue;

                if (!ValidateDestination(destination, allowedDestinations))
                    throw new DisallowedDestException(destination);
            }
        }
    }

    private static bool TryGetDestinationForFlag(
        string flag,
        IReadOnlyList<string> args,
        int idx,
        IReadOnlyList<string> tokens,
        out string destination)
    {
        destination = string.Empty;
        string arg = args[idx];

        if (string.Equals(arg, flag, StringComparison.Ordinal))
        {
            int destIdx = idx + 1;
            if (destIdx >= args.Count)
            {
                throw new DisallowedDestException(
                    string.Join(" ", tokens),
                    $"Missing destination argument after flag {flag} for command: {string.Join(" ", tokens)}");
            }

            destination = args[destIdx];
            return true;
        }

        string prefix = flag + "=";
        if (!arg.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string destValue = arg[prefix.Length..];
        if (string.IsNullOrWhiteSpace(destValue))
        {
            throw new DisallowedDestException(
                string.Join(" ", tokens),
                $"Missing destination value for flag {flag} in command: {string.Join(" ", tokens)}");
        }

        destination = destValue;
        return true;
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
        string canonicalDest = CanonicalizeForPolicyMatch(normalizedDest);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string pattern in allowedPatterns)
        {
            string normalizedPattern = NormalizeDestination(pattern);
            string canonicalPattern = CanonicalizeForPolicyMatch(normalizedPattern);
            if (MatchesAllowedDestinationPrefix(canonicalDest, canonicalPattern, comparison))
                return true;
        }

        return false;
    }

    private static bool MatchesAllowedDestinationPrefix(string destination, string allowedPrefix, StringComparison comparison)
    {
        if (string.Equals(destination, allowedPrefix, comparison))
            return true;

        if (!destination.StartsWith(allowedPrefix, comparison))
            return false;

        if (allowedPrefix.EndsWith(Path.DirectorySeparatorChar)
            || allowedPrefix.EndsWith(Path.AltDirectorySeparatorChar))
            return true;

        if (destination.Length <= allowedPrefix.Length)
            return false;

        char nextChar = destination[allowedPrefix.Length];
        return nextChar == Path.DirectorySeparatorChar || nextChar == Path.AltDirectorySeparatorChar;
    }

    private static string CanonicalizeForPolicyMatch(string absolutePath)
    {
        string canonicalPath = ResolveExistingPathOrParent(absolutePath);
        return Path.GetFullPath(canonicalPath);
    }

    private static string ResolveExistingPathOrParent(string absolutePath)
    {
        if (File.Exists(absolutePath) || Directory.Exists(absolutePath))
            return ResolveLinkTargetOrSelf(absolutePath);

        string? parent = Path.GetDirectoryName(absolutePath);
        if (string.IsNullOrEmpty(parent))
            return absolutePath;

        string resolvedParent = ResolveExistingPathOrParent(parent);
        string leaf = Path.GetFileName(absolutePath);
        return string.IsNullOrEmpty(leaf)
            ? resolvedParent
            : Path.Combine(resolvedParent, leaf);
    }

    private static string ResolveLinkTargetOrSelf(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var directoryInfo = new DirectoryInfo(path);
                return directoryInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directoryInfo.FullName;
            }

            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return fileInfo.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? fileInfo.FullName;
            }
        }
        catch
        {
            // Fall back to lexical normalization if link resolution is unavailable.
        }

        return path;
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
        if (destination.StartsWith('~'))
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

        // PowerShell-style expansion: $env:VAR or ${env:VAR}
        expanded = Regex.Replace(expanded, @"\$\{env:([A-Za-z_][A-Za-z0-9_]*)\}|\$env:([A-Za-z_][A-Za-z0-9_]*)", match =>
        {
            string varName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return Environment.GetEnvironmentVariable(varName) ?? match.Value;
        });

        // Protect escaped dollar signs so Unix-style expansion does not match
        // from the second '$' in sequences like $$HOME.
        const string escapedDollarPlaceholder = "\u0000ESCAPED_DOLLAR\u0000";
        expanded = expanded.Replace("$$", escapedDollarPlaceholder);

        // Unix-style expansion: $VAR or ${VAR}
        expanded = Regex.Replace(expanded, @"(?<!\$)\$\{([A-Za-z_][A-Za-z0-9_]*)\}|(?<!\$)\$([A-Za-z_][A-Za-z0-9_]*)", match =>
        {
            string varName = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return Environment.GetEnvironmentVariable(varName) ?? match.Value;
        });

        // Restore escaped dollar signs as literal dollars.
        expanded = expanded.Replace(escapedDollarPlaceholder, "$");

        return expanded;
    }
}
