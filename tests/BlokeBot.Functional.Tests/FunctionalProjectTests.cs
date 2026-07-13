using Shouldly;
using TUnit.Core;

namespace BlokeBot.Functional.Tests;

public sealed class FunctionalProjectTests
{
    [Test]
    public void TestProject_Discovery_Succeeds()
    {
        true.ShouldBeTrue();
    }
}
