using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using x.ai_client_PC.Models;

namespace x.ai_client_PC.Services;

public sealed class LocalizationService : ObservableObject
{
    public const string RussianCode = "ru";
    public const string EnglishCode = "en";

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [RussianCode] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "xAI Grok Chat",
                ["NavText"] = "Текст",
                ["NavImages"] = "Изображения",
                ["NavVideos"] = "Видео",
                ["NavModels"] = "Модели",
                ["NavSettings"] = "Настройки",
                ["NewButton"] = "Новый",
                ["SearchTooltip"] = "Поиск по чатам",
                ["Duplicate"] = "Дублировать",
                ["Delete"] = "Удалить",
                ["SelectOrCreateChat"] = "Выберите или создайте чат",
                ["Send"] = "Отправить",
                ["Stop"] = "Стоп",
                ["Attach"] = "Прикрепить",
                ["Ready"] = "Готово",
                ["Edit"] = "Изменить",
                ["EditMessageTitle"] = "Редактировать сообщение",
                ["Branch"] = "Ветка",
                ["Regenerate"] = "Повторить",
                ["SaveMedia"] = "Сохранить медиа",
                ["Cancel"] = "Отмена",
                ["Save"] = "Сохранить",
                ["RequestId"] = "ID запроса:",
                ["Status"] = "Статус:",
                ["ModelsHeader"] = "Модели",
                ["LoadFromApi"] = "Загрузить из API",
                ["ModelsHelp"] = "Включайте модели, выбирайте модели по умолчанию и задавайте цены. Сначала сохраните API-ключ в настройках.",
                ["ColumnEnabled"] = "Вкл.",
                ["ColumnId"] = "ID",
                ["ColumnCategory"] = "Категория",
                ["ColumnDefault"] = "По умолч.",
                ["ColumnInput"] = "Ввод $/млн",
                ["ColumnOutput"] = "Вывод $/млн",
                ["SettingsHeader"] = "Настройки",
                ["ApiNotice"] = "Приложение не дает бесплатный доступ к xAI API. Нужен ваш собственный API-ключ.",
                ["Language"] = "Язык",
                ["ApiKeyLabel"] = "API-ключ (хранится зашифрованным в Windows Credential Manager)",
                ["SaveApiKey"] = "Сохранить API-ключ",
                ["Validate"] = "Проверить",
                ["BaseUrl"] = "Базовый URL",
                ["SettingsModelsHelp"] = "Загрузите доступные текстовые, графические и видео-модели из xAI API. Требуется сохраненный API-ключ.",
                ["OpenModelsScreen"] = "Открыть экран моделей",
                ["LoadedModels"] = "Загружено моделей: {0}",
                ["SystemRoles"] = "Системные роли",
                ["SaveRole"] = "Сохранить роль",
                ["AddRole"] = "Добавить роль",
                ["SaveSettings"] = "Сохранить настройки",
                ["ExportBackup"] = "Экспорт резервной копии",
                ["ImportBackup"] = "Импорт резервной копии",
                ["ChatSettings"] = "Настройки чата",
                ["Model"] = "Модель",
                ["SystemRole"] = "Системная роль",
                ["Temperature"] = "Температура",
                ["TopP"] = "Top P",
                ["MaxTokens"] = "Макс. токенов",
                ["FrequencyPenalty"] = "Штраф частоты",
                ["PresencePenalty"] = "Штраф присутствия",
                ["ReasoningEffort"] = "Усилие рассуждения",
                ["ReasoningLow"] = "Низкое",
                ["ReasoningMedium"] = "Среднее",
                ["ReasoningHigh"] = "Высокое",
                ["ReasoningXHigh"] = "Очень высокое",
                ["WebSearchEnabled"] = "Веб-поиск (Live Search устарел)",
                ["ContextMessageLimit"] = "Лимит сообщений контекста",
                ["ImageVideoOptions"] = "Параметры изображений / видео",
                ["AspectRatio"] = "Соотношение сторон",
                ["Resolution"] = "Разрешение",
                ["VideoDuration"] = "Длительность видео (секунды)",
                ["SourceImagePath"] = "Путь к исходному изображению",
                ["SourceImageUrl"] = "URL исходного изображения",
                ["ApplySettings"] = "Применить настройки",
                ["LastRequest"] = "Последний запрос",
                ["Cost"] = "Стоимость: $",
                ["PromptTokens"] = "Токены запроса: {0}",
                ["CompletionTokens"] = "Токены ответа: {0}",
                ["ReasoningTokens"] = "Токены рассуждения: {0}",
                ["ChatTotalCost"] = "Общая стоимость чата: $",
                ["ApiKeyStored"] = "Ключ сохранен безопасно",
                ["ApiKeyMissing"] = "Ключ не сохранен",
                ["StatusReady"] = "Готово",
                ["StatusGenerating"] = "Генерация...",
                ["StatusDone"] = "Готово",
                ["StatusStopped"] = "Остановлено",
                ["StatusStopping"] = "Останавливаю...",
                ["StatusSettingsSaved"] = "Настройки сохранены.",
                ["StatusChatSettingsSaved"] = "Настройки чата сохранены.",
                ["StatusBackupExported"] = "Резервная копия экспортирована (API-ключ не включен).",
                ["StatusBackupImported"] = "Резервная копия импортирована.",
                ["StatusApiKeyValid"] = "API-ключ действителен.",
                ["StatusApiKeyInvalid"] = "Проверка API-ключа не пройдена.",
                ["StatusApiKeyVerifiedModels"] = "API-ключ проверен. Загружено моделей: {0}.",
                ["StatusApiKeyVerified"] = "API-ключ проверен. Чтобы получить модели, нажмите \"Загрузить из API\".",
                ["StatusApiKeySavedButInvalid"] = "API-ключ сохранен, но проверка не пройдена.",
                ["StatusApiKeyDeleted"] = "API-ключ удален.",
                ["StatusSaveApiKeyFailed"] = "Не удалось сохранить API-ключ: {0}",
                ["StatusLoadModelsFirst"] = "Сначала сохраните API-ключ, затем загрузите модели.",
                ["StatusLoadedModels"] = "Загружено моделей из API: {0}.",
                ["StatusFailedLoadModels"] = "Не удалось загрузить модели: {0}",
                ["SendNoChat"] = "Сначала создайте или выберите чат.",
                ["SendEmpty"] = "Введите сообщение перед отправкой.",
                ["SendBusy"] = "Дождитесь завершения текущей генерации или нажмите \"Стоп\".",
                ["SendNoApiKey"] = "Сначала сохраните API-ключ в настройках.",
                ["SendNoModel"] = "Выберите модель или загрузите список моделей из API.",
                ["ErrorTitle"] = "Ошибка",
                ["ApiKeyRequiredTitle"] = "Нужен API-ключ",
                ["ApiKeyRequiredMessage"] = "Сохраните xAI API-ключ в настройках перед загрузкой моделей.",
                ["FailedLoadModelsTitle"] = "Не удалось загрузить модели",
                ["NewTextChat"] = "Новый текстовый чат",
                ["NewImageChat"] = "Новый чат изображений",
                ["NewVideoChat"] = "Новый видео-чат",
                ["NewRoleName"] = "Новая роль",
                ["DefaultRoleName"] = "Ассистент по умолчанию",
                ["DefaultRoleContent"] = "Ты Grok, полезный AI-ассистент.",
                ["MultiAgentNotice"] = "Multi-Agent модель: maxTokens, frequencyPenalty и presencePenalty отключены. reasoningEffort доступен.",
                ["RoleSystem"] = "Система",
                ["RoleUser"] = "Пользователь",
                ["RoleAssistant"] = "Ассистент",
                ["StatusPending"] = "ожидает",
                ["StatusInProgress"] = "в процессе",
                ["StatusVideoDone"] = "готово",
                ["StatusFailed"] = "ошибка",
                ["StatusExpired"] = "истекло",
                ["AssistantStopped"] = "[Остановлено]",
                ["ErrorPrefix"] = "Ошибка: {0}",
                ["ImageGenerating"] = "Генерация изображения...",
                ["ImageGenerated"] = "Изображение создано.",
                ["NoImageReturned"] = "API не вернуло изображение.",
                ["ImageResponseNoData"] = "Ответ изображения не содержит данных.",
                ["VideoStarting"] = "Запуск генерации видео...",
                ["VideoRequestStatus"] = "ID запроса: {0}\nСтатус: {1}",
                ["VideoGenerated"] = "Видео создано.",
                ["VideoGenerationEnded"] = "Генерация видео завершилась со статусом: {0}."
            },
            [EnglishCode] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AppTitle"] = "xAI Grok Chat",
                ["NavText"] = "Text",
                ["NavImages"] = "Images",
                ["NavVideos"] = "Videos",
                ["NavModels"] = "Models",
                ["NavSettings"] = "Settings",
                ["NewButton"] = "New",
                ["SearchTooltip"] = "Search chats",
                ["Duplicate"] = "Duplicate",
                ["Delete"] = "Delete",
                ["SelectOrCreateChat"] = "Select or create a chat",
                ["Send"] = "Send",
                ["Stop"] = "Stop",
                ["Attach"] = "Attach",
                ["Ready"] = "Ready",
                ["Edit"] = "Edit",
                ["EditMessageTitle"] = "Edit message",
                ["Branch"] = "Branch",
                ["Regenerate"] = "Regenerate",
                ["SaveMedia"] = "Save media",
                ["Cancel"] = "Cancel",
                ["Save"] = "Save",
                ["RequestId"] = "Request ID:",
                ["Status"] = "Status:",
                ["ModelsHeader"] = "Models",
                ["LoadFromApi"] = "Load from API",
                ["ModelsHelp"] = "Enable models, set defaults and prices. Save API key in Settings first.",
                ["ColumnEnabled"] = "Enabled",
                ["ColumnId"] = "ID",
                ["ColumnCategory"] = "Category",
                ["ColumnDefault"] = "Default",
                ["ColumnInput"] = "Input $/M",
                ["ColumnOutput"] = "Output $/M",
                ["SettingsHeader"] = "Settings",
                ["ApiNotice"] = "This app does not provide free access to xAI API. You need your own API key.",
                ["Language"] = "Language",
                ["ApiKeyLabel"] = "API Key (stored encrypted in Windows Credential Manager)",
                ["SaveApiKey"] = "Save API Key",
                ["Validate"] = "Validate",
                ["BaseUrl"] = "Base URL",
                ["SettingsModelsHelp"] = "Load available text, image and video models from xAI API. Requires a saved API key.",
                ["OpenModelsScreen"] = "Open Models screen",
                ["LoadedModels"] = "Loaded models: {0}",
                ["SystemRoles"] = "System Roles",
                ["SaveRole"] = "Save role",
                ["AddRole"] = "Add role",
                ["SaveSettings"] = "Save settings",
                ["ExportBackup"] = "Export backup",
                ["ImportBackup"] = "Import backup",
                ["ChatSettings"] = "Chat settings",
                ["Model"] = "Model",
                ["SystemRole"] = "System role",
                ["Temperature"] = "Temperature",
                ["TopP"] = "Top P",
                ["MaxTokens"] = "Max tokens",
                ["FrequencyPenalty"] = "Frequency penalty",
                ["PresencePenalty"] = "Presence penalty",
                ["ReasoningEffort"] = "Reasoning effort",
                ["ReasoningLow"] = "Low",
                ["ReasoningMedium"] = "Medium",
                ["ReasoningHigh"] = "High",
                ["ReasoningXHigh"] = "XHigh",
                ["WebSearchEnabled"] = "Web search (Live Search deprecated)",
                ["ContextMessageLimit"] = "Context message limit",
                ["ImageVideoOptions"] = "Image / Video options",
                ["AspectRatio"] = "Aspect ratio",
                ["Resolution"] = "Resolution",
                ["VideoDuration"] = "Video duration (seconds)",
                ["SourceImagePath"] = "Source image path",
                ["SourceImageUrl"] = "Source image URL",
                ["ApplySettings"] = "Apply settings",
                ["LastRequest"] = "Last request",
                ["Cost"] = "Cost: $",
                ["PromptTokens"] = "Prompt tokens: {0}",
                ["CompletionTokens"] = "Completion tokens: {0}",
                ["ReasoningTokens"] = "Reasoning tokens: {0}",
                ["ChatTotalCost"] = "Chat total cost: $",
                ["ApiKeyStored"] = "Key stored securely",
                ["ApiKeyMissing"] = "No key stored",
                ["StatusReady"] = "Ready",
                ["StatusGenerating"] = "Generating...",
                ["StatusDone"] = "Done",
                ["StatusStopped"] = "Stopped",
                ["StatusStopping"] = "Stopping...",
                ["StatusSettingsSaved"] = "Settings saved.",
                ["StatusChatSettingsSaved"] = "Chat settings saved.",
                ["StatusBackupExported"] = "Backup exported (API key excluded).",
                ["StatusBackupImported"] = "Backup imported.",
                ["StatusApiKeyValid"] = "API key is valid.",
                ["StatusApiKeyInvalid"] = "API key validation failed.",
                ["StatusApiKeyVerifiedModels"] = "API key verified. Loaded {0} models.",
                ["StatusApiKeyVerified"] = "API key verified. Use \"Load from API\" to fetch models.",
                ["StatusApiKeySavedButInvalid"] = "API key saved but validation failed.",
                ["StatusApiKeyDeleted"] = "API key deleted.",
                ["StatusSaveApiKeyFailed"] = "Failed to save API key: {0}",
                ["StatusLoadModelsFirst"] = "Save API key first, then load models.",
                ["StatusLoadedModels"] = "Loaded {0} models from API.",
                ["StatusFailedLoadModels"] = "Failed to load models: {0}",
                ["SendNoChat"] = "Create or select a chat first.",
                ["SendEmpty"] = "Enter a message before sending.",
                ["SendBusy"] = "Wait for the current generation to finish or press Stop.",
                ["SendNoApiKey"] = "Save your API key in Settings first.",
                ["SendNoModel"] = "Choose a model or load the model list from the API.",
                ["ErrorTitle"] = "Error",
                ["ApiKeyRequiredTitle"] = "API key required",
                ["ApiKeyRequiredMessage"] = "Save your xAI API key in Settings before loading models.",
                ["FailedLoadModelsTitle"] = "Failed to load models",
                ["NewTextChat"] = "New text chat",
                ["NewImageChat"] = "New image chat",
                ["NewVideoChat"] = "New video chat",
                ["NewRoleName"] = "New role",
                ["DefaultRoleName"] = "Default Assistant",
                ["DefaultRoleContent"] = "You are Grok, a helpful AI assistant.",
                ["MultiAgentNotice"] = "Multi-Agent model: maxTokens, frequencyPenalty and presencePenalty are disabled. reasoningEffort is available.",
                ["RoleSystem"] = "System",
                ["RoleUser"] = "User",
                ["RoleAssistant"] = "Assistant",
                ["StatusPending"] = "pending",
                ["StatusInProgress"] = "in progress",
                ["StatusVideoDone"] = "done",
                ["StatusFailed"] = "failed",
                ["StatusExpired"] = "expired",
                ["AssistantStopped"] = "[Stopped]",
                ["ErrorPrefix"] = "Error: {0}",
                ["ImageGenerating"] = "Generating image...",
                ["ImageGenerated"] = "Image generated.",
                ["NoImageReturned"] = "No image returned from API.",
                ["ImageResponseNoData"] = "Image response has no data.",
                ["VideoStarting"] = "Starting video generation...",
                ["VideoRequestStatus"] = "Request ID: {0}\nStatus: {1}",
                ["VideoGenerated"] = "Video generated successfully.",
                ["VideoGenerationEnded"] = "Video generation {0}."
            }
        };

    private string _languageCode = RussianCode;

    public ObservableCollection<LanguageOption> AvailableLanguages { get; } =
    [
        new(RussianCode, "Русский"),
        new(EnglishCode, "English")
    ];

    public string LanguageCode
    {
        get => _languageCode;
        set
        {
            var normalized = NormalizeLanguageCode(value);
            if (SetProperty(ref _languageCode, normalized))
            {
                CultureInfo.CurrentUICulture = Culture;
                CultureInfo.CurrentCulture = Culture;
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            }
        }
    }

    public CultureInfo Culture => CultureInfo.GetCultureInfo(LanguageCode == RussianCode ? "ru-RU" : "en-US");

    public string this[string key]
    {
        get => Get(key);
        set
        {
            // Some WPF targets bind Content/Header as TwoWay by default. Localization is read-only;
            // accepting the setter keeps those bindings from failing at startup.
        }
    }

    public string Get(string key)
    {
        if (Translations.TryGetValue(LanguageCode, out var current) && current.TryGetValue(key, out var value))
        {
            return value;
        }

        return Translations[EnglishCode].TryGetValue(key, out var fallback)
            ? fallback
            : key;
    }

    public string Format(string key, params object[] args) => string.Format(Culture, Get(key), args);

    public string GetMessageRole(MessageRole role) => role switch
    {
        MessageRole.System => Get("RoleSystem"),
        MessageRole.User => Get("RoleUser"),
        MessageRole.Assistant => Get("RoleAssistant"),
        _ => role.ToString()
    };

    public string GetVideoStatus(VideoGenerationStatus? status) => status switch
    {
        VideoGenerationStatus.Pending => Get("StatusPending"),
        VideoGenerationStatus.InProgress => Get("StatusInProgress"),
        VideoGenerationStatus.Done => Get("StatusVideoDone"),
        VideoGenerationStatus.Failed => Get("StatusFailed"),
        VideoGenerationStatus.Expired => Get("StatusExpired"),
        _ => string.Empty
    };

    public static string NormalizeLanguageCode(string? languageCode) =>
        string.Equals(languageCode, EnglishCode, StringComparison.OrdinalIgnoreCase)
            ? EnglishCode
            : RussianCode;
}

public sealed record LanguageOption(string Code, string DisplayName);
