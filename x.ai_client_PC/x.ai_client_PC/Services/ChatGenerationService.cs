using x.ai_client_PC.Infrastructure;
using x.ai_client_PC.Models;
using x.ai_client_PC.Services.Api;

namespace x.ai_client_PC.Services;

public class ChatGenerationService
{
    private readonly XaiApiClient _api;
    private readonly DataRepository _repo;
    private readonly LocalizationService _loc;

    public ChatGenerationService(XaiApiClient api, DataRepository repo, LocalizationService loc)
    {
        _api = api;
        _repo = repo;
        _loc = loc;
    }

    public async Task<ChatMessage> SendMessageAsync(
        ChatSession chat,
        string userText,
        IEnumerable<string> attachmentPaths,
        CancellationToken ct)
    {
        var userMessage = new ChatMessage
        {
            ChatId = chat.Id,
            Role = MessageRole.User,
            Content = userText,
            Attachments = attachmentPaths.Select(p => new MessageAttachment
            {
                FileName = Path.GetFileName(p),
                LocalPath = p,
                MimeType = GetMimeType(p)
            }).ToList()
        };
        chat.Messages.Add(userMessage);

        var assistant = new ChatMessage
        {
            ChatId = chat.Id,
            Role = MessageRole.Assistant,
            Content = string.Empty,
            IsStreaming = true,
            ParentMessageId = userMessage.Id
        };
        chat.Messages.Add(assistant);

        await _repo.SaveChatAsync(chat);

        var model = (await _repo.GetModelsAsync()).FirstOrDefault(m => m.Id == chat.ModelId);
        var roles = await _repo.GetRolesAsync();
        var systemRole = roles.FirstOrDefault(r => r.Id == chat.SystemRoleId)
            ?? roles.FirstOrDefault(r => r.IsDefault);

        try
        {
            if (model?.UsesResponsesApi == true)
            {
                await StreamResponsesAsync(chat, assistant, systemRole?.Content, ct);
            }
            else
            {
                await StreamChatCompletionAsync(chat, assistant, systemRole?.Content, model, ct);
            }
        }
        catch (OperationCanceledException)
        {
            assistant.IsStreaming = false;
            assistant.Content = string.IsNullOrEmpty(assistant.Content) ? _loc["AssistantStopped"] : assistant.Content;
            await _repo.SaveChatAsync(chat);
            throw;
        }
        catch (Exception ex)
        {
            assistant.IsStreaming = false;
            assistant.ErrorMessage = ex.Message;
            assistant.Content = _loc.Format("ErrorPrefix", ex.Message);
            await _repo.SaveChatAsync(chat);
            throw;
        }

        assistant.IsStreaming = false;
        chat.TotalCostUsd += assistant.CostUsd;
        chat.TotalTokens += assistant.PromptTokens + assistant.CompletionTokens + assistant.ReasoningTokens;
        await _repo.SaveChatAsync(chat);
        return assistant;
    }

    private async Task StreamChatCompletionAsync(
        ChatSession chat,
        ChatMessage assistant,
        string? systemPrompt,
        ModelInfo? model,
        CancellationToken ct)
    {
        var request = new ChatCompletionRequest
        {
            Model = chat.ModelId,
            PromptCacheKey = chat.PromptCacheKey
        };

        if (!XaiApiClient.IsMultiAgentModel(chat.ModelId))
        {
            request.Temperature = chat.Temperature;
            request.TopP = chat.TopP;
            request.MaxTokens = chat.MaxTokens;
            request.FrequencyPenalty = chat.FrequencyPenalty;
            request.PresencePenalty = chat.PresencePenalty;
        }

        if (model?.SupportsReasoning == true || XaiApiClient.IsGrok4Family(chat.ModelId))
        {
            request.ReasoningEffort = XaiApiClient.ToReasoningEffortString(chat.ReasoningEffort);
        }

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            request.Messages.Add(new ChatMessageDto { Role = "system", Content = systemPrompt });
        }

        foreach (var msg in GetActiveMessages(chat))
        {
            if (msg.Role == MessageRole.Assistant)
            {
                request.Messages.Add(new ChatMessageDto { Role = "assistant", Content = msg.Content });
            }
            else if (msg.Role == MessageRole.User)
            {
                request.Messages.Add(BuildUserMessage(msg, model));
            }
        }

        await foreach (var delta in _api.StreamChatCompletionAsync(request, ct))
        {
            if (delta.ContentDelta is not null)
            {
                assistant.Content += delta.ContentDelta;
            }

            if (delta.ReasoningDelta is not null)
            {
                assistant.ReasoningContent = (assistant.ReasoningContent ?? string.Empty) + delta.ReasoningDelta;
            }

            if (delta.ResponseId is not null)
            {
                assistant.ResponseId = delta.ResponseId;
                chat.LastResponseId = delta.ResponseId;
            }

            if (delta.Usage is not null)
            {
                ApplyUsage(assistant, delta.Usage);
            }

            if (delta.IsDone)
            {
                break;
            }

            await _repo.SaveChatAsync(chat);
        }
    }

    private async Task StreamResponsesAsync(
        ChatSession chat,
        ChatMessage assistant,
        string? systemPrompt,
        CancellationToken ct)
    {
        var input = new List<object>();
        foreach (var msg in GetActiveMessages(chat))
        {
            if (msg.Role == MessageRole.User)
            {
                var parts = new List<object> { new { type = "input_text", text = msg.Content } };
                foreach (var att in msg.Attachments)
                {
                    var dataUri = await ToDataUriAsync(att.LocalPath);
                    parts.Add(new { type = "input_image", image_url = dataUri });
                }

                input.Add(new { role = "user", content = parts });
            }
            else if (msg.Role == MessageRole.Assistant)
            {
                input.Add(new { role = "assistant", content = msg.Content });
            }
        }

        var request = new ResponsesRequest
        {
            Model = chat.ModelId,
            Input = input,
            Instructions = string.IsNullOrWhiteSpace(chat.LastResponseId) ? systemPrompt : null,
            PreviousResponseId = chat.LastResponseId,
            PromptCacheKey = chat.PromptCacheKey,
            Reasoning = new ReasoningDto { Effort = XaiApiClient.ToReasoningEffortString(chat.ReasoningEffort) }
        };

        if (!XaiApiClient.IsMultiAgentModel(chat.ModelId))
        {
            request.Temperature = chat.Temperature;
            request.TopP = chat.TopP;
            request.MaxOutputTokens = chat.MaxTokens;
        }

        await foreach (var delta in _api.StreamResponsesAsync(request, ct))
        {
            if (delta.ContentDelta is not null)
            {
                assistant.Content += delta.ContentDelta;
            }

            if (delta.ReasoningDelta is not null)
            {
                assistant.ReasoningContent = (assistant.ReasoningContent ?? string.Empty) + delta.ReasoningDelta;
            }

            if (delta.ResponseId is not null)
            {
                assistant.ResponseId = delta.ResponseId;
                chat.LastResponseId = delta.ResponseId;
            }

            if (delta.Usage is not null)
            {
                ApplyUsage(assistant, delta.Usage);
            }

            if (delta.IsDone)
            {
                break;
            }

            await _repo.SaveChatAsync(chat);
        }
    }

    public async Task RegenerateAsync(ChatSession chat, ChatMessage assistantMessage, CancellationToken ct)
    {
        assistantMessage.Content = string.Empty;
        assistantMessage.ReasoningContent = null;
        assistantMessage.IsStreaming = true;
        assistantMessage.ErrorMessage = null;

        var model = (await _repo.GetModelsAsync()).FirstOrDefault(m => m.Id == chat.ModelId);
        var roles = await _repo.GetRolesAsync();
        var systemRole = roles.FirstOrDefault(r => r.Id == chat.SystemRoleId)
            ?? roles.FirstOrDefault(r => r.IsDefault);

        var trimmed = chat.Messages
            .Where(m => m.CreatedAt <= assistantMessage.CreatedAt && m.IsActiveVersion)
            .OrderBy(m => m.CreatedAt)
            .ToList();
        chat.Messages = trimmed;

        try
        {
            if (model?.UsesResponsesApi == true)
            {
                await StreamResponsesAsync(chat, assistantMessage, systemRole?.Content, ct);
            }
            else
            {
                await StreamChatCompletionAsync(chat, assistantMessage, systemRole?.Content, model, ct);
            }
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            await _repo.SaveChatAsync(chat);
        }
    }

    private static IEnumerable<ChatMessage> GetActiveMessages(ChatSession chat) =>
        chat.Messages
            .Where(m => m.IsActiveVersion && m.Role != MessageRole.System)
            .OrderBy(m => m.CreatedAt)
            .TakeLast(chat.ContextMessageLimit);

    private static ChatMessageDto BuildUserMessage(ChatMessage msg, ModelInfo? model)
    {
        if (!msg.Attachments.Any() || model?.SupportsImageInput != true)
        {
            return new ChatMessageDto { Role = "user", Content = msg.Content };
        }

        var parts = new List<object>
        {
            new { type = "text", text = msg.Content }
        };

        foreach (var att in msg.Attachments)
        {
            var dataUri = ToDataUri(att.LocalPath);
            parts.Add(new { type = "image_url", image_url = new { url = dataUri } });
        }

        return new ChatMessageDto { Role = "user", Content = parts };
    }

    private static async Task<string> ToDataUriAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var mime = GetMimeType(path);
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string ToDataUri(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var mime = GetMimeType(path);
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    private static void ApplyUsage(ChatMessage assistant, UsageInfo usage)
    {
        assistant.PromptTokens = usage.PromptTokens;
        assistant.CompletionTokens = usage.CompletionTokens;
        assistant.ReasoningTokens = usage.ReasoningTokens;
        assistant.CostUsd = usage.CostUsd;
    }

    public async Task<string> SaveDroppedFileAsync(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var dest = Path.Combine(AppPaths.AttachmentsPath, $"{Guid.NewGuid():N}_{fileName}");
        File.Copy(sourcePath, dest, true);
        return dest;
    }
}
