using Shouldly;
using ShellShelter.Core;

namespace ShellShelter.Tests;

public sealed class CmdSpecTests
{
    [Fact]
    public void Constructor_NameContainsExtraWhitespace_SplitsUsingPythonSemantics()
    {
        CmdSpec spec = new("  git   log \t ");

        spec.Name.ShouldBe(["git", "log"]);
    }

    [Fact]
    public void FromStr_PlainCommand_ReturnsEquivalentSpec()
    {
        CmdSpec spec = CmdSpec.FromStr("cat");

        spec.ShouldBe(new CmdSpec("cat"));
    }

    [Fact]
    public void FromStr_WithDeniedExecAndDestSections_ParsesFlagsAndPositions()
    {
        CmdSpec spec = CmdSpec.FromStr("find:-delete|-ok:exec=-exec|-execdir|$0:dest=-o|$-1");

        spec.Name.ShouldBe(["find"]);
        spec.Denied.ShouldBe(["-delete", "-ok"]);
        spec.ExecFlags.ShouldBe(["-exec", "-execdir"]);
        spec.ExecPos.ShouldBe([0]);
        spec.DestFlags.ShouldBe(["-o"]);
        spec.DestPos.ShouldBe([-1]);
    }

    [Fact]
    public void EqualsAndGetHashCode_SameNameDifferentMetadata_AreEqualByNameOnly()
    {
        CmdSpec left = new("find", denied: ["-delete"]);
        CmdSpec right = new("find", execFlags: ["-exec"]);

        left.ShouldBe(right);
        left.GetHashCode().ShouldBe(right.GetHashCode());
    }

    [Fact]
    public void IsAllowed_PrefixDoesNotMatch_ReturnsFalse()
    {
        CmdSpec spec = new("git status");

        spec.IsAllowed(["git", "log"]).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowed_NoDeniedFlagsAndPrefixMatches_ReturnsTrue()
    {
        CmdSpec spec = new("git status");

        spec.IsAllowed(["git", "status", "--short"]).ShouldBeTrue();
    }

    [Fact]
    public void IsAllowed_DeniedExactFlag_ReturnsFalse()
    {
        CmdSpec spec = new("find", denied: ["-delete"]);

        spec.IsAllowed(["find", ".", "-delete"]).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowed_DeniedLongFlagValueForm_ReturnsFalse()
    {
        CmdSpec spec = new("tar", denied: ["--to-command"]);

        spec.IsAllowed(["tar", "--to-command=cat"]).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowed_DeniedCombinedShortFlag_ReturnsFalse()
    {
        CmdSpec spec = new("tar", denied: ["-I"]);

        spec.IsAllowed(["tar", "-xvfI", "zstd"]).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowed_AllowedCombinedShortFlagsWithoutDeniedChar_ReturnsTrue()
    {
        CmdSpec spec = new("tar", denied: ["-I"]);

        spec.IsAllowed(["tar", "-xvf", "file.tar"]).ShouldBeTrue();
    }
}
