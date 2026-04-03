namespace ShellShelter.Core;

/// <summary>
/// Represents the result of extracting commands from a bash command string.
/// </summary>
/// <remarks>
/// Contains the list of all executable commands, a set of operators used,
/// and a list of write redirect destinations.
/// </remarks>
public sealed record ExtractionResult(
    IReadOnlyList<IReadOnlyList<string>> Commands,
    IReadOnlySet<string> Operators,
    IReadOnlyList<(string Op, string Dest)> Redirects)
{
    /// <summary>
    /// Gets the list of all commands that would be executed.
    /// </summary>
    /// <remarks>
    /// Each command is represented as a list of tokens (strings).
    /// </remarks>
    public IReadOnlyList<IReadOnlyList<string>> Commands { get; } = Commands;

    /// <summary>
    /// Gets the set of all operators used in the command.
    /// </summary>
    /// <remarks>
    /// Examples: "&amp;&amp;", "||", "|", ";", "&amp;", "=", "&gt;", etc.
    /// </remarks>
    public IReadOnlySet<string> Operators { get; } = Operators;

    /// <summary>
    /// Gets the list of write redirect destinations.
    /// </summary>
    /// <remarks>
    /// Each tuple contains (operator, destination), e.g., ("&gt;", "output.txt").
    /// </remarks>
    public IReadOnlyList<(string Op, string Dest)> Redirects { get; } = Redirects;
}
