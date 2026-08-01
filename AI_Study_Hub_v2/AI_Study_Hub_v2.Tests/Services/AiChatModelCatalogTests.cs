using AI_Study_Hub_v2.Options;
using AI_Study_Hub_v2.Services;

namespace AI_Study_Hub_v2.Tests.Services;

[TestFixture]
public sealed class AiChatModelCatalogTests
{
    [Test]
    public void GetAvailableModels_GeminiDefaultWithoutKey_KeepsGeminiFirst()
    {
        var models = AiChatModelCatalog.GetAvailableModels(
            new AiChatOptions { DefaultProvider = "gemini" },
            new GroqOptions { Model = "configured-groq" },
            new GeminiOptions { Model = "configured-gemini", ApiKey = string.Empty });

        models.Should().Equal("configured-gemini", "configured-groq");
    }

    [Test]
    public void GetAvailableModels_GroqDefaultWithoutGeminiKey_OmitsGemini()
    {
        var models = AiChatModelCatalog.GetAvailableModels(
            new AiChatOptions { DefaultProvider = "groq" },
            new GroqOptions { Model = "configured-groq" },
            new GeminiOptions { Model = "configured-gemini", ApiKey = string.Empty });

        models.Should().Equal("configured-groq");
    }
}
