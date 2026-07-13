namespace AI_Study_Hub_v2.Tests;

/// <summary>
/// Sanity tests xác nhận pipeline test chạy được:
/// - NUnit discover + run
/// - FluentAssertions hoạt động
/// - Reference tới main project compile
/// Không test logic Auth hiện tại vì sẽ bị rip khi migrate sang Supabase Auth.
/// Sau migration sẽ thay test này bằng test cho SupabaseAuthService.
/// </summary>
[TestFixture]
public class SmokeTests
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
        var name = "AI_Study_Hub_v2";
        name.Should().StartWith("AI_").And.EndWith("_v2");
    }

    [Test]
    public void MainProject_Reference_Should_Compile()
    {
        // Reference 1 type bất kỳ trong main project để chứng minh ProjectReference hoạt động
        var type = typeof(AI_Study_Hub_v2.Data.AppDbContext);
        type.Should().NotBeNull();
        type.Assembly.GetName().Name.Should().Be("AI_Study_Hub_v2");
    }
}


