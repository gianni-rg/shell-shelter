namespace ShellShelter.Core.Config;

/// <summary>
/// Represents the loaded shell policies for bash and PowerShell.
/// </summary>
/// <param name="BashPolicy">The loaded bash policy.</param>
/// <param name="PsPolicy">The loaded PowerShell policy.</param>
public sealed record ShellPolicyPair(ShellPolicy BashPolicy, ShellPolicy PsPolicy);
