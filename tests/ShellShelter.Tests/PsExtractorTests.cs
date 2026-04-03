using Shouldly;
using ShellShelter.Core;
using ShellShelter.Core.PowerShell;

namespace ShellShelter.Tests;

/// <summary>
/// Unit tests for <see cref="PsExtractor"/> and <see cref="PsAliasResolver"/>.
/// These tests parse PowerShell syntax in-process and do not require pwsh in PATH.
/// </summary>
public sealed class PsExtractorTests
{
    private readonly PsExtractor _extractor = new();

    // ---------------------------------------------------------------------------
    // Basic command extraction
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_SingleCommand_ReturnsCommandTokens()
    {
        var result = await _extractor.ExtractAsync("Get-ChildItem -Path /tmp");

        result.Commands.ShouldHaveSingleItem();
        result.Commands[0].ShouldBe(new[] { "Get-ChildItem", "-Path", "/tmp" });
    }

    [Fact]
    public async Task ExtractAsync_AliasedCommand_ResolvesToCanonical()
    {
        var result = await _extractor.ExtractAsync("ls /tmp");

        result.Commands.ShouldHaveSingleItem();
        result.Commands[0][0].ShouldBe("Get-ChildItem");
    }

    [Fact]
    public async Task ExtractAsync_CatAlias_ResolvesToGetContent()
    {
        var result = await _extractor.ExtractAsync("cat /etc/hosts");

        result.Commands[0][0].ShouldBe("Get-Content");
    }

    [Fact]
    public async Task ExtractAsync_EchoAlias_ResolvesToWriteOutput()
    {
        var result = await _extractor.ExtractAsync("echo 'hello'");

        result.Commands[0][0].ShouldBe("Write-Output");
    }

    // ---------------------------------------------------------------------------
    // Pipeline extraction
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_Pipeline_ExtractsBothCommands()
    {
        var result = await _extractor.ExtractAsync("Get-Process | Sort-Object CPU");

        result.Commands.Count.ShouldBe(2);
        result.Commands[0][0].ShouldBe("Get-Process");
        result.Commands[1][0].ShouldBe("Sort-Object");
        result.Operators.ShouldContain("|");
    }

    [Fact]
    public async Task ExtractAsync_PipelineChainAnd_DetectsAndAndOperator()
    {
        var result = await _extractor.ExtractAsync("git status && git diff");

        result.Operators.ShouldContain("&&");
    }

    [Fact]
    public async Task ExtractAsync_PipelineChainOr_DetectsOrOrOperator()
    {
        var result = await _extractor.ExtractAsync("git pull || Write-Host 'failed'");

        result.Operators.ShouldContain("||");
    }

    // ---------------------------------------------------------------------------
    // Redirection extraction
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_WriteRedirect_ExtractsDest()
    {
        var result = await _extractor.ExtractAsync("Get-Process > /tmp/procs.txt");

        result.Redirects.ShouldHaveSingleItem();
        result.Redirects[0].Op.ShouldBe(">");
        result.Redirects[0].Dest.ShouldContain("procs.txt");
    }

    [Fact]
    public async Task ExtractAsync_AppendRedirect_ExtractsDest()
    {
        var result = await _extractor.ExtractAsync("Get-Process >> /tmp/procs.txt");

        result.Redirects.ShouldHaveSingleItem();
        result.Redirects[0].Op.ShouldBe(">>");
    }

    // ---------------------------------------------------------------------------
    // Multi-command and nested extraction
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExtractAsync_MultipleCommands_ExtractsAll()
    {
        var result = await _extractor.ExtractAsync("Get-ChildItem; Get-Process; Get-Date");

        result.Commands.Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task ExtractAsync_EmptyCommand_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(
            async () => await _extractor.ExtractAsync(""));
    }

    [Fact]
    public async Task ExtractAsync_NullCommand_ThrowsArgumentException()
    {
        await Should.ThrowAsync<ArgumentException>(
            async () => await _extractor.ExtractAsync(null!));
    }

    // ---------------------------------------------------------------------------
    // PsAliasResolver unit tests
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("ls", "Get-ChildItem")]
    [InlineData("dir", "Get-ChildItem")]
    [InlineData("cat", "Get-Content")]
    [InlineData("cd", "Set-Location")]
    [InlineData("pwd", "Get-Location")]
    [InlineData("rm", "Remove-Item")]
    [InlineData("cp", "Copy-Item")]
    [InlineData("mv", "Move-Item")]
    [InlineData("echo", "Write-Output")]
    [InlineData("kill", "Stop-Process")]
    public void PsAliasResolver_KnownAlias_ResolvesCorrectly(string alias, string expected)
    {
        PsAliasResolver.Resolve(alias).ShouldBe(expected);
    }

    [Fact]
    public void PsAliasResolver_UnknownToken_ReturnsSameToken()
    {
        PsAliasResolver.Resolve("Get-ChildItem").ShouldBe("Get-ChildItem");
        PsAliasResolver.Resolve("nonexistent-cmdlet").ShouldBe("nonexistent-cmdlet");
    }

    [Fact]
    public void PsAliasResolver_AliasLookup_IsCaseInsensitive()
    {
        PsAliasResolver.Resolve("LS").ShouldBe("Get-ChildItem");
        PsAliasResolver.Resolve("Ls").ShouldBe("Get-ChildItem");
    }

    [Fact]
    public void PsAliasResolver_TransitiveAlias_ResolvesToCanonical()
    {
        PsAliasResolver.Resolve("man").ShouldBe("Get-Help");
    }

    [Fact]
    public void PsAliasResolver_IsAlias_ReturnsTrueForKnownAlias()
    {
        PsAliasResolver.IsAlias("ls").ShouldBeTrue();
        PsAliasResolver.IsAlias("Get-ChildItem").ShouldBeFalse();
    }
}
