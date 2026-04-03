namespace ShellShelter.Core.Tools;

/// <summary>
/// Represents the result of a shell tool invocation from an LLM agent.
/// </summary>
/// <remarks>
/// This is the structured response type returned by <see cref="BashTools.Bash"/>,
/// <see cref="BashTools.UnsafeBash"/>, <see cref="PsTools.Pwsh"/>, and related LLM entry points.
/// On success <see cref="Output"/> contains the combined stdout/stderr.
/// On policy failure <see cref="Error"/>, <see cref="AllowedCmds"/>, and <see cref="AllowedDests"/>
/// are populated so the calling agent can report back to the user.
/// </remarks>
/// <param name="IsSuccess">True when the command was validated and executed without error.</param>
/// <param name="Output">The combined stdout + stderr when <see cref="IsSuccess"/> is true.</param>
/// <param name="Error">A human-readable error message when <see cref="IsSuccess"/> is false.</param>
/// <param name="AllowedCmds">Semi-colon-separated list of allowed command specs, included on policy failures.</param>
/// <param name="AllowedDests">Semi-colon-separated list of allowed destinations, included on policy failures.</param>
/// <param name="Suggestion">A hint for the agent on how to proceed after a policy failure.</param>
public sealed record ToolResult(
    bool IsSuccess,
    string? Output = null,
    string? Error = null,
    string? AllowedCmds = null,
    string? AllowedDests = null,
    string? Suggestion = null)
{
    /// <summary>
    /// Creates a successful result with the given output.
    /// </summary>
    public static ToolResult Success(string output) => new(true, Output: output);

    /// <summary>
    /// Creates a policy-failure result with the given error and allowed-list details.
    /// </summary>
    public static ToolResult Denied(
        string error,
        IEnumerable<CmdSpec>? allowedCmds = null,
        IEnumerable<string>? allowedDests = null)
    {
        string? cmds = allowedCmds is not null
            ? string.Join("; ", allowedCmds.Select(c => string.Join(" ", c.Name)))
            : null;
        string? dests = allowedDests is not null
            ? string.Join("; ", allowedDests)
            : null;

        return new(
            false,
            Error: error,
            AllowedCmds: cmds,
            AllowedDests: dests,
            Suggestion: "Rerun using an allowed tool/dest, or ask the user for permission to add it to the allowlist.");
    }

    /// <summary>
    /// Creates an execution-failure result (command was allowed but execution failed).
    /// </summary>
    public static ToolResult ExecutionFailed(string error) =>
        new(false, Error: error);
}
