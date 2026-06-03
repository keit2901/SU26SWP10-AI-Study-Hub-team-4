using System.Net.Http.Headers;
using System.Net.Http.Json;
using AI_Study_Hub_v2.Dtos;

namespace AI_Study_Hub_v2.Services;

/// <summary>
/// Thin wrapper around the Sprint 2 AI chat endpoint used by the Blazor chat page.
/// </summary>
public sealed class AiChatApiClient
{
    private const string AskPath = "api/ai/chat/ask";

    private readonly HttpClient _http;

    public AiChatApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<AiChatAnswerResponse> AskAsync(
        AiChatAskRequest request,
        CancellationToken cancellationToken = default)
        => AskCoreAsync(accessToken: null, request, cancellationToken);

    public Task<AiChatAnswerResponse> AskAsync(
        string accessToken,
        AiChatAskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return AskCoreAsync(accessToken, request, cancellationToken);
    }

    private async Task<AiChatAnswerResponse> AskCoreAsync(
        string? accessToken,
        AiChatAskRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Question);

        using var req = new HttpRequestMessage(HttpMethod.Post, AskPath)
        {
            Content = JsonContent.Create(request),
        };

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        using var resp = await _http.SendAsync(req, cancellationToken);
        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<AiChatAnswerResponse>(cancellationToken: cancellationToken);
            if (dto is null)
            {
                throw new AiChatApiException(500, "empty_response", "Server returned empty response.");
            }

            return dto.Sources is null
                ? dto with { Sources = Array.Empty<AiChatSourceDto>() }
                : dto;
        }

        await ThrowFromResponseAsync(resp, cancellationToken);
        throw new InvalidOperationException("Unreachable");
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
                throw new AiChatApiException(status, err.Code ?? "request_failed", message, err.Errors);
            }
        }
        catch (AiChatApiException)
        {
            throw;
        }
        catch
        {
            // Fall through to generic text handling.
        }

        var raw = await resp.Content.ReadAsStringAsync(cancellationToken);
        throw new AiChatApiException(status, "request_failed", string.IsNullOrWhiteSpace(raw) ? $"Request failed ({status})." : raw);
    }
}

public sealed class AiChatApiException : Exception
{
    public AiChatApiException(int statusCode, string code, string message, IDictionary<string, string[]>? errors = null)
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
