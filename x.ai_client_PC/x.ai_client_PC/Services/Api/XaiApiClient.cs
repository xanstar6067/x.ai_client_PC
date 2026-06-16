using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using x.ai_client_PC.Models;

namespace x.ai_client_PC.Services.Api;

public class XaiApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private XaiApiOptions _options = new();

    public XaiApiClient(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    public void Configure(XaiApiOptions options)
    {
        _options = new XaiApiOptions
        {
            BaseUrl = options.BaseUrl.TrimEnd('/'),
            ApiKey = options.ApiKey
        };
    }

    public async Task<bool> ValidateApiKeyAsync(CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "models");
        using var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<RemoteModel>> GetModelsAsync(string endpoint, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, endpoint);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var parsed = JsonSerializer.Deserialize<ModelListResponse>(json, JsonOptions);
        return parsed?.Data ?? [];
    }

    public async IAsyncEnumerable<StreamDelta> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        request.Stream = true;
        using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        using var httpRequest = CreateRequest(HttpMethod.Post, "chat/completions", content);
        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        await foreach (var delta in ReadSseStreamAsync(response, ct))
        {
            yield return delta;
        }
    }

    public async IAsyncEnumerable<StreamDelta> StreamResponsesAsync(
        ResponsesRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        request.Stream = true;
        using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        using var httpRequest = CreateRequest(HttpMethod.Post, "responses", content);
        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        await foreach (var delta in ReadSseStreamAsync(response, ct))
        {
            yield return delta;
        }
    }

    public async Task<ImageGenerationResponse> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default)
    {
        using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        using var httpRequest = CreateRequest(HttpMethod.Post, "images/generations", content);
        using var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ImageGenerationResponse>(json, JsonOptions) ?? new ImageGenerationResponse();
    }

    public async Task<ImageGenerationResponse> EditImageAsync(ImageEditRequest request, CancellationToken ct = default)
    {
        using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        using var httpRequest = CreateRequest(HttpMethod.Post, "images/edits", content);
        using var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<ImageGenerationResponse>(json, JsonOptions) ?? new ImageGenerationResponse();
    }

    public async Task<VideoStartResponse> StartVideoGenerationAsync(VideoGenerationRequest request, CancellationToken ct = default)
    {
        using var content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");
        using var httpRequest = CreateRequest(HttpMethod.Post, "videos/generations", content);
        using var response = await _http.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<VideoStartResponse>(json, JsonOptions) ?? new VideoStartResponse();
    }

    public async Task<VideoStatusResponse> GetVideoStatusAsync(string requestId, CancellationToken ct = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"videos/{requestId}");
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<VideoStatusResponse>(json, JsonOptions) ?? new VideoStatusResponse();
    }

    public async Task<byte[]> DownloadBytesAsync(string url, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, BuildUri(relativePath));
        if (content is not null)
        {
            request.Content = content;
        }

        ApplyAuth(request);
        return request;
    }

    private Uri BuildUri(string relativePath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.x.ai/v1"
            : _options.BaseUrl.TrimEnd('/');

        var path = relativePath.TrimStart('/');
        return new Uri($"{baseUrl}/{path}");
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    private static async IAsyncEnumerable<StreamDelta> ReadSseStreamAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"API error {(int)response.StatusCode}: {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data: "))
            {
                continue;
            }

            var payload = line["data: ".Length..].Trim();
            if (payload == "[DONE]")
            {
                yield return new StreamDelta { IsDone = true };
                yield break;
            }

            var delta = ParseStreamPayload(payload);
            if (delta is not null)
            {
                yield return delta;
            }
        }
    }

    private static StreamDelta? ParseStreamPayload(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var delta = new StreamDelta();

            if (root.TryGetProperty("id", out var idProp))
            {
                delta.ResponseId = idProp.GetString();
            }

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind != JsonValueKind.Null)
            {
                delta.Usage = ParseUsage(usage);
            }

            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                if (choice.TryGetProperty("delta", out var d))
                {
                    if (d.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    {
                        delta.ContentDelta = content.GetString();
                    }

                    if (d.TryGetProperty("reasoning_content", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                    {
                        delta.ReasoningDelta = reasoning.GetString();
                    }
                }
            }

            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in output.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var type) && type.GetString() == "message")
                    {
                        if (item.TryGetProperty("content", out var contentArr))
                        {
                            foreach (var part in contentArr.EnumerateArray())
                            {
                                if (part.TryGetProperty("type", out var partType) && partType.GetString() == "output_text"
                                    && part.TryGetProperty("text", out var text))
                                {
                                    delta.ContentDelta = (delta.ContentDelta ?? string.Empty) + text.GetString();
                                }
                            }
                        }
                    }

                    if (item.TryGetProperty("type", out var rType) && rType.GetString() == "reasoning"
                        && item.TryGetProperty("summary", out var summary))
                    {
                        foreach (var part in summary.EnumerateArray())
                        {
                            if (part.TryGetProperty("text", out var text))
                            {
                                delta.ReasoningDelta = (delta.ReasoningDelta ?? string.Empty) + text.GetString();
                            }
                        }
                    }
                }
            }

            if (root.TryGetProperty("delta", out var responseDelta))
            {
                if (responseDelta.TryGetProperty("output_text", out var outputText))
                {
                    delta.ContentDelta = outputText.GetString();
                }
            }

            return delta;
        }
        catch
        {
            return null;
        }
    }

    private static UsageInfo ParseUsage(JsonElement usage)
    {
        var info = new UsageInfo();
        if (usage.TryGetProperty("prompt_tokens", out var pt))
        {
            info.PromptTokens = pt.GetInt32();
        }

        if (usage.TryGetProperty("completion_tokens", out var ct))
        {
            info.CompletionTokens = ct.GetInt32();
        }

        if (usage.TryGetProperty("input_tokens", out var it))
        {
            info.PromptTokens = it.GetInt32();
        }

        if (usage.TryGetProperty("output_tokens", out var ot))
        {
            info.CompletionTokens = ot.GetInt32();
        }

        if (usage.TryGetProperty("completion_tokens_details", out var ctd)
            && ctd.TryGetProperty("reasoning_tokens", out var rt))
        {
            info.ReasoningTokens = rt.GetInt32();
        }

        if (usage.TryGetProperty("output_tokens_details", out var otd)
            && otd.TryGetProperty("reasoning_tokens", out var ort))
        {
            info.ReasoningTokens = ort.GetInt32();
        }

        if (usage.TryGetProperty("cost_in_usd_ticks", out var ticks))
        {
            info.CostUsd = ticks.GetInt64() / 10_000_000_000.0;
        }

        if (usage.TryGetProperty("cost_in_nano_usd", out var nano))
        {
            info.CostUsd = nano.GetInt64() / 1_000_000_000.0;
        }

        return info;
    }

    public static string ToReasoningEffortString(ReasoningEffort effort) => effort switch
    {
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        ReasoningEffort.XHigh => "xhigh",
        _ => "medium"
    };

    public static bool IsGrok4Family(string modelId) =>
        modelId.Contains("grok-4", StringComparison.OrdinalIgnoreCase);

    public static bool IsMultiAgentModel(string modelId) =>
        modelId.Contains("multi-agent", StringComparison.OrdinalIgnoreCase);
}