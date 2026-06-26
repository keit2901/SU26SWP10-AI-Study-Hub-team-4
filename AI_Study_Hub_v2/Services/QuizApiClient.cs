using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Thin wrapper around the Sprint 3 quiz endpoints used by the Blazor AI workspace.
/// </summary>
public sealed class QuizApiClient
{
    private const string GeneratePath = "api/ai/quizzes/generate";

    private readonly HttpClient _http;

    public QuizApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<QuizGenerateResponse> GenerateAsync(
        string accessToken,
        QuizGenerateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);

        return SendAsync<QuizGenerateResponse>(
            accessToken,
            HttpMethod.Post,
            GeneratePath,
            request,
            EnsureGenerateResponse,
            cancellationToken);
    }

    public Task<QuizSubmitResponse> SubmitAsync(
        string accessToken,
        Guid quizId,
        QuizSubmitRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(request);
        if (quizId == Guid.Empty)
        {
            throw new ArgumentException("Quiz id is required.", nameof(quizId));
        }

        return SendAsync<QuizSubmitResponse>(
            accessToken,
            HttpMethod.Post,
            $"api/ai/quizzes/{quizId}/submit",
            request,
            EnsureSubmitResponse,
            cancellationToken);
    }

    private async Task<TResponse> SendAsync<TResponse>(
        string accessToken,
        HttpMethod method,
        string path,
        object body,
        Func<TResponse?, TResponse> ensureResponse,
        CancellationToken cancellationToken)
    {
        using var req = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var resp = await _http.SendAsync(req, cancellationToken);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<TResponse>(cancellationToken: cancellationToken);
            return ensureResponse(dto);
        }

        await ThrowFromResponseAsync(resp, cancellationToken);
        throw new InvalidOperationException("Unreachable");
    }

    private static QuizGenerateResponse EnsureGenerateResponse(QuizGenerateResponse? dto)
    {
        if (dto is null)
        {
            throw new QuizApiException(500, "empty_response", "Server returned empty quiz response.");
        }

        return dto with
        {
            Questions = dto.Questions ?? Array.Empty<QuizQuestionDto>(),
            Sources = dto.Sources ?? Array.Empty<QuizSourceDto>(),
        };
    }

    private static QuizSubmitResponse EnsureSubmitResponse(QuizSubmitResponse? dto)
    {
        if (dto is null)
        {
            throw new QuizApiException(500, "empty_response", "Server returned empty quiz submission response.");
        }

        return dto with { Results = dto.Results ?? Array.Empty<QuizQuestionResultDto>() };
    }

    private static async Task ThrowFromResponseAsync(HttpResponseMessage resp, CancellationToken cancellationToken)
    {
        var status = (int)resp.StatusCode;
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ApiErrorResponse>(cancellationToken: cancellationToken);
            if (err is not null && (!string.IsNullOrEmpty(err.Code) || !string.IsNullOrEmpty(err.Message)))
            {
                var message = !string.IsNullOrWhiteSpace(err.Message)
                    ? err.Message
                    : $"Request failed with status {status}.";
                throw new QuizApiException(status, err.Code ?? "request_failed", message, err.Errors);
            }
        }
        catch (QuizApiException)
        {
            throw;
        }
        catch
        {
            // Fall through to generic text handling.
        }

        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
        throw new QuizApiException(status, "request_failed", string.IsNullOrWhiteSpace(raw) ? $"Request failed ({status})." : raw);
    }
}

public sealed class QuizApiException : Exception
{
    public QuizApiException(int statusCode, string code, string message, IDictionary<string, string[]>? errors = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Errors = errors;
    }

    public int StatusCode { get; }
    public string Code { get; }
    public IDictionary<string, string[]>? Errors { get; }
}
