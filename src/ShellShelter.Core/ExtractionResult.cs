namespace ShellShelter.Core;

/// <summary>
/// Represents the result of extracting commands from a shell command string.
/// </summary>
/// <remarks>
/// Contains the list of all executable commands, a set of operators used,
/// and a list of write redirect destinations.
/// </remarks>
public sealed record ExtractionResult(
    IReadOnlyList<IReadOnlyList<string>> Commands,
    IReadOnlySet<string> Operators,
    IReadOnlyList<(string Op, string Dest)> Redirects);
