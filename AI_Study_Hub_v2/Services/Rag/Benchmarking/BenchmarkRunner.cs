using System.Diagnostics;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public sealed class BenchmarkRunner
{
    private readonly IAiChatService _aiChat;
    private readonly BenchmarkEvaluator _evaluator;
    private readonly IAiChatCompletionClientFactory _clientFactory;
    private readonly ILogger<BenchmarkRunner> _logger;

    public BenchmarkRunner(
        IAiChatService aiChat,
        BenchmarkEvaluator evaluator,
        IAiChatCompletionClientFactory clientFactory,
        ILogger<BenchmarkRunner> logger)
    {
        _aiChat = aiChat;
        _evaluator = evaluator;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public async Task<BenchmarkResult> RunAsync(
        Guid supabaseUserId,
        BenchmarkConfig config,
        IReadOnlyList<BenchmarkQuestion>? questions = null,
        CancellationToken cancellationToken = default)
    {
        var dataset = questions ?? BenchmarkDataset.All;
        if (config.Count is > 0)
        {
            dataset = dataset.Take(config.Count.Value).ToList();
        }

        var responses = new List<BenchmarkResponse>();

        _logger.LogInformation(
            "Starting benchmark: model={Model}, questions={Count}, user={User}",
            config.ModelName, dataset.Count, supabaseUserId);

        for (var i = 0; i < dataset.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var q = dataset[i];
            _logger.LogInformation("Benchmark [{Idx}/{Total}] {Id}: {Category} - {Question}",
                i + 1, dataset.Count, q.Id, q.Category, q.Question[..Math.Min(60, q.Question.Length)]);

            var request = new AiChatAskRequest(
                q.Question,
                DocumentId: null,
                FolderId: null,
                SubjectCode: null,
                Semester: null,
                TopK: 5,
                DocumentIds: config.DocumentIds,
                Model: config.ModelName);

            var stopwatch = Stopwatch.StartNew();
            string? answer = null;
            IReadOnlyList<AiChatSourceDto>? sources = null;
            string? error = null;

            try
            {
                var response = await _aiChat.AskAsync(supabaseUserId, request, cancellationToken);
                answer = response.Answer;
                sources = response.Sources;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _logger.LogWarning(ex, "Benchmark question {Id} failed.", q.Id);
            }

            stopwatch.Stop();

            responses.Add(new BenchmarkResponse(q, answer, sources, stopwatch.ElapsedMilliseconds, error));
        }

        var provider = config.Provider ?? _clientFactory.GetProviderName(config.ModelName);
        var result = _evaluator.Evaluate(config.ModelName, provider, responses);

        _logger.LogInformation(
            "Benchmark complete: model={Model}, overall={Score:P2}, " +
            "citation={Citation:P2}, hallucination={Hallucination:P2}, refusal={Refusal:P2}",
            config.ModelName, result.OverallScore, result.CitationAccuracy,
            result.HallucinationRate, result.RefusalAccuracy);

        return result;
    }
}
