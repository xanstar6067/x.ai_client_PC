namespace x.ai_client_PC.Models;

public static class WorkspaceKind
{
    public const string Text = "text";
    public const string Images = "images";
    public const string Videos = "videos";
}

public sealed class PersistedState
{
    public AppSettings Settings { get; set; } = new();
    public List<ChatSession> TextChats { get; set; } = [];
    public List<MediaConversation> ImageChats { get; set; } = [];
    public List<MediaConversation> VideoChats { get; set; } = [];
}

public sealed class AppSettings
{
    public string BaseUrl { get; set; } = "https://api.x.ai/v1";
    public string TextModel { get; set; } = "grok-4.3";
    public string ImageModel { get; set; } = "grok-imagine-image-quality";
    public string VideoModel { get; set; } = "grok-imagine-video";
    public string SystemPrompt { get; set; } = "You are Grok, a helpful AI assistant.";
    public string ReasoningEffort { get; set; } = "low";
    public double Temperature { get; set; } = 0.7;
    public double TopP { get; set; } = 1.0;
    public double FrequencyPenalty { get; set; } = 0;
    public double PresencePenalty { get; set; } = 0;
    public int MaxOutputTokens { get; set; } = 0;
    public int ContextMessageLimit { get; set; } = 20;
    public bool WebSearchEnabled { get; set; }
    public bool XSearchEnabled { get; set; }
    public bool UsePreviousResponseId { get; set; } = true;
    public bool StoreServerResponses { get; set; } = true;
    public string ImageAspectRatio { get; set; } = "auto";
    public string ImageResolution { get; set; } = "1k";
    public int ImageCount { get; set; } = 1;
    public string VideoAspectRatio { get; set; } = "16:9";
    public string VideoResolution { get; set; } = "480p";
    public int VideoDurationSeconds { get; set; } = 5;
}

public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = WorkspaceKind.Text;
    public string Title { get; set; } = "Новый чат";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? LastResponseId { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public long CachedTokens { get; set; }
    public decimal TotalCostUsd { get; set; }
    public List<ChatMessage> Messages { get; set; } = [];

    public ChatSession Duplicate()
    {
        return new ChatSession
        {
            Kind = Kind,
            Title = Title + " копия",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastResponseId = LastResponseId,
            PromptTokens = PromptTokens,
            CompletionTokens = CompletionTokens,
            ReasoningTokens = ReasoningTokens,
            CachedTokens = CachedTokens,
            TotalCostUsd = TotalCostUsd,
            Messages = Messages.Select(message => message.Duplicate()).ToList()
        };
    }

    public override string ToString() => Title;
}

public sealed class ChatMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<string> Attachments { get; set; } = [];
    public bool IsError { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public long CachedTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal CostUsd { get; set; }

    public ChatMessage Duplicate()
    {
        return new ChatMessage
        {
            Role = Role,
            Content = Content,
            CreatedAtUtc = CreatedAtUtc,
            Attachments = [.. Attachments],
            IsError = IsError,
            PromptTokens = PromptTokens,
            CompletionTokens = CompletionTokens,
            ReasoningTokens = ReasoningTokens,
            CachedTokens = CachedTokens,
            TotalTokens = TotalTokens,
            CostUsd = CostUsd
        };
    }
}

public sealed class MediaConversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = WorkspaceKind.Images;
    public string Title { get; set; } = "Новая генерация";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<MediaItem> Items { get; set; } = [];

    public MediaConversation Duplicate()
    {
        return new MediaConversation
        {
            Kind = Kind,
            Title = Title + " копия",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Items = Items.Select(item => item.Duplicate()).ToList()
        };
    }

    public override string ToString() => Title;
}

public sealed class MediaItem
{
    public string Prompt { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? Url { get; set; }
    public string? LocalPath { get; set; }
    public string? RequestId { get; set; }
    public string Status { get; set; } = "done";
    public string? Model { get; set; }
    public string? RevisedPrompt { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal CostUsd { get; set; }

    public string DisplayStatus => Status switch
    {
        "pending" => "ожидает",
        "progress" => "в работе",
        "processing" => "в работе",
        "done" => "готово",
        "failed" => "ошибка",
        "expired" => "истекло",
        _ => Status
    };

    public MediaItem Duplicate()
    {
        return new MediaItem
        {
            Prompt = Prompt,
            Source = Source,
            Url = Url,
            LocalPath = LocalPath,
            RequestId = RequestId,
            Status = Status,
            Model = Model,
            RevisedPrompt = RevisedPrompt,
            Error = Error,
            CreatedAtUtc = CreatedAtUtc,
            CostUsd = CostUsd
        };
    }

    public override string ToString()
    {
        var label = string.IsNullOrWhiteSpace(Prompt) ? "(без промпта)" : Prompt;
        return $"{CreatedAtUtc.ToLocalTime():HH:mm} · {DisplayStatus} · {label}";
    }
}

public sealed class ModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = WorkspaceKind.Text;
    public string OwnedBy { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public IReadOnlyList<string> InputModalities { get; set; } = [];
    public IReadOnlyList<string> OutputModalities { get; set; } = [];
    public IReadOnlyList<string> Aliases { get; set; } = [];
    public decimal? PromptPriceUsdPerMillion { get; set; }
    public decimal? CompletionPriceUsdPerMillion { get; set; }
    public decimal? ImagePriceUsd { get; set; }
    public bool Enabled { get; set; } = true;

    public string Summary
    {
        get
        {
            var modalities = InputModalities.Count == 0 && OutputModalities.Count == 0
                ? Kind
                : string.Join(" → ", string.Join("+", InputModalities), string.Join("+", OutputModalities)).Trim(' ', '→');
            return $"{Id}  {modalities}";
        }
    }

    public override string ToString() => Id;
}

public sealed class ModelCatalog
{
    public List<ModelInfo> LanguageModels { get; set; } = [];
    public List<ModelInfo> ImageModels { get; set; } = [];
    public List<ModelInfo> VideoModels { get; set; } = [];
    public string Status { get; set; } = "Локальные значения по умолчанию";
}

public sealed class TextModelParameterProfile
{
    public string Model { get; set; } = string.Empty;
    public bool IsReasoning { get; set; }
    public bool IsMultiAgent { get; set; }
    public bool IsNonReasoning { get; set; }
    public bool SupportsReasoningEffort { get; set; }
    public bool SupportsSampling { get; set; } = true;
    public bool SupportsMaxOutputTokens { get; set; } = true;
    public bool SupportsPenalties { get; set; } = true;
    public IReadOnlyList<string> ReasoningEfforts { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
}

public sealed class XaiUsage
{
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long ReasoningTokens { get; set; }
    public long CachedTokens { get; set; }
    public long TotalTokens { get; set; }
    public decimal CostUsd { get; set; }
}

public sealed class TextGenerationResult
{
    public string Text { get; set; } = string.Empty;
    public string ReasoningSummary { get; set; } = string.Empty;
    public string? ResponseId { get; set; }
    public XaiUsage Usage { get; set; } = new();
}

public sealed class ImageGenerationResult
{
    public string? Url { get; set; }
    public string? Base64 { get; set; }
    public string? MimeType { get; set; }
    public string? RevisedPrompt { get; set; }
    public XaiUsage Usage { get; set; } = new();
}

public sealed class VideoGenerationRequestResult
{
    public string RequestId { get; set; } = string.Empty;
}

public sealed class VideoStatusResult
{
    public string Status { get; set; } = "pending";
    public string? Url { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Model { get; set; }
    public string? Error { get; set; }
}
