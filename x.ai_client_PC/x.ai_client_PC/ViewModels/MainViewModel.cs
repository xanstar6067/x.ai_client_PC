using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using x.ai_client_PC.Infrastructure;
using x.ai_client_PC.Models;
using x.ai_client_PC.Services;
using x.ai_client_PC.Services.Api;
using x.ai_client_PC.Views;

namespace x.ai_client_PC.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DataRepository _repo;
    private readonly XaiApiClient _api;
    private readonly ModelCatalogService _modelCatalog;
    private readonly ChatGenerationService _chatService;
    private readonly ImageGenerationService _imageService;
    private readonly VideoGenerationService _videoService;
    private readonly BackupService _backupService;

    private CancellationTokenSource? _generationCts;

    [ObservableProperty] private AppSection _currentSection = AppSection.Text;
    [ObservableProperty] private ChatKind _currentChatKind = ChatKind.Text;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private ChatSession? _selectedChat;
    [ObservableProperty] private ModelInfo? _selectedModel;
    [ObservableProperty] private SystemRole? _selectedRole;
    [ObservableProperty] private AppSettingsEntity? _settings;
    [ObservableProperty] private string _apiKeyInput = string.Empty;
    [ObservableProperty] private bool _hasApiKey;
    [ObservableProperty] private double _lastRequestCost;
    [ObservableProperty] private int _lastPromptTokens;
    [ObservableProperty] private int _lastCompletionTokens;
    [ObservableProperty] private int _lastReasoningTokens;
    [ObservableProperty] private bool _isMultiAgentModel;
    [ObservableProperty] private string _multiAgentNotice = string.Empty;

    public ObservableCollection<ChatSession> Chats { get; } = [];
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public ObservableCollection<ModelInfo> Models { get; } = [];
    public ObservableCollection<SystemRole> Roles { get; } = [];
    public ObservableCollection<string> PendingAttachments { get; } = [];

    public IEnumerable<ChatSession> FilteredChats => string.IsNullOrWhiteSpace(SearchQuery)
        ? Chats
        : Chats.Where(c => c.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));

    public MainViewModel(
        DataRepository repo,
        XaiApiClient api,
        ModelCatalogService modelCatalog,
        ChatGenerationService chatService,
        ImageGenerationService imageService,
        VideoGenerationService videoService,
        BackupService backupService)
    {
        _repo = repo;
        _api = api;
        _modelCatalog = modelCatalog;
        _chatService = chatService;
        _imageService = imageService;
        _videoService = videoService;
        _backupService = backupService;
    }

    public async Task InitializeAsync()
    {
        AppPaths.EnsureDirectories();
        await _repo.InitializeAsync();

        Settings = await _repo.GetSettingsAsync();
        HasApiKey = WindowsCredentialStore.LoadApiKey() is not null;

        var apiKey = WindowsCredentialStore.LoadApiKey();
        _api.Configure(new XaiApiOptions { BaseUrl = Settings.BaseUrl, ApiKey = apiKey });

        await LoadRolesAsync();
        await LoadModelsAsync();
        await LoadChatsAsync();
    }

    [RelayCommand]
    private async Task SelectSectionAsync(string section)
    {
        CurrentSection = Enum.Parse<AppSection>(section);
        CurrentChatKind = CurrentSection switch
        {
            AppSection.Images => ChatKind.Image,
            AppSection.Videos => ChatKind.Video,
            _ => ChatKind.Text
        };

        if (CurrentSection is AppSection.Text or AppSection.Images or AppSection.Videos)
        {
            await LoadChatsAsync();
            SelectedChat = null;
            Messages.Clear();
        }
    }

    [RelayCommand]
    private async Task NewChatAsync()
    {
        var defaultModel = Models.FirstOrDefault(m =>
            m.IsDefault && m.Category == (CurrentChatKind switch
            {
                ChatKind.Image => ModelCategory.Image,
                ChatKind.Video => ModelCategory.Video,
                _ => ModelCategory.Text
            }))
            ?? Models.FirstOrDefault(m => m.Category == ModelCategory.Text);

        var chat = new ChatSession
        {
            Kind = CurrentChatKind,
            Title = $"New {CurrentChatKind} chat",
            ModelId = defaultModel?.Id ?? string.Empty,
            SystemRoleId = Roles.FirstOrDefault(r => r.IsDefault)?.Id,
            AspectRatio = "16:9",
            Resolution = "1024x1024",
            VideoDurationSeconds = 5
        };

        await _repo.SaveChatAsync(chat);
        await LoadChatsAsync();
        await SelectChatAsync(chat);
    }

    [RelayCommand]
    private async Task SelectChatAsync(ChatSession? chat)
    {
        if (chat is null)
        {
            return;
        }

        SelectedChat = await _repo.GetChatAsync(chat.Id);
        Messages.Clear();
        if (SelectedChat is not null)
        {
            foreach (var msg in SelectedChat.Messages.Where(m => m.IsActiveVersion).OrderBy(m => m.CreatedAt))
            {
                Messages.Add(msg);
            }

            SelectedModel = Models.FirstOrDefault(m => m.Id == SelectedChat.ModelId);
            SelectedRole = Roles.FirstOrDefault(r => r.Id == SelectedChat.SystemRoleId);
            UpdateModelConstraints();
        }
    }

    [RelayCommand]
    private async Task DeleteChatAsync(ChatSession? chat)
    {
        if (chat is null)
        {
            return;
        }

        await _repo.DeleteChatAsync(chat.Id);
        if (SelectedChat?.Id == chat.Id)
        {
            SelectedChat = null;
            Messages.Clear();
        }

        await LoadChatsAsync();
    }

    [RelayCommand]
    private async Task DuplicateChatAsync(ChatSession? chat)
    {
        if (chat is null)
        {
            return;
        }

        var copy = await _repo.DuplicateChatAsync(chat.Id);
        await LoadChatsAsync();
        await SelectChatAsync(copy);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (SelectedChat is null || string.IsNullOrWhiteSpace(InputText) || IsGenerating)
        {
            return;
        }

        var text = InputText;
        var attachments = PendingAttachments.ToList();
        InputText = string.Empty;
        PendingAttachments.Clear();

        _generationCts = new CancellationTokenSource();
        IsGenerating = true;
        StatusMessage = "Generating...";

        try
        {
            ChatMessage result;
            if (SelectedChat.Kind == ChatKind.Text)
            {
                result = await _chatService.SendMessageAsync(SelectedChat, text, attachments, _generationCts.Token);
            }
            else if (SelectedChat.Kind == ChatKind.Image)
            {
                result = await _imageService.GenerateAsync(SelectedChat, text, _generationCts.Token);
            }
            else
            {
                result = await _videoService.GenerateAsync(SelectedChat, text, _generationCts.Token);
            }

            await RefreshSelectedChatAsync();
            LastRequestCost = result.CostUsd;
            LastPromptTokens = result.PromptTokens;
            LastCompletionTokens = result.CompletionTokens;
            LastReasoningTokens = result.ReasoningTokens;
            StatusMessage = "Done";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Stopped";
            await RefreshSelectedChatAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsGenerating = false;
            _generationCts?.Dispose();
            _generationCts = null;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _generationCts?.Cancel();
        StatusMessage = "Stopping...";
    }

    [RelayCommand]
    private async Task RegenerateAsync(ChatMessage? message)
    {
        if (SelectedChat is null || message is null || message.Role != MessageRole.Assistant)
        {
            return;
        }

        _generationCts = new CancellationTokenSource();
        IsGenerating = true;
        try
        {
            if (SelectedChat.Kind == ChatKind.Text)
            {
                await _chatService.RegenerateAsync(SelectedChat, message, _generationCts.Token);
            }
            else if (SelectedChat.Kind == ChatKind.Image)
            {
                var prompt = SelectedChat.Messages.LastOrDefault(m => m.Role == MessageRole.User)?.Content ?? string.Empty;
                await _imageService.GenerateAsync(SelectedChat, prompt, _generationCts.Token);
            }
            else
            {
                var prompt = SelectedChat.Messages.LastOrDefault(m => m.Role == MessageRole.User)?.Content ?? string.Empty;
                await _videoService.GenerateAsync(SelectedChat, prompt, _generationCts.Token);
            }

            await RefreshSelectedChatAsync();
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            WindowsCredentialStore.DeleteApiKey();
            HasApiKey = false;
            _api.Configure(new XaiApiOptions { BaseUrl = Settings?.BaseUrl ?? "https://api.x.ai/v1" });
            return;
        }

        WindowsCredentialStore.SaveApiKey(ApiKeyInput);
        HasApiKey = true;
        _api.Configure(new XaiApiOptions { BaseUrl = Settings?.BaseUrl ?? "https://api.x.ai/v1", ApiKey = ApiKeyInput });
        ApiKeyInput = string.Empty;

        var valid = await _api.ValidateApiKeyAsync();
        if (valid)
        {
            try
            {
                await LoadModelsAsync(refresh: true);
                StatusMessage = $"API key verified. Loaded {Models.Count} models.";
            }
            catch
            {
                StatusMessage = "API key verified. Use 'Load models from API' to fetch models.";
            }
        }
        else
        {
            StatusMessage = "API key saved but validation failed.";
        }
    }

    [RelayCommand]
    private async Task ValidateApiKeyAsync()
    {
        var valid = await _api.ValidateApiKeyAsync();
        StatusMessage = valid ? "API key is valid." : "API key validation failed.";
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        if (!HasApiKey)
        {
            StatusMessage = "Save API key first, then load models.";
            MessageBox.Show("Save your xAI API key in Settings before loading models.", "API key required",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await LoadModelsAsync(refresh: true);
            StatusMessage = $"Loaded {Models.Count} models from API.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load models: {ex.Message}";
            MessageBox.Show(ex.Message, "Failed to load models", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (Settings is null)
        {
            return;
        }

        await _repo.SaveSettingsAsync(Settings);
        _api.Configure(new XaiApiOptions
        {
            BaseUrl = Settings.BaseUrl,
            ApiKey = WindowsCredentialStore.LoadApiKey()
        });
        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON backup (*.json)|*.json",
            FileName = $"xai-backup-{DateTime.Now:yyyyMMdd-HHmmss}.json"
        };

        if (dialog.ShowDialog() == true)
        {
            await _backupService.ExportAsync(dialog.FileName);
            StatusMessage = "Backup exported (API key excluded).";
        }
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        var dialog = new OpenFileDialog { Filter = "JSON backup (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
        {
            await _backupService.ImportAsync(dialog.FileName);
            await InitializeAsync();
            StatusMessage = "Backup imported.";
        }
    }

    [RelayCommand]
    private void AttachFiles()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.webp",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            foreach (var file in dialog.FileNames)
            {
                PendingAttachments.Add(file);
            }
        }
    }

    [RelayCommand]
    private async Task AddDroppedFilesAsync(string[]? files)
    {
        if (files is null)
        {
            return;
        }

        foreach (var file in files.Where(File.Exists))
        {
            var saved = await _chatService.SaveDroppedFileAsync(file);
            PendingAttachments.Add(saved);
        }
    }

    [RelayCommand]
    private void SaveMedia(ChatMessage? message)
    {
        if (message?.MediaLocalPath is null || !File.Exists(message.MediaLocalPath))
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = Path.GetFileName(message.MediaLocalPath),
            Filter = "All files|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            File.Copy(message.MediaLocalPath, dialog.FileName, true);
        }
    }

    [RelayCommand]
    private async Task SaveChatSettingsAsync()
    {
        if (SelectedChat is null)
        {
            return;
        }

        if (SelectedModel is not null)
        {
            SelectedChat.ModelId = SelectedModel.Id;
        }

        if (SelectedRole is not null)
        {
            SelectedChat.SystemRoleId = SelectedRole.Id;
        }

        await _repo.SaveChatAsync(SelectedChat);
        UpdateModelConstraints();
        StatusMessage = "Chat settings saved.";
    }

    [RelayCommand]
    private async Task SaveRoleAsync(SystemRole? role)
    {
        if (role is null)
        {
            return;
        }

        await _repo.SaveRoleAsync(role);
        await LoadRolesAsync();
    }

    [RelayCommand]
    private async Task AddRoleAsync()
    {
        var role = new SystemRole { Name = "New role", Content = string.Empty };
        await _repo.SaveRoleAsync(role);
        await LoadRolesAsync();
    }

    partial void OnSelectedModelChanged(ModelInfo? value) => UpdateModelConstraints();

    partial void OnSearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredChats));

    partial void OnSelectedChatChanged(ChatSession? value)
    {
        if (value is not null && (value.Messages is null || value.Messages.Count == 0))
        {
            _ = SelectChatAsync(value);
        }
    }

    [RelayCommand]
    private async Task EditMessageAsync(ChatMessage? message)
    {
        if (SelectedChat is null || message is null)
        {
            return;
        }

        var dialog = new EditMessageDialog(message.Content) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        message.Content = dialog.EditedText;
        await _repo.SaveChatAsync(SelectedChat);
        await RefreshSelectedChatAsync();
    }

    [RelayCommand]
    private async Task BranchMessageAsync(ChatMessage? message)
    {
        if (SelectedChat is null || message is null)
        {
            return;
        }

        var siblings = SelectedChat.Messages
            .Where(m => m.ParentMessageId == message.ParentMessageId && m.Role == message.Role)
            .ToList();

        foreach (var s in siblings)
        {
            s.IsActiveVersion = false;
        }

        var branch = new ChatMessage
        {
            ChatId = SelectedChat.Id,
            Role = message.Role,
            Content = message.Content,
            ParentMessageId = message.ParentMessageId ?? message.Id,
            VersionIndex = siblings.Count,
            IsActiveVersion = true
        };

        SelectedChat.Messages.Add(branch);
        await _repo.SaveChatAsync(SelectedChat);
        await RefreshSelectedChatAsync();
    }

    private void UpdateModelConstraints()
    {
        IsMultiAgentModel = SelectedModel?.IsMultiAgent == true || XaiApiClient.IsMultiAgentModel(SelectedChat?.ModelId ?? string.Empty);
        MultiAgentNotice = IsMultiAgentModel
            ? "Multi-Agent model: maxTokens, frequencyPenalty and presencePenalty are disabled. reasoningEffort is available."
            : string.Empty;
    }

    private async Task RefreshSelectedChatAsync()
    {
        if (SelectedChat is null)
        {
            return;
        }

        await SelectChatAsync(SelectedChat);
        await LoadChatsAsync();
    }

    private async Task LoadChatsAsync()
    {
        Chats.Clear();
        foreach (var chat in await _repo.GetChatsAsync(CurrentChatKind))
        {
            Chats.Add(chat);
        }

        OnPropertyChanged(nameof(FilteredChats));
    }

    private async Task LoadModelsAsync(bool refresh = false)
    {
        if (refresh && HasApiKey)
        {
            await _modelCatalog.RefreshModelsAsync();
        }

        Models.Clear();
        foreach (var model in await _repo.GetModelsAsync())
        {
            Models.Add(model);
        }
    }

    private async Task LoadRolesAsync()
    {
        Roles.Clear();
        foreach (var role in await _repo.GetRolesAsync())
        {
            Roles.Add(role);
        }
    }
}