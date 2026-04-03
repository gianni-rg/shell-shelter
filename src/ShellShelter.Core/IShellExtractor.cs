namespace ShellShelter.Core;

/// <summary>
/// Defines the contract for shell command extractors.
/// </summary>
/// <remarks>
/// Both <c>BashExtractor</c> (shfmt AST walker) and <c>PsExtractor</c>
/// (System.Management.Automation AST walker) implement this interface so the
/// shared validation engine can be shell-agnostic.
/// </remarks>
public interface IShellExtractor
{
    /// <summary>
    /// Extracts all commands, operators, and write-redirect destinations from a shell command string.
    /// </summary>
    /// <param name="cmd">The shell command string to extract from.</param>
    /// <returns>An <see cref="ExtractionResult"/> containing commands, operators, and redirects.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cmd"/> is null or empty.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the underlying parser is not available.</exception>
    Task<ExtractionResult> ExtractAsync(string cmd);
}
