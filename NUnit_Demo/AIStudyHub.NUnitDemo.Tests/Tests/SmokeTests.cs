namespace AIStudyHub.NUnitDemo.Tests.Tests;

/// <summary>
/// Minimal smoke tests showing the NUnit runner and FluentAssertions work without the main project.
/// </summary>
[TestFixture]
public sealed class SmokeTests
{
    [Test]
    public void TestRunner_Should_Be_Working()
    {
        var result = 1 + 1;

        result.Should().Be(2);
    }

    [Test]
    public void FluentAssertions_Should_Be_Available()
    {
        var projectName = "AIStudyHub.NUnitDemo";

        projectName.Should().StartWith("AIStudyHub").And.EndWith("Demo");
    }
}
