namespace x.ai_client_PC.Models;

public enum ChatKind
{
    Text,
    Image,
    Video
}

public enum MessageRole
{
    System,
    User,
    Assistant
}

public enum VideoGenerationStatus
{
    Pending,
    InProgress,
    Done,
    Failed,
    Expired
}

public enum ModelCategory
{
    Text,
    Image,
    Video
}

public enum AppSection
{
    Text,
    Images,
    Videos,
    Models,
    Settings
}

public enum ReasoningEffort
{
    Low,
    Medium,
    High,
    XHigh
}