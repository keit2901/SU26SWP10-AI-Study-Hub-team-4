using AI_Study_Hub_v2.Options;

namespace AI_Study_Hub_v2.Tests.Options;

[TestFixture]
public sealed class OllamaOptionsTests
{
    [Test]
    public void Defaults_UseTheLockedEmbeddingContract()
    {
        var options = new OllamaOptions();

        options.BaseUrl.Should().Be("http://localhost:11434");
        options.Model.Should().Be("all-minilm:l6-v2");
        options.TimeoutSeconds.Should().Be(60);
        options.MaxRetries.Should().Be(3);
    }

    [TestCase("http://ollama:11434")]
    [TestCase("https://ollama.example.com")]
    public void HasValidBaseUrl_AbsoluteHttpOrHttpsUrl_ReturnsTrue(string baseUrl)
    {
        OllamaOptions.HasValidBaseUrl(baseUrl).Should().BeTrue();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("ollama:11434")]
    [TestCase("/api/tags")]
    [TestCase("ftp://ollama.example.com")]
    public void HasValidBaseUrl_NonHttpAbsoluteUrl_ReturnsFalse(string? baseUrl)
    {
        OllamaOptions.HasValidBaseUrl(baseUrl).Should().BeFalse();
    }
}
