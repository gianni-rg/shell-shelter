using Shouldly;
using ShellShelter.Core;

namespace ShellShelter.Tests;

public sealed class ShellPolicyTests
{
    [Fact]
    public void Constructor_WithValues_DeduplicatesCommandsByNameAndTrimsDestinations()
    {
        ShellPolicy policy = new(
            [new CmdSpec("cat"), new CmdSpec("cat"), new CmdSpec("git status")],
            [" ./ ", "./", null!, "/tmp"]);

        policy.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("cat"), new CmdSpec("git status")]);
        policy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/tmp"]);
    }

    [Fact]
    public void WithAdd_WithValues_ReturnsModifiedCloneWithoutChangingOriginal()
    {
        ShellPolicy policy = new([new CmdSpec("cat")], ["./"]);

        ShellPolicy clone = policy.WithAdd([new CmdSpec("git status")], ["/tmp"]);

        clone.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("cat"), new CmdSpec("git status")]);
        clone.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/tmp"]);
        policy.OkCmds.ShouldBe([new CmdSpec("cat")]);
        policy.OkDests.ShouldBe(["./"]);
    }

    [Fact]
    public void WithRemove_WithValues_ReturnsModifiedCloneWithoutChangingOriginal()
    {
        ShellPolicy policy = new([new CmdSpec("cat"), new CmdSpec("git status")], ["./", "/tmp"]);

        ShellPolicy clone = policy.WithRemove([new CmdSpec("git status")], ["/tmp"]);

        clone.OkCmds.ShouldBe([new CmdSpec("cat")]);
        clone.OkDests.ShouldBe(["./"]);
        policy.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("cat"), new CmdSpec("git status")]);
        policy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/tmp"]);
    }

    [Fact]
    public void Constructor_ConflictingDuplicateCmdSpec_MergesMetadataConservatively()
    {
        ShellPolicy policy = new(
            [new CmdSpec("curl", destFlags: ["-o"]),
             new CmdSpec("curl", destFlags: ["--output"])],
            ["./"]);

        CmdSpec curl = policy.OkCmds.Single(static spec => spec.Equals(new CmdSpec("curl")));
        curl.DestFlags.SetEquals(["--output", "-o"]).ShouldBeTrue();
    }

    [Fact]
    public void WithAdd_ConflictingDuplicateCmdSpec_MergesMetadataConservatively()
    {
        var policy = new ShellPolicy([new CmdSpec("curl", destFlags: ["-o"])], ["./"]);
        ShellPolicy merged = policy.WithAdd(addCmds: [new CmdSpec("curl", destFlags: ["--output"])]);

        CmdSpec curl = merged.OkCmds.Single(static spec => spec.Equals(new CmdSpec("curl")));
        curl.DestFlags.SetEquals(["--output", "-o"]).ShouldBeTrue();
    }
}
