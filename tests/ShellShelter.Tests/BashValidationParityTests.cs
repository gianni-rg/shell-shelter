using Shouldly;
using ShellShelter.Core;
using ShellShelter.Core.Config;

namespace ShellShelter.Tests;

/// <summary>
/// Tests that verify parity between the C# ShellValidator and the expected behavior
/// documented in the Python safecmd library.
/// </summary>
/// <remarks>
/// These tests use only in-process logic (no shfmt, no pwsh), relying on the
/// <see cref="ExtractionResult"/> struct directly to simulate extractor output.
/// Full end-to-end parity tests that invoke shfmt are in <see cref="BashExtractorTests"/>.
/// </remarks>
public sealed class BashValidationParityTests
{
    private static ShellPolicy DefaultPolicy()
    {
        var loader = new ConfigLoader();
        return loader.LoadFromText(DefaultConfigs.BashDefaultIni).BashPolicy;
    }

    // ---------------------------------------------------------------------------
    // Allowed commands
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("echo")]
    [InlineData("cat")]
    [InlineData("ls")]
    [InlineData("grep")]
    [InlineData("git status")]
    [InlineData("git log")]
    [InlineData("git diff")]
    [InlineData("cd")]
    [InlineData("pwd")]
    public void ValidateCommand_DefaultPolicyAllowedCommands_AllowsAll(string cmdStr)
    {
        ShellPolicy policy = DefaultPolicy();
        string[] tokens = cmdStr.Split(' ');

        Should.NotThrow(() => ShellValidator.ValidateCommand(tokens, policy.OkCmds));
    }

    // ---------------------------------------------------------------------------
    // Denied commands
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("rm")]
    [InlineData("sudo")]
    [InlineData("chmod")]
    [InlineData("chown")]
    [InlineData("dd")]
    [InlineData("mkfs")]
    [InlineData("fdisk")]
    public void ValidateCommand_DefaultPolicyDeniedCommands_Throws(string cmd)
    {
        ShellPolicy policy = DefaultPolicy();
        string[] tokens = cmd.Split(' ');

        Should.Throw<DisallowedCmdException>(
            () => ShellValidator.ValidateCommand(tokens, policy.OkCmds));
    }

    // ---------------------------------------------------------------------------
    // Destination validation
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("./output.txt")]
    [InlineData("./subdir/file.txt")]
    [InlineData("/tmp/myfile.txt")]
    [InlineData("/dev/null")]
    public void ValidateDestination_DefaultPolicyAllowedDests_AllowsAll(string dest)
    {
        ShellPolicy policy = DefaultPolicy();

        ShellValidator.ValidateDestination(dest, policy.OkDests).ShouldBeTrue();
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/home/user/.ssh/authorized_keys")]
    [InlineData("../escape")]
    [InlineData("/usr/bin/sudo")]
    public void ValidateDestination_DefaultPolicyDisallowedDests_ReturnsFalse(string dest)
    {
        ShellPolicy policy = DefaultPolicy();

        ShellValidator.ValidateDestination(dest, policy.OkDests).ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // Path traversal prevention
    // ---------------------------------------------------------------------------

    [Fact]
    public void ValidateDestination_PathTraversalAttempt_ReturnsFalse()
    {
        ShellPolicy policy = DefaultPolicy();

        // These should not pass even though they start with "./"
        ShellValidator.ValidateDestination("./../etc/passwd", policy.OkDests).ShouldBeFalse();
        ShellValidator.ValidateDestination("./subdir/../../etc/passwd", policy.OkDests).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDestination_TmpTraversalAttempt_ReturnsFalse()
    {
        ShellPolicy policy = DefaultPolicy();

        ShellValidator.ValidateDestination("/tmp/../etc/passwd", policy.OkDests).ShouldBeFalse();
    }

    // ---------------------------------------------------------------------------
    // Full Validate (via ExtractionResult) — parity with Python validate()
    // ---------------------------------------------------------------------------

    [Fact]
    public void Validate_AllowedCommandAndDest_DoesNotThrow()
    {
        ShellPolicy policy = DefaultPolicy();

        var result = new ExtractionResult(
            Commands: new[] { new[] { "echo", "hello" } },
            Operators: new HashSet<string>(),
            Redirects: new[] { (">", "./out.txt") });

        Should.NotThrow(() => ShellValidator.Validate(result, policy));
    }

    [Fact]
    public void Validate_DisallowedCommand_Throws()
    {
        ShellPolicy policy = DefaultPolicy();

        var result = new ExtractionResult(
            Commands: new[] { new[] { "rm", "-rf", "/tmp" } },
            Operators: new HashSet<string>(),
            Redirects: Array.Empty<(string, string)>());

        Should.Throw<DisallowedCmdException>(() => ShellValidator.Validate(result, policy));
    }

    [Fact]
    public void Validate_DisallowedDest_Throws()
    {
        ShellPolicy policy = DefaultPolicy();

        var result = new ExtractionResult(
            Commands: new[] { new[] { "echo", "hello" } },
            Operators: new HashSet<string>(),
            Redirects: new[] { (">", "/etc/passwd") });

        Should.Throw<DisallowedDestException>(() => ShellValidator.Validate(result, policy));
    }

    // ---------------------------------------------------------------------------
    // DefaultConfigs loading
    // ---------------------------------------------------------------------------

    [Fact]
    public void DefaultConfigsIni_ParsesWithNonEmptyPolicy()
    {
        var loader = new ConfigLoader();
        ShellPolicyPair pair = loader.LoadFromText(DefaultConfigs.BashDefaultIni);

        pair.BashPolicy.OkCmds.Count.ShouldBeGreaterThan(0);
        pair.BashPolicy.OkDests.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void DefaultConfigsJson_ParsesWithNonEmptyPolicy()
    {
        var loader = new ConfigLoader();
        ShellPolicyPair pair = loader.LoadFromText(DefaultConfigs.BashDefaultJson);

        pair.BashPolicy.OkCmds.Count.ShouldBeGreaterThan(0);
        pair.BashPolicy.OkDests.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void DefaultConfigsPsIni_ParsesWithNonEmptyPsPolicy()
    {
        var loader = new ConfigLoader();
        ShellPolicyPair pair = loader.LoadFromText(
            DefaultConfigs.BashDefaultIni + "\n" + DefaultConfigs.PsDefaultIni);

        pair.PsPolicy.OkCmds.Count.ShouldBeGreaterThan(0);
        pair.PsPolicy.OkDests.Count.ShouldBeGreaterThan(0);
    }
}
