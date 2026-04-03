using Shouldly;
using ShellShelter.Core;
using ShellShelter.Core.Bash;

namespace ShellShelter.Tests;

/// <summary>
/// Tests for ShellValidator module.
/// </summary>
public sealed class ValidationTests
{
    private static ShellPolicy GetDefaultPolicy()
    {
        var policy = new ShellPolicy();
        
        // Add common safe commands
        policy.OkCmds.Add(new CmdSpec("echo"));
        policy.OkCmds.Add(new CmdSpec("cat"));
        policy.OkCmds.Add(new CmdSpec("ls"));
        policy.OkCmds.Add(new CmdSpec("grep"));
        policy.OkCmds.Add(new CmdSpec("find", execFlags: new[] { "-exec", "-execdir" }));
        policy.OkCmds.Add(new CmdSpec("curl", destFlags: new[] { "-o", "--output" }));
        
        // Add allowed destinations
        policy.OkDests.Add("./");
        policy.OkDests.Add("/tmp");
        policy.OkDests.Add("/dev/null");

        return policy;
    }

    [Fact]
    public void ValidateCommand_AllowedCommand_DoesNotThrow()
    {
        var policy = GetDefaultPolicy();
        var tokens = new[] { "echo", "hello" };

        Should.NotThrow(() => ShellValidator.ValidateCommand(tokens, policy.OkCmds));
    }

    [Fact]
    public void ValidateCommand_DisallowedCommand_ThrowsDisallowedCmdException()
    {
        var policy = GetDefaultPolicy();
        var tokens = new[] { "rm", "-rf", "/" };

        Should.Throw<DisallowedCmdException>(() =>
            ShellValidator.ValidateCommand(tokens, policy.OkCmds));
    }

    [Fact]
    public void ValidateCommand_EmptyTokens_ThrowsDisallowedCmdException()
    {
        var policy = GetDefaultPolicy();
        var tokens = Array.Empty<string>();

        Should.Throw<DisallowedCmdException>(() =>
            ShellValidator.ValidateCommand(tokens, policy.OkCmds));
    }

    [Fact]
    public void ValidateCommand_PartialCommandMatch_AllowedIfPrefixMatches()
    {
        var policy = new ShellPolicy();
        policy.OkCmds.Add(new CmdSpec("git status"));
        
        var tokens = new[] { "git", "status", "--porcelain" };
        
        Should.NotThrow(() => ShellValidator.ValidateCommand(tokens, policy.OkCmds));
    }

    [Fact]
    public void ValidateDestination_AllowedPath_ReturnsTrue()
    {
        var allowedDests = new HashSet<string> { "./", "/tmp" };
        
        bool result = ShellValidator.ValidateDestination("./output.txt", allowedDests);
        
        result.ShouldBeTrue();
    }

    [Fact]
    public void ValidateDestination_DisallowedPath_ReturnsFalse()
    {
        var allowedDests = new HashSet<string> { "./", "/tmp" };
        
        bool result = ShellValidator.ValidateDestination("/etc/passwd", allowedDests);
        
        result.ShouldBeFalse();
    }

    [Fact]
    public void ValidateDestination_AbsolutePathInAllowedDir_ReturnsTrue()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var allowedDests = new HashSet<string> { "./" };
        var destInCurrentDir = Path.Combine(currentDir, "test.txt");
        
        bool result = ShellValidator.ValidateDestination(destInCurrentDir, allowedDests);
        
        result.ShouldBeTrue();
    }

    [Fact]
    public void ValidateDestination_TmpPath_ReturnsTrue()
    {
        var allowedDests = new HashSet<string> { "/tmp" };
        
        bool result = ShellValidator.ValidateDestination("/tmp/output.log", allowedDests);
        
        result.ShouldBeTrue();
    }

    [Fact]
    public void NormalizeDestination_TildePath_ExpandsToHome()
    {
        string input = "~/test.txt";
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "test.txt");
        
        string result = ShellValidator.NormalizeDestination(input);
        
        result.ShouldBe(expected);
    }

    [Fact]
    public void NormalizeDestination_TildeOnly_ExpandsToHome()
    {
        string input = "~";
        string expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        
        string result = ShellValidator.NormalizeDestination(input);
        
        result.ShouldBe(expected);
    }

    [Fact]
    public void NormalizeDestination_RelativePath_BecomesAbsolute()
    {
        string input = "./test.txt";
        string expected = Path.Combine(Directory.GetCurrentDirectory(), "test.txt");
        
        string result = ShellValidator.NormalizeDestination(input);
        
        result.ShouldBe(expected);
    }

    [Fact]
    public void NormalizeDestination_EnvironmentVariable_Expanded()
    {
        var tmpDir = Path.GetTempPath();
        Environment.SetEnvironmentVariable("TEST_DIR", tmpDir);
        
        string input = "$TEST_DIR/test.txt";
        string result = ShellValidator.NormalizeDestination(input);
        
        result.ShouldStartWith(tmpDir);
    }

    [Fact]
    public void Validate_AllowedCommand_DoesNotThrow()
    {
        var policy = GetDefaultPolicy();
        var extraction = new ExtractionResult(
            new[] { new[] { "echo", "hello" }.AsReadOnly() }.AsReadOnly(),
            new HashSet<string>(),
            new List<(string, string)>());

        Should.NotThrow(() => ShellValidator.Validate(extraction, policy));
    }

    [Fact]
    public void Validate_DisallowedCommand_ThrowsDisallowedCmdException()
    {
        var policy = GetDefaultPolicy();
        var extraction = new ExtractionResult(
            new[] { new[] { "rm", "-rf", "/" }.AsReadOnly() }.AsReadOnly(),
            new HashSet<string>(),
            new List<(string, string)>());

        Should.Throw<DisallowedCmdException>(() => ShellValidator.Validate(extraction, policy));
    }

    [Fact]
    public void Validate_DisallowedRedirectDest_ThrowsDisallowedDestException()
    {
        var policy = GetDefaultPolicy();
        var extraction = new ExtractionResult(
            new[] { new[] { "echo", "hello" }.AsReadOnly() }.AsReadOnly(),
            new HashSet<string> { ">" },
            new List<(string, string)> { (">", "/etc/passwd") });

        Should.Throw<DisallowedDestException>(() => ShellValidator.Validate(extraction, policy));
    }

    [Fact]
    public void Validate_AllowedRedirectDest_DoesNotThrow()
    {
        var policy = GetDefaultPolicy();
        var extraction = new ExtractionResult(
            new[] { new[] { "echo", "hello" }.AsReadOnly() }.AsReadOnly(),
            new HashSet<string> { ">" },
            new List<(string, string)> { (">", "./output.txt") });

        Should.NotThrow(() => ShellValidator.Validate(extraction, policy));
    }
}
