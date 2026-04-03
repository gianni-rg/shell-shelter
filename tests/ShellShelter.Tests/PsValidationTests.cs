using Shouldly;
using ShellShelter.Core;
using ShellShelter.Core.PowerShell;

namespace ShellShelter.Tests;

/// <summary>
/// Unit tests for PS validation: <see cref="PsShell.ValidateAsync"/> against <see cref="ShellValidator"/>.
/// These tests use the in-process PS parser and do not require pwsh in PATH.
/// </summary>
public sealed class PsValidationTests
{
    private static ShellPolicy PsPolicy() =>
        new ShellPolicy(
            okCmds:
            [
                new CmdSpec("Get-ChildItem"),
                new CmdSpec("Get-Content"),
                new CmdSpec("Sort-Object"),
                new CmdSpec("Where-Object"),
                new CmdSpec("Write-Output"),
                new CmdSpec("Get-Process"),
                new CmdSpec("Tee-Object", destFlags: ["-FilePath"]),
            ],
            okDests:
            [
                "./",
                ".\\",
                "/tmp",
            ]);

    // ---------------------------------------------------------------------------
    // Allowed commands validate without exception
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_AllowedCommand_DoesNotThrow()
    {
        await Should.NotThrowAsync(
            async () => await PsShell.ValidateAsync("Get-ChildItem -Path /tmp", PsPolicy()));
    }

    [Fact]
    public async Task ValidateAsync_AllowedPipeline_DoesNotThrow()
    {
        await Should.NotThrowAsync(
            async () => await PsShell.ValidateAsync(
                "Get-Process | Sort-Object CPU",
                PsPolicy()));
    }

    [Fact]
    public async Task ValidateAsync_AliasedCommand_ResolvesAndValidates()
    {
        // "ls" resolves to "Get-ChildItem" which is in the policy
        await Should.NotThrowAsync(
            async () => await PsShell.ValidateAsync("ls /tmp", PsPolicy()));
    }

    // ---------------------------------------------------------------------------
    // Disallowed commands
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_DisallowedCommand_ThrowsDisallowedCmdException()
    {
        await Should.ThrowAsync<DisallowedCmdException>(
            async () => await PsShell.ValidateAsync("Remove-Item /nonexistent/path", PsPolicy()));
    }

    [Fact]
    public async Task ValidateAsync_DisallowedAlias_ThrowsAfterResolution()
    {
        // "rm" resolves to "Remove-Item" which is not in the test policy
        await Should.ThrowAsync<DisallowedCmdException>(
            async () => await PsShell.ValidateAsync("rm /nonexistent/path", PsPolicy()));
    }

    [Fact]
    public async Task ValidateAsync_PipelineWithDisallowedCommand_Throws()
    {
        await Should.ThrowAsync<DisallowedCmdException>(
            async () => await PsShell.ValidateAsync(
                "Get-ChildItem | Remove-Item",
                PsPolicy()));
    }

    // ---------------------------------------------------------------------------
    // Redirect destination validation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_AllowedRedirectDest_DoesNotThrow()
    {
        await Should.NotThrowAsync(
            async () => await PsShell.ValidateAsync(
                "Get-Process > /tmp/procs.txt",
                PsPolicy()));
    }

    [Fact]
    public async Task ValidateAsync_DisallowedRedirectDest_ThrowsDisallowedDestException()
    {
        await Should.ThrowAsync<DisallowedDestException>(
            async () => await PsShell.ValidateAsync(
                "Get-Process > /etc/passwd",
                PsPolicy()));
    }

    // ---------------------------------------------------------------------------
    // ShellPolicy.WithRemove / WithAdd string overloads
    // ---------------------------------------------------------------------------

    [Fact]
    public void ShellPolicy_WithRemoveString_RemovesCmd()
    {
        ShellPolicy base_ = PsPolicy();
        ShellPolicy reduced = base_.WithRemove(removeCmds: "Get-ChildItem");

        reduced.OkCmds.ShouldNotContain(c => c.Name.SequenceEqual(new[] { "Get-ChildItem" }));
    }

    [Fact]
    public void ShellPolicy_WithAddString_AddsCmd()
    {
        ShellPolicy base_ = PsPolicy();
        ShellPolicy extended = base_.WithAdd(addCmds: "Set-Content");

        extended.OkCmds.ShouldContain(c => c.Name.SequenceEqual(new[] { "Set-Content" }));
    }

    [Fact]
    public void ShellPolicy_WithAddStringDests_AddsDest()
    {
        ShellPolicy base_ = PsPolicy();
        ShellPolicy extended = base_.WithAdd(addCmds: null, addDests: "/var/log");

        extended.OkDests.ShouldContain("/var/log");
    }
}
