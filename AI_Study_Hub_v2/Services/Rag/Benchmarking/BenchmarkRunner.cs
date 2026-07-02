using System.Diagnostics;
using System.Text.Json;
using AI_Study_Hub_v2.Data;
using AI_Study_Hub_v2.Data.Entities;
using AI_Study_Hub_v2.Dtos;
using AI_Study_Hub_v2.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI_Study_Hub_v2.Services.Rag.Benchmarking;

public sealed class BenchmarkRunner
{
    private readonly IAiChatService _aiChat;
    private readonly BenchmarkEvaluator _evaluator;
    private readonly IAiChatCompletionClientFactory _clientFactory;
    private readonly AppDbContext _db;
    private readonly RagOptions _ragOptions;
    private readonly ILogger<BenchmarkRunner> _logger;

    public BenchmarkRunner(
        IAiChatService aiChat,
        BenchmarkEvaluator evaluator,
        IAiChatCompletionClientFactory clientFactory,
        AppDbContext db,
        IOptions<RagOptions> ragOptions,
        ILogger<BenchmarkRunner> logger)
    {
        _aiChat = aiChat;
        _evaluator = evaluator;
        _clientFactory = clientFactory;
        _db = db;
        _ragOptions = ragOptions.Value;
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

        await SaveHistoryAsync(result, config, cancellationToken);
        return result;
    }

    private async Task SaveHistoryAsync(
        BenchmarkResult result,
        BenchmarkConfig config,
        CancellationToken cancellationToken)
    {
        var previous = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(x => x.ModelName == result.ModelName && x.Provider == result.Provider)
            .OrderByDescending(x => x.RunAt)
            .FirstOrDefaultAsync(cancellationToken);

        var alertTriggered = false;
        if (previous is not null && previous.OverallScore > 0 && _ragOptions.BenchmarkAlertDropPercent > 0)
        {
            var dropPercent = ((previous.OverallScore - result.OverallScore) / previous.OverallScore) * 100d;
            alertTriggered = dropPercent > _ragOptions.BenchmarkAlertDropPercent;

            if (alertTriggered)
            {
                _logger.LogWarning(
                    "Benchmark regression detected: model={Model}, previous={Previous:P2}, current={Current:P2}, drop_percent={DropPercent:F2}",
                    result.ModelName,
                    previous.OverallScore,
                    result.OverallScore,
                    dropPercent);
            }
        }

        var passedQuestions = result.CategoryScores.Sum(x => x.Passed);
        var totalQuestions = result.Responses.Count;
        var record = new BenchmarkRunRecord
        {
            ModelName = result.ModelName,
            Provider = result.Provider,
            RunAt = result.RunAt,
            OverallScore = result.OverallScore,
            CitationAccuracy = result.CitationAccuracy,
            HallucinationRate = result.HallucinationRate,
            RefusalAccuracy = result.RefusalAccuracy,
            TutoringQuality = result.TutoringQuality,
            DiagramAccuracy = result.DiagramAccuracy,
            P50LatencyMs = result.P50LatencyMs,
            P95LatencyMs = result.P95LatencyMs,
            TotalQuestions = totalQuestions,
            PassedQuestions = passedQuestions,
            FailedQuestions = Math.Max(0, totalQuestions - passedQuestions),
            IsAutomated = config.IsAutomated,
            AlertTriggered = alertTriggered,
            PayloadJson = JsonSerializer.Serialize(result)
        };

        _db.BenchmarkRuns.Add(record);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
