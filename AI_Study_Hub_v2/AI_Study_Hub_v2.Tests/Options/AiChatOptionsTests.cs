using AI_Study_Hub_v2.Options;

namespace AI_Study_Hub_v2.Tests.Options;

[TestFixture]
public sealed class AiChatOptionsTests
{
    [TestCase("groq")]
    [TestCase("GEMINI")]
    public void IsSupportedProvider_KnownProvider_ReturnsTrue(string provider)
    {
        AiChatOptions.IsSupportedProvider(provider).Should().BeTrue();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("other")]
    public void IsSupportedProvider_UnknownProvider_ReturnsFalse(string? provider)
    {
        AiChatOptions.IsSupportedProvider(provider).Should().BeFalse();
    }

    [Test]
    public void HasValidDefaultProviderConfiguration_GeminiWithoutKey_ReturnsFalse()
    {
        AiChatOptions.HasValidDefaultProviderConfiguration(
            new AiChatOptions { DefaultProvider = "gemini" },
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { ApiKey = string.Empty }))
            .Should().BeFalse();
    }

    [Test]
    public void HasValidDefaultProviderConfiguration_GeminiWithKey_ReturnsTrue()
    {
        AiChatOptions.HasValidDefaultProviderConfiguration(
            new AiChatOptions { DefaultProvider = "gemini" },
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { ApiKey = "configured" }))
            .Should().BeTrue();
    }
}
