using Shouldly;
using ShellShelter.Core;

namespace ShellShelter.Tests;

public sealed class ExceptionTests
{
    [Fact]
    public void DisallowedCmdException_ConstructedWithTokens_PreservesTokensAndBuildsMessage()
    {
        DisallowedCmdException exception = new(["git", "push"]);

        exception.CommandTokens.ShouldBe(["git", "push"]);
        exception.Message.ShouldBe("Disallowed command: git push");
    }

    [Fact]
    public void DisallowedDestException_ConstructedWithDestination_PreservesDestinationAndBuildsMessage()
    {
        DisallowedDestException exception = new("/etc/passwd");

        exception.Destination.ShouldBe("/etc/passwd");
        exception.Message.ShouldBe("Disallowed destination: /etc/passwd");
    }

    [Fact]
    public void ShfmtNotFoundException_DefaultConstructor_UsesExpectedFileName()
    {
        ShfmtNotFoundException exception = new();

        exception.FileName.ShouldBe("shfmt");
        exception.Message.ShouldContain("shfmt");
    }

    [Fact]
    public void PwshNotFoundException_DefaultConstructor_UsesExpectedFileName()
    {
        PwshNotFoundException exception = new();

        exception.FileName.ShouldBe("pwsh");
        exception.Message.ShouldContain("pwsh");
    }
}
