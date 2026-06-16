namespace x.ai_client_PC.Models;

public class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ChatKind Kind { get; set; }
    public string Title { get; set; } = "New chat";
    public string ModelId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? SystemRoleId { get; set; }
    public string? LastResponseId { get; set; }
    public string? PromptCacheKey { get; set; }
    public double TotalCostUsd { get; set; }
    public int TotalTokens { get; set; }

    public double Temperature { get; set; } = 1.0;
    public double TopP { get; set; } = 1.0;
    public double FrequencyPenalty { get; set; }
    public double PresencePenalty { get; set; }
    public int? MaxTokens { get; set; }
    public ReasoningEffort ReasoningEffort { get; set; } = ReasoningEffort.Medium;
    public int ContextMessageLimit { get; set; } = 50;
    public bool WebSearchEnabled { get; set; }

    public string? AspectRatio { get; set; }
    public string? Resolution { get; set; }
    public int? VideoDurationSeconds { get; set; }
    public string? SourceImagePath { get; set; }
    public string? SourceImageUrl { get; set; }

    public List<ChatMessage> Messages { get; set; } = [];
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ChatId { get; set; } = string.Empty;
    public MessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ReasoningContent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ParentMessageId { get; set; }
    public int VersionIndex { get; set; }
    public bool IsActiveVersion { get; set; } = true;
    public string? ResponseId { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int ReasoningTokens { get; set; }
    public double CostUsd { get; set; }
    public bool IsStreaming { get; set; }
    public string? VideoRequestId { get; set; }
    public VideoGenerationStatus? VideoStatus { get; set; }
    public string? MediaLocalPath { get; set; }
    public string? MediaUrl { get; set; }
    public string? ErrorMessage { get; set; }

    public List<MessageAttachment> Attachments { get; set; } = [];
}

public class MessageAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string MessageId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string? MimeType { get; set; }
    public string? RemoteUrl { get; set; }
}

public class ModelInfo
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ModelCategory Category { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsDefault { get; set; }
    public bool SupportsImageInput { get; set; }
    public bool SupportsReasoning { get; set; }
    public bool IsMultiAgent { get; set; }
    public bool UsesResponsesApi { get; set; }
    public double? InputPricePerMillion { get; set; }
    public double? OutputPricePerMillion { get; set; }
    public double? ImagePrice { get; set; }
    public double? VideoPricePerSecond { get; set; }
    public string? RawJson { get; set; }
}

public class SystemRole
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class AppSettingsEntity
{
    public int Id { get; set; } = 1;
    public string BaseUrl { get; set; } = "https://api.x.ai/v1";
    public string? DefaultTextModelId { get; set; }
    public string? DefaultImageModelId { get; set; }
    public string? DefaultVideoModelId { get; set; }
    public string Theme { get; set; } = "Dark";
}