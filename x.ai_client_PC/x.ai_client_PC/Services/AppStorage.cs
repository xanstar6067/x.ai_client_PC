using System.IO;
using System.Text.Json;
using x.ai_client_PC.Models;

namespace x.ai_client_PC.Services;

public sealed class AppStorage
{
    private const string AppFolderName = "xAI_Grok_Chat_PC";
    private const string StateFileName = "state.json";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string AppDataPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName);

    public string StateFilePath => Path.Combine(AppDataPath, StateFileName);

    public PersistedState Load()
    {
        Directory.CreateDirectory(AppDataPath);

        if (!File.Exists(StateFilePath))
        {
            return CreateDefaultState();
        }

        try
        {
            var json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<PersistedState>(json, _jsonOptions) ?? CreateDefaultState();
            Normalize(state);
            return state;
        }
        catch
        {
            return CreateDefaultState();
        }
    }

    public PersistedState ImportBackup(string sourcePath)
    {
        var json = File.ReadAllText(sourcePath);
        var state = JsonSerializer.Deserialize<PersistedState>(json, _jsonOptions)
                    ?? throw new InvalidDataException("Резервная копия не содержит состояние приложения.");
        Normalize(state);
        return state;
    }

    public void ExportBackup(PersistedState state, string targetPath)
    {
        Normalize(state);
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        File.WriteAllText(targetPath, json);
    }

    public void Save(PersistedState state)
    {
        Directory.CreateDirectory(AppDataPath);
        Normalize(state);

        var json = JsonSerializer.Serialize(state, _jsonOptions);
        var tempPath = StateFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, StateFilePath, true);
    }

    public static PersistedState CreateDefaultState()
    {
        return new PersistedState
        {
            Settings = new AppSettings(),
            TextChats =
            [
                new ChatSession
                {
                    Title = "Первый чат",
                    Messages =
                    [
                        new ChatMessage
                        {
                            Role = "assistant",
                            Content = "Готов к работе. Вставьте xAI API-ключ в настройках, выберите модель и отправьте сообщение."
                        }
                    ]
                }
            ],
            ImageChats =
            [
                new MediaConversation
                {
                    Kind = WorkspaceKind.Images,
                    Title = "Изображения"
                }
            ],
            VideoChats =
            [
                new MediaConversation
                {
                    Kind = WorkspaceKind.Videos,
                    Title = "Видео"
                }
            ]
        };
    }

    private static void Normalize(PersistedState state)
    {
        state.Settings ??= new AppSettings();
        state.Settings.BaseUrl = string.IsNullOrWhiteSpace(state.Settings.BaseUrl)
            ? "https://api.x.ai/v1"
            : state.Settings.BaseUrl.Trim();
        state.Settings.TextModel = EmptyToDefault(state.Settings.TextModel, "grok-4.3");
        state.Settings.ImageModel = EmptyToDefault(state.Settings.ImageModel, "grok-imagine-image-quality");
        state.Settings.VideoModel = EmptyToDefault(state.Settings.VideoModel, "grok-imagine-video");
        state.Settings.ReasoningEffort = EmptyToDefault(state.Settings.ReasoningEffort, "low");
        state.Settings.ImageAspectRatio = EmptyToDefault(state.Settings.ImageAspectRatio, "auto");
        state.Settings.ImageResolution = EmptyToDefault(state.Settings.ImageResolution, "1k");
        state.Settings.VideoAspectRatio = EmptyToDefault(state.Settings.VideoAspectRatio, "16:9");
        state.Settings.VideoResolution = EmptyToDefault(state.Settings.VideoResolution, "480p");
        state.Settings.ContextMessageLimit = Math.Clamp(state.Settings.ContextMessageLimit, 1, 200);
        state.Settings.ImageCount = Math.Clamp(state.Settings.ImageCount, 1, 4);
        state.Settings.VideoDurationSeconds = Math.Clamp(state.Settings.VideoDurationSeconds, 1, 15);

        state.TextChats ??= [];
        state.ImageChats ??= [];
        state.VideoChats ??= [];

        if (state.TextChats.Count == 0)
        {
            state.TextChats.Add(new ChatSession { Title = "Новый чат" });
        }

        if (state.ImageChats.Count == 0)
        {
            state.ImageChats.Add(new MediaConversation { Kind = WorkspaceKind.Images, Title = "Изображения" });
        }

        if (state.VideoChats.Count == 0)
        {
            state.VideoChats.Add(new MediaConversation { Kind = WorkspaceKind.Videos, Title = "Видео" });
        }
    }

    private static string EmptyToDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
