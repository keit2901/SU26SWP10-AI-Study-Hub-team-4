using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class AiChatCompletionClientFactoryTests
{
    [Test]
    public void GetClient_ConfiguredModels_RoutesToTheirConfiguredProviders()
    {
        var (sut, groq, gemini) = CreateSut();

        sut.GetClient("configured-groq").Should().BeSameAs(groq);
        sut.GetClient("CONFIGURED-GEMINI").Should().BeSameAs(gemini);
        sut.GetProviderName("configured-groq").Should().Be("groq");
        sut.GetProviderName("configured-gemini").Should().Be("gemini");
    }

    [Test]
    public void GetClient_BlankModel_RoutesToConfiguredDefaultProvider()
    {
        var (sut, groq, _) = CreateSut(defaultProvider: "groq");

        sut.GetClient("  ").Should().BeSameAs(groq);
        sut.GetProviderName(null).Should().Be("groq");
    }

    [Test]
    public void GetClient_BlankModel_RoutesToGeminiWhenGeminiIsDefault()
    {
        var (sut, _, gemini) = CreateSut(defaultProvider: "gemini");

        sut.GetClient("  ").Should().BeSameAs(gemini);
        sut.GetProviderName(null).Should().Be("gemini");
    }

    [Test]
    public void GetClient_ExplicitProvider_IsUnaffectedByOppositeDefault()
    {
        var (sut, groq, gemini) = CreateSut(defaultProvider: "gemini");

        sut.GetClient("configured-groq").Should().BeSameAs(groq);
        sut.GetClient("configured-gemini").Should().BeSameAs(gemini);
    }

    [Test]
    public void GetClient_UnknownModel_ThrowsUnsupportedModel()
    {
        var (sut, _, _) = CreateSut();

        var action = () => sut.GetClient("not-configured");

        var exception = action.Should().Throw<AiChatModelException>().Which;
        exception.Code.Should().Be("unsupported_model");
    }

    [Test]
    public void GetClient_ConfiguredGeminiWithoutKey_DoesNotRouteToGroq()
    {
        var (sut, groq, gemini) = CreateSut(geminiApiKey: string.Empty);

        sut.GetClient("configured-gemini").Should().BeSameAs(gemini);
        sut.GetClient("configured-gemini").Should().NotBeSameAs(groq);
    }

    [Test]
    public void GetClient_BlankGeminiDefaultWithoutKey_DoesNotRouteToGroq()
    {
        var (sut, groq, gemini) = CreateSut(defaultProvider: "gemini", geminiApiKey: string.Empty);

        sut.GetClient(null).Should().BeSameAs(gemini);
        sut.GetClient(null).Should().NotBeSameAs(groq);
    }

    private static (AiChatCompletionClientFactory Sut, GroqChatCompletionClient Groq, GeminiChatCompletionClient Gemini) CreateSut(
        string defaultProvider = "groq",
        string geminiApiKey = "gemini-key")
    {
        var groq = new GroqChatCompletionClient(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { Model = "configured-groq" }),
            NullLogger<GroqChatCompletionClient>.Instance);
        var gemini = new GeminiChatCompletionClient(
            new HttpClient(),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { Model = "configured-gemini", ApiKey = geminiApiKey }),
            NullLogger<GeminiChatCompletionClient>.Instance);
        var sut = new AiChatCompletionClientFactory(
            groq,
            gemini,
            Microsoft.Extensions.Options.Options.Create(new GroqOptions { Model = "configured-groq" }),
            Microsoft.Extensions.Options.Options.Create(new GeminiOptions { Model = "configured-gemini", ApiKey = geminiApiKey }),
            Microsoft.Extensions.Options.Options.Create(new AiChatOptions { DefaultProvider = defaultProvider }));
        return (sut, groq, gemini);
    }
}
