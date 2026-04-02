namespace ShellShelter.Core;

/// <summary>
/// Represents a domain error raised by ShellShelter validation or policy operations.
/// </summary>
public class ShellShelterException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShellShelterException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public ShellShelterException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShellShelterException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ShellShelterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Represents an error raised when a command token sequence is not allowed by policy.
/// </summary>
public sealed class DisallowedCmdException : ShellShelterException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DisallowedCmdException"/> class.
    /// </summary>
    /// <param name="commandTokens">The rejected command tokens.</param>
    public DisallowedCmdException(IEnumerable<string> commandTokens)
        : this(commandTokens, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DisallowedCmdException"/> class.
    /// </summary>
    /// <param name="commandTokens">The rejected command tokens.</param>
    /// <param name="message">An optional custom message.</param>
    public DisallowedCmdException(IEnumerable<string> commandTokens, string? message)
        : base(message ?? BuildMessage(commandTokens))
    {
        CommandTokens = commandTokens?.ToArray() ?? throw new ArgumentNullException(nameof(commandTokens));
    }

    /// <summary>
    /// Gets the rejected command tokens.
    /// </summary>
    public IReadOnlyList<string> CommandTokens { get; }

    private static string BuildMessage(IEnumerable<string> commandTokens)
    {
        ArgumentNullException.ThrowIfNull(commandTokens);

        string[] tokens = commandTokens.ToArray();
        return tokens.Length == 0
            ? "Disallowed command."
            : $"Disallowed command: {string.Join(" ", tokens)}";
    }
}

/// <summary>
/// Represents an error raised when a write destination is not allowed by policy.
/// </summary>
public sealed class DisallowedDestException : ShellShelterException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DisallowedDestException"/> class.
    /// </summary>
    /// <param name="destination">The rejected destination.</param>
    public DisallowedDestException(string destination)
        : this(destination, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DisallowedDestException"/> class.
    /// </summary>
    /// <param name="destination">The rejected destination.</param>
    /// <param name="message">An optional custom message.</param>
    public DisallowedDestException(string destination, string? message)
        : base(message ?? $"Disallowed destination: {destination}")
    {
        Destination = string.IsNullOrWhiteSpace(destination)
            ? throw new ArgumentException("Destination cannot be null or whitespace.", nameof(destination))
            : destination;
    }

    /// <summary>
    /// Gets the rejected destination.
    /// </summary>
    public string Destination { get; }
}

/// <summary>
/// Represents an error raised when <c>shfmt</c> is required but not available.
/// </summary>
public sealed class ShfmtNotFoundException : FileNotFoundException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShfmtNotFoundException"/> class.
    /// </summary>
    public ShfmtNotFoundException()
        : base(
            "The 'shfmt' executable was not found in PATH. Install shfmt before using bash extraction features.",
            "shfmt")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ShfmtNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public ShfmtNotFoundException(string message)
        : base(message, "shfmt")
    {
    }
}

/// <summary>
/// Represents an error raised when PowerShell Core (<c>pwsh</c>) is required but not available.
/// </summary>
public sealed class PwshNotFoundException : FileNotFoundException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PwshNotFoundException"/> class.
    /// </summary>
    public PwshNotFoundException()
        : base(
            "The 'pwsh' executable was not found in PATH. Install PowerShell Core before using PowerShell execution features.",
            "pwsh")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PwshNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public PwshNotFoundException(string message)
        : base(message, "pwsh")
    {
    }
}
