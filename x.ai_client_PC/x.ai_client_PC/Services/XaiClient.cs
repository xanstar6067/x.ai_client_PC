using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using x.ai_client_PC.Models;

namespace x.ai_client_PC.Services;

public sealed class XaiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromHours(1)
    };

    public static ModelCatalog CreateDefaultCatalog()
    {
        return new ModelCatalog
        {
            LanguageModels =
            [
                new ModelInfo
                {
                    Id = "grok-4.3",
                    Kind = WorkspaceKind.Text,
                    OwnedBy = "xai",
                    InputModalities = ["text", "image"],
                    OutputModalities = ["text"],
                    PromptPriceUsdPerMillion = 1.25m,
                    CompletionPriceUsdPerMillion = 2.50m
                },
                new ModelInfo
                {
                    Id = "grok-4.20-multi-agent",
                    Kind = WorkspaceKind.Text,
                    OwnedBy = "xai",
                    InputModalities = ["text"],
                    OutputModalities = ["text"]
                }
            ],
            ImageModels =
            [
                new ModelInfo
                {
                    Id = "grok-imagine-image-quality",
                    Kind = WorkspaceKind.Images,
                    OwnedBy = "xai",
                    InputModalities = ["text", "image"],
                    OutputModalities = ["image"],
                    ImagePriceUsd = 0.02m
                },
                new ModelInfo
                {
                    Id = "grok-imagine-image",
                    Kind = WorkspaceKind.Images,
                    OwnedBy = "xai",
                    InputModalities = ["text", "image"],
                    OutputModalities = ["image"]
                }
            ],
            VideoModels =
            [
                new ModelInfo
                {
                    Id = "grok-imagine-video",
                    Kind = WorkspaceKind.Videos,
                    OwnedBy = "xai",
                    InputModalities = ["text", "image"],
                    OutputModalities = ["video"]
                },
                new ModelInfo
                {
                    Id = "grok-imagine-video-1.5",
                    Kind = WorkspaceKind.Videos,
                    OwnedBy = "xai",
                    InputModalities = ["text", "image"],
                    OutputModalities = ["video"]
                }
            ]
        };
    }

    public static TextModelParameterProfile GetTextModelParameterProfile(string model)
    {
        var modelId = model ?? string.Empty;
        var normalized = modelId.Trim().ToLowerInvariant();
        var isMultiAgent = normalized.Contains("multi-agent", StringComparison.OrdinalIgnoreCase);
        var isNonReasoning = normalized.Contains("non-reasoning", StringComparison.OrdinalIgnoreCase)
                             || normalized.Contains("non_reasoning", StringComparison.OrdinalIgnoreCase);
        var isGrok43Reasoning = normalized.StartsWith("grok-4.3", StringComparison.OrdinalIgnoreCase);
        var isExplicitReasoning = normalized.Contains("reasoning", StringComparison.OrdinalIgnoreCase) && !isNonReasoning;
        var isReasoning = isGrok43Reasoning || isExplicitReasoning || isMultiAgent;

        if (isMultiAgent)
        {
            return new TextModelParameterProfile
            {
                Model = modelId,
                IsReasoning = true,
                IsMultiAgent = true,
                SupportsReasoningEffort = true,
                SupportsSampling = false,
                SupportsMaxOutputTokens = false,
                SupportsPenalties = false,
                ReasoningEfforts = ["low", "medium", "high", "xhigh"],
                Summary = "Multi-Agent: reasoning.effort управляет числом агентов; sampling, max tokens и penalties не отправляются."
            };
        }

        if (isReasoning && !isNonReasoning)
        {
            return new TextModelParameterProfile
            {
                Model = modelId,
                IsReasoning = true,
                SupportsReasoningEffort = true,
                SupportsSampling = true,
                SupportsMaxOutputTokens = true,
                SupportsPenalties = false,
                ReasoningEfforts = ["none", "low", "medium", "high"],
                Summary = "Reasoning: отправляется reasoning.effort; presence/frequency penalties не отправляются."
            };
        }

        return new TextModelParameterProfile
        {
            Model = modelId,
            IsNonReasoning = isNonReasoning,
            SupportsReasoningEffort = false,
            SupportsSampling = true,
            SupportsMaxOutputTokens = true,
            SupportsPenalties = true,
            ReasoningEfforts = [],
            Summary = isNonReasoning
                ? "Non-reasoning: reasoning.effort полностью убран из запроса; sampling и penalties доступны."
                : "Обычная текстовая модель: reasoning.effort не отправляется; sampling и penalties доступны."
        };
    }

    public async Task<ModelCatalog> LoadModelsAsync(AppSettings settings, string? apiKey, CancellationToken cancellationToken)
    {
        var catalog = CreateDefaultCatalog();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            catalog.Status = "Введите и сохраните API key, чтобы загрузить доступные модели.";
            return catalog;
        }

        var errors = new List<string>();
        var loadedAnySpecializedEndpoint = false;

        try
        {
            catalog.LanguageModels = await GetModelsAsync(settings.BaseUrl, apiKey, "language-models", WorkspaceKind.Text, cancellationToken);
            loadedAnySpecializedEndpoint = true;
        }
        catch (Exception exception)
        {
            errors.Add("language-models: " + exception.Message);
        }

        try
        {
            catalog.ImageModels = await GetModelsAsync(settings.BaseUrl, apiKey, "image-generation-models", WorkspaceKind.Images, cancellationToken);
            loadedAnySpecializedEndpoint = true;
        }
        catch (Exception exception)
        {
            errors.Add("image-generation-models: " + exception.Message);
        }

        try
        {
            catalog.VideoModels = await GetModelsAsync(settings.BaseUrl, apiKey, "video-generation-models", WorkspaceKind.Videos, cancellationToken);
            loadedAnySpecializedEndpoint = true;
        }
        catch (Exception exception)
        {
            errors.Add("video-generation-models: " + exception.Message);
        }

        if (!loadedAnySpecializedEndpoint)
        {
            try
            {
                var allModels = await GetModelsAsync(settings.BaseUrl, apiKey, "models", "all", cancellationToken);
                catalog.LanguageModels = allModels.Where(model => model.Kind == WorkspaceKind.Text).ToList();
                catalog.ImageModels = allModels.Where(model => model.Kind == WorkspaceKind.Images).ToList();
                catalog.VideoModels = allModels.Where(model => model.Kind == WorkspaceKind.Videos).ToList();
                errors.Clear();
                catalog.Status = "Модели загружены через fallback /models.";
            }
            catch (Exception exception)
            {
                errors.Add("models fallback: " + exception.Message);
            }
        }

        MergeMissingDefaults(catalog);
        if (errors.Count == 0)
        {
            catalog.Status = "Модели загружены из xAI API.";
        }
        else if (loadedAnySpecializedEndpoint)
        {
            catalog.Status = "Часть моделей загружена, часть endpoint'ов недоступна: " + string.Join("; ", errors);
        }
        else
        {
            catalog.Status = "Не удалось загрузить модели, оставлены значения по умолчанию: " + string.Join("; ", errors);
        }

        return catalog;
    }

    public async Task<TextGenerationResult> SendTextStreamingAsync(
        AppSettings settings,
        ChatSession chat,
        ChatMessage userMessage,
        string apiKey,
        Action<string> onTextDelta,
        Action<string> onReasoningDelta,
        CancellationToken cancellationToken)
    {
        var payload = BuildResponsesPayload(settings, chat, userMessage);
        using var request = CreateJsonRequest(HttpMethod.Post, settings.BaseUrl, "responses", apiKey, payload);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = new TextGenerationResult();
        var sawStreamEvent = false;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line[5..].Trim();
            if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
            {
                continue;
            }

            sawStreamEvent = true;
            ApplyResponseStreamEvent(data, result, onTextDelta, onReasoningDelta);
        }

        if (!sawStreamEvent)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(content);
            ApplyCompletedResponse(document.RootElement, result);
        }

        return result;
    }

    public async Task<List<ImageGenerationResult>> GenerateImageAsync(
        AppSettings settings,
        string prompt,
        string? source,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.ImageModel,
            ["prompt"] = prompt,
            ["response_format"] = "url",
            ["n"] = settings.ImageCount,
            ["aspect_ratio"] = settings.ImageAspectRatio,
            ["resolution"] = settings.ImageResolution
        };

        var endpoint = "images/generations";
        if (!string.IsNullOrWhiteSpace(source))
        {
            endpoint = "images/edits";
            payload["image"] = new Dictionary<string, string?>
            {
                ["url"] = ToImageReference(source),
                ["type"] = "image_url"
            };
        }

        using var request = CreateJsonRequest(HttpMethod.Post, settings.BaseUrl, endpoint, apiKey, payload);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        return ParseImageResults(document.RootElement);
    }

    public async Task<VideoGenerationRequestResult> StartVideoAsync(
        AppSettings settings,
        string prompt,
        string? source,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.VideoModel,
            ["prompt"] = prompt,
            ["duration"] = settings.VideoDurationSeconds,
            ["aspect_ratio"] = settings.VideoAspectRatio,
            ["resolution"] = settings.VideoResolution
        };

        if (!string.IsNullOrWhiteSpace(source))
        {
            payload["image"] = new Dictionary<string, string?>
            {
                ["url"] = ToImageReference(source)
            };
        }

        using var request = CreateJsonRequest(HttpMethod.Post, settings.BaseUrl, "videos/generations", apiKey, payload);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        var requestId = GetString(document.RootElement, "request_id") ?? GetString(document.RootElement, "id");
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new InvalidDataException("xAI не вернул request_id для видео.");
        }

        return new VideoGenerationRequestResult { RequestId = requestId };
    }

    public async Task<VideoStatusResult> GetVideoStatusAsync(
        AppSettings settings,
        string requestId,
        string apiKey,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Get, settings.BaseUrl, $"videos/{Uri.EscapeDataString(requestId)}", apiKey, null);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        var status = GetString(root, "status") ?? "pending";
        var video = root.TryGetProperty("video", out var videoElement) ? videoElement : default;

        return new VideoStatusResult
        {
            Status = status,
            Url = video.ValueKind == JsonValueKind.Object ? GetString(video, "url") : null,
            DurationSeconds = video.ValueKind == JsonValueKind.Object ? (int?)GetLong(video, "duration") : null,
            Model = GetString(root, "model"),
            Error = root.TryGetProperty("error", out var errorElement) ? errorElement.ToString() : null
        };
    }

    public async Task DownloadAsync(string url, string targetPath, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, cancellationToken);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private static Dictionary<string, object?> BuildResponsesPayload(AppSettings settings, ChatSession chat, ChatMessage userMessage)
    {
        var profile = GetTextModelParameterProfile(settings.TextModel);
        var usePreviousResponseId = settings.StoreServerResponses
                                    && settings.UsePreviousResponseId
                                    && !string.IsNullOrWhiteSpace(chat.LastResponseId);

        List<Dictionary<string, object?>> input = usePreviousResponseId
            ? [ToApiMessage(userMessage)]
            : BuildContextMessages(settings, chat);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = settings.TextModel,
            ["input"] = input,
            ["store"] = settings.StoreServerResponses,
            ["stream"] = true
        };

        if (profile.SupportsReasoningEffort)
        {
            var effort = NormalizeReasoningEffort(settings.ReasoningEffort, profile);
            payload["reasoning"] = new Dictionary<string, object?> { ["effort"] = effort };
        }

        if (usePreviousResponseId)
        {
            payload["previous_response_id"] = chat.LastResponseId;
        }

        if (profile.SupportsSampling)
        {
            payload["temperature"] = settings.Temperature;
            payload["top_p"] = settings.TopP;
        }

        if (profile.SupportsMaxOutputTokens)
        {
            if (settings.MaxOutputTokens > 0)
            {
                payload["max_output_tokens"] = settings.MaxOutputTokens;
            }
        }

        if (profile.SupportsPenalties)
        {
            payload["frequency_penalty"] = settings.FrequencyPenalty;
            payload["presence_penalty"] = settings.PresencePenalty;
        }

        var tools = new List<Dictionary<string, string>>();
        if (settings.WebSearchEnabled)
        {
            tools.Add(new Dictionary<string, string> { ["type"] = "web_search" });
        }

        if (settings.XSearchEnabled)
        {
            tools.Add(new Dictionary<string, string> { ["type"] = "x_search" });
        }

        if (tools.Count > 0)
        {
            payload["tools"] = tools;
        }

        return payload;
    }

    private static List<Dictionary<string, object?>> BuildContextMessages(AppSettings settings, ChatSession chat)
    {
        var messages = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
        {
            messages.Add(new Dictionary<string, object?>
            {
                ["role"] = "system",
                ["content"] = settings.SystemPrompt.Trim()
            });
        }

        var contextMessages = chat.Messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Content) || message.Attachments.Count > 0)
            .TakeLast(Math.Max(1, settings.ContextMessageLimit));

        foreach (var message in contextMessages)
        {
            messages.Add(ToApiMessage(message));
        }

        return messages;
    }

    private static Dictionary<string, object?> ToApiMessage(ChatMessage message)
    {
        if (message.Role == "user" && message.Attachments.Count > 0)
        {
            var content = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["type"] = "input_text",
                    ["text"] = message.Content
                }
            };

            foreach (var attachment in message.Attachments)
            {
                content.Add(new Dictionary<string, object?>
                {
                    ["type"] = "input_image",
                    ["image_url"] = ToImageReference(attachment)
                });
            }

            return new Dictionary<string, object?>
            {
                ["role"] = message.Role,
                ["content"] = content
            };
        }

        return new Dictionary<string, object?>
        {
            ["role"] = message.Role,
            ["content"] = message.Content
        };
    }

    private static void ApplyResponseStreamEvent(
        string data,
        TextGenerationResult result,
        Action<string> onTextDelta,
        Action<string> onReasoningDelta)
    {
        using var document = JsonDocument.Parse(data);
        var root = document.RootElement;
        var eventType = GetString(root, "type") ?? string.Empty;

        if (eventType.Contains("output_text.delta", StringComparison.OrdinalIgnoreCase)
            || eventType.Contains("message.delta", StringComparison.OrdinalIgnoreCase))
        {
            var delta = GetString(root, "delta") ?? GetString(root, "text");
            if (!string.IsNullOrEmpty(delta))
            {
                result.Text += delta;
                onTextDelta(delta);
            }

            return;
        }

        if (eventType.Contains("reasoning", StringComparison.OrdinalIgnoreCase)
            && eventType.Contains("delta", StringComparison.OrdinalIgnoreCase))
        {
            var delta = GetString(root, "delta") ?? GetString(root, "text");
            if (!string.IsNullOrEmpty(delta))
            {
                result.ReasoningSummary += delta;
                onReasoningDelta(delta);
            }

            return;
        }

        if (eventType.Contains("completed", StringComparison.OrdinalIgnoreCase))
        {
            if (root.TryGetProperty("response", out var responseElement))
            {
                ApplyCompletedResponse(responseElement, result);
            }
            else
            {
                ApplyCompletedResponse(root, result);
            }
        }
    }

    private static void ApplyCompletedResponse(JsonElement responseElement, TextGenerationResult result)
    {
        result.ResponseId ??= GetString(responseElement, "id");
        result.Usage = ParseUsage(responseElement);

        if (string.IsNullOrWhiteSpace(result.Text))
        {
            result.Text = ExtractResponseText(responseElement);
        }
    }

    private static string ExtractResponseText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                var type = GetString(contentItem, "type");
                if (!string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = GetString(contentItem, "text");
                if (!string.IsNullOrEmpty(text))
                {
                    builder.Append(text);
                }
            }
        }

        return builder.ToString();
    }

    private static List<ImageGenerationResult> ParseImageResults(JsonElement root)
    {
        var usage = ParseUsage(root);
        var results = new List<ImageGenerationResult>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        var items = data.EnumerateArray().ToList();
        var costPerItem = items.Count > 0 ? usage.CostUsd / items.Count : usage.CostUsd;
        foreach (var item in items)
        {
            results.Add(new ImageGenerationResult
            {
                Url = GetString(item, "url"),
                Base64 = GetString(item, "b64_json"),
                MimeType = GetString(item, "mime_type"),
                RevisedPrompt = GetString(item, "revised_prompt"),
                Usage = new XaiUsage
                {
                    CostUsd = costPerItem,
                    PromptTokens = usage.PromptTokens,
                    CompletionTokens = usage.CompletionTokens,
                    ReasoningTokens = usage.ReasoningTokens,
                    TotalTokens = usage.TotalTokens
                }
            });
        }

        return results;
    }

    private async Task<List<ModelInfo>> GetModelsAsync(
        string baseUrl,
        string apiKey,
        string endpoint,
        string kind,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(HttpMethod.Get, baseUrl, endpoint, apiKey, null);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        var listProperty = root.TryGetProperty("models", out var models) ? models :
            root.TryGetProperty("data", out var data) ? data : default;

        if (listProperty.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return listProperty
            .EnumerateArray()
            .Select(element => ParseModel(element, kind))
            .OrderBy(model => model.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ModelInfo ParseModel(JsonElement element, string kind)
    {
        var id = GetString(element, "id") ?? string.Empty;
        var inputModalities = GetStringArray(element, "input_modalities");
        var outputModalities = GetStringArray(element, "output_modalities");
        var detectedKind = kind == "all" ? DetectKind(id, inputModalities, outputModalities) : kind;

        return new ModelInfo
        {
            Id = id,
            Kind = detectedKind,
            OwnedBy = GetString(element, "owned_by") ?? string.Empty,
            Version = GetString(element, "version") ?? string.Empty,
            InputModalities = inputModalities,
            OutputModalities = outputModalities,
            Aliases = GetStringArray(element, "aliases"),
            PromptPriceUsdPerMillion = PriceCentsPer100MillionToUsdPerMillion(GetLong(element, "prompt_text_token_price")),
            CompletionPriceUsdPerMillion = PriceCentsPer100MillionToUsdPerMillion(GetLong(element, "completion_text_token_price")),
            ImagePriceUsd = ModelImagePriceToUsd(GetLong(element, "image_price"))
        };
    }

    private static string DetectKind(string id, IReadOnlyList<string> inputModalities, IReadOnlyList<string> outputModalities)
    {
        if (outputModalities.Any(modality => string.Equals(modality, "video", StringComparison.OrdinalIgnoreCase))
            || id.Contains("video", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceKind.Videos;
        }

        if (outputModalities.Any(modality => string.Equals(modality, "image", StringComparison.OrdinalIgnoreCase))
            || id.Contains("image", StringComparison.OrdinalIgnoreCase)
            || id.Contains("imagine", StringComparison.OrdinalIgnoreCase))
        {
            return WorkspaceKind.Images;
        }

        return WorkspaceKind.Text;
    }

    private static void MergeMissingDefaults(ModelCatalog catalog)
    {
        var defaults = CreateDefaultCatalog();
        AddMissing(catalog.LanguageModels, defaults.LanguageModels);
        AddMissing(catalog.ImageModels, defaults.ImageModels);
        AddMissing(catalog.VideoModels, defaults.VideoModels);
    }

    private static void AddMissing(List<ModelInfo> target, List<ModelInfo> defaults)
    {
        var seen = target.Select(model => model.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var model in defaults)
        {
            if (seen.Add(model.Id))
            {
                target.Add(model);
            }
        }
    }

    private static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string baseUrl,
        string endpoint,
        string apiKey,
        object? payload)
    {
        var request = new HttpRequestMessage(method, BuildUri(baseUrl, endpoint));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        if (payload is not null)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static Uri BuildUri(string baseUrl, string endpoint)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseUrl)
            ? "https://api.x.ai/v1"
            : baseUrl.Trim().TrimEnd('/');
        return new Uri($"{normalizedBase}/{endpoint.TrimStart('/')}");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"xAI API вернул {(int)response.StatusCode} {response.ReasonPhrase}: {TrimErrorBody(body)}");
    }

    private static string TrimErrorBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(пустой ответ)";
        }

        var sanitized = body.Replace("\r", " ").Replace("\n", " ");
        var dataUriIndex = sanitized.IndexOf("data:image", StringComparison.OrdinalIgnoreCase);
        if (dataUriIndex >= 0)
        {
            sanitized = sanitized[..dataUriIndex] + "data:image/...<hidden>";
        }

        return sanitized.Length <= 1600 ? sanitized : sanitized[..1600] + "...";
    }

    private static string ToImageReference(string source)
    {
        var trimmed = source.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("file-", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (!File.Exists(trimmed))
        {
            throw new FileNotFoundException("Файл изображения не найден.", trimmed);
        }

        var extension = Path.GetExtension(trimmed).ToLowerInvariant();
        var mimeType = extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };

        var bytes = File.ReadAllBytes(trimmed);
        return $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string NormalizeReasoningEffort(string effort, TextModelParameterProfile profile)
    {
        var normalized = string.IsNullOrWhiteSpace(effort) ? "low" : effort.Trim().ToLowerInvariant();
        var allowed = profile.ReasoningEfforts.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allowed.Contains(normalized))
        {
            return normalized;
        }

        return allowed.Contains("low") ? "low" : allowed.FirstOrDefault() ?? "low";
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static long GetLong(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value))
        {
            return value;
        }

        if (property.ValueKind == JsonValueKind.String
            && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value;
        }

        return 0;
    }

    private static XaiUsage ParseUsage(JsonElement root)
    {
        var usage = root.TryGetProperty("usage", out var usageElement) ? usageElement : root;
        var result = new XaiUsage
        {
            PromptTokens = FirstLong(usage, "input_tokens", "prompt_tokens"),
            CompletionTokens = FirstLong(usage, "output_tokens", "completion_tokens"),
            TotalTokens = GetLong(usage, "total_tokens"),
            CostUsd = CostTicksToUsd(FirstLong(usage, "cost_in_usd_ticks", "cost_usd_ticks"))
        };

        if (usage.TryGetProperty("output_tokens_details", out var outputDetails))
        {
            result.ReasoningTokens = GetLong(outputDetails, "reasoning_tokens");
        }

        if (result.ReasoningTokens == 0)
        {
            result.ReasoningTokens = GetLong(usage, "reasoning_tokens");
        }

        return result;
    }

    private static long FirstLong(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = GetLong(element, propertyName);
            if (value != 0)
            {
                return value;
            }
        }

        return 0;
    }

    private static decimal? PriceCentsPer100MillionToUsdPerMillion(long value)
    {
        return value <= 0 ? null : value / 10_000m;
    }

    private static decimal? ModelImagePriceToUsd(long value)
    {
        if (value <= 0)
        {
            return null;
        }

        return value > 100_000 ? CostTicksToUsd(value) : value / 100m;
    }

    private static decimal CostTicksToUsd(long ticks)
    {
        return ticks <= 0 ? 0 : ticks / 10_000_000_000m;
    }
}
