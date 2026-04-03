using Shouldly;
using ShellShelter.Core;

namespace ShellShelter.Tests;

public sealed class SolutionStructureTests
{
    [Fact]
    public void ShellShelterCoreAssemblyMarker_TypeIsAccessible()
    {
        typeof(ShellShelterCoreAssemblyMarker).Namespace.ShouldBe("ShellShelter.Core");
    }
}
