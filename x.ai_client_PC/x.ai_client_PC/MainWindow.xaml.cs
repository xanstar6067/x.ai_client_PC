using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using x.ai_client_PC.Models;
using x.ai_client_PC.Services;

namespace x.ai_client_PC;

public partial class MainWindow : Window
{
    private readonly AppStorage _storage = new();
    private readonly CredentialVault _credentialVault = new();
    private readonly XaiClient _xaiClient = new();

    private PersistedState _state = AppStorage.CreateDefaultState();
    private ModelCatalog _modelCatalog = XaiClient.CreateDefaultCatalog();
    private string _currentSection = "Text";
    private CancellationTokenSource? _currentRequest;
    private bool _loadingControls;
    private MediaItem? _selectedImageItem;
    private MediaItem? _selectedVideoItem;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _state = _storage.Load();
        ApplySettingsToControls();
        PopulateModelCombo();
        RefreshCredentialStatus();
        SelectSection("Text");
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        SyncSettingsFromControls();
        _storage.Save(_state);
        _currentRequest?.Cancel();
        _currentRequest?.Dispose();
        _xaiClient.Dispose();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
        {
            NewConversationButton_Click(sender, e);
            e.Handled = true;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string section)
        {
            SelectSection(section);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshConversationList();
    }

    private void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls)
        {
            return;
        }

        if (_currentSection == "Text")
        {
            RenderMessages();
        }
        else if (_currentSection == "Images")
        {
            RefreshImageHistory();
        }
        else if (_currentSection == "Videos")
        {
            RefreshVideoHistory();
        }

        UpdateUsagePanel();
    }

    private void NewConversationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSection == "Text")
        {
            var chat = new ChatSession { Title = "Новый чат" };
            _state.TextChats.Insert(0, chat);
            RefreshConversationList(chat);
        }
        else if (_currentSection == "Images")
        {
            var conversation = new MediaConversation { Kind = WorkspaceKind.Images, Title = "Новая генерация" };
            _state.ImageChats.Insert(0, conversation);
            RefreshConversationList(conversation);
        }
        else if (_currentSection == "Videos")
        {
            var conversation = new MediaConversation { Kind = WorkspaceKind.Videos, Title = "Новая генерация" };
            _state.VideoChats.Insert(0, conversation);
            RefreshConversationList(conversation);
        }

        _storage.Save(_state);
    }

    private void DuplicateConversationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSection == "Text" && ConversationList.SelectedItem is ChatSession chat)
        {
            var copy = chat.Duplicate();
            _state.TextChats.Insert(0, copy);
            RefreshConversationList(copy);
        }
        else if (_currentSection == "Images" && ConversationList.SelectedItem is MediaConversation imageConversation)
        {
            var copy = imageConversation.Duplicate();
            _state.ImageChats.Insert(0, copy);
            RefreshConversationList(copy);
        }
        else if (_currentSection == "Videos" && ConversationList.SelectedItem is MediaConversation videoConversation)
        {
            var copy = videoConversation.Duplicate();
            _state.VideoChats.Insert(0, copy);
            RefreshConversationList(copy);
        }

        _storage.Save(_state);
    }

    private void DeleteConversationButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConversationList.SelectedItem is null)
        {
            return;
        }

        var result = MessageBox.Show(this, "Удалить выбранный элемент?", "xAI Grok Chat PC", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        if (_currentSection == "Text" && ConversationList.SelectedItem is ChatSession chat && _state.TextChats.Count > 1)
        {
            _state.TextChats.Remove(chat);
        }
        else if (_currentSection == "Images" && ConversationList.SelectedItem is MediaConversation imageConversation && _state.ImageChats.Count > 1)
        {
            _state.ImageChats.Remove(imageConversation);
        }
        else if (_currentSection == "Videos" && ConversationList.SelectedItem is MediaConversation videoConversation && _state.VideoChats.Count > 1)
        {
            _state.VideoChats.Remove(videoConversation);
        }

        RefreshConversationList();
        _storage.Save(_state);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendPromptAsync();
    }

    private async Task SendPromptAsync()
    {
        var apiKey = EnsureApiKey();
        if (apiKey is null)
        {
            return;
        }

        var chat = CurrentTextChat();
        if (chat is null)
        {
            return;
        }

        SyncSettingsFromControls();

        var prompt = PromptBox.Text.Trim();
        var attachments = AttachmentList.Items.Cast<string>().ToList();
        if (string.IsNullOrWhiteSpace(prompt) && attachments.Count == 0)
        {
            return;
        }

        var userMessage = new ChatMessage
        {
            Role = "user",
            Content = prompt,
            Attachments = attachments
        };
        var assistantMessage = new ChatMessage
        {
            Role = "assistant",
            Content = string.Empty
        };

        chat.Messages.Add(userMessage);
        chat.Messages.Add(assistantMessage);
        chat.UpdatedAtUtc = DateTime.UtcNow;
        if (chat.Messages.Count <= 3 && prompt.Length > 0)
        {
            chat.Title = prompt.Length <= 48 ? prompt : prompt[..48] + "...";
            RefreshConversationList(chat);
        }

        PromptBox.Clear();
        AttachmentList.Items.Clear();
        RenderMessages();

        var assistantEditor = FindMessageEditor(assistantMessage);
        var answerBuffer = string.Empty;
        var reasoningBuffer = string.Empty;

        SetBusy(true);
        _currentRequest = new CancellationTokenSource();
        try
        {
            var result = await _xaiClient.SendTextStreamingAsync(
                _state.Settings,
                chat,
                userMessage,
                apiKey,
                delta =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        answerBuffer += delta;
                        assistantMessage.Content = answerBuffer;
                        if (assistantEditor is not null)
                        {
                            assistantEditor.Text = assistantMessage.Content;
                        }

                        MessageScroll.ScrollToEnd();
                    });
                },
                delta =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        reasoningBuffer += delta;
                        CompatibilityText.Text = "Reasoning summary streaming...";
                    });
                },
                _currentRequest.Token);

            if (string.IsNullOrWhiteSpace(answerBuffer))
            {
                answerBuffer = result.Text;
            }

            if (!string.IsNullOrWhiteSpace(result.ReasoningSummary))
            {
                reasoningBuffer = result.ReasoningSummary;
            }

            assistantMessage.Content = string.IsNullOrWhiteSpace(reasoningBuffer)
                ? answerBuffer
                : $"Reasoning summary:\n{reasoningBuffer.Trim()}\n\nAnswer:\n{answerBuffer.Trim()}";

            chat.LastResponseId = result.ResponseId;
            chat.PromptTokens += result.Usage.PromptTokens;
            chat.CompletionTokens += result.Usage.CompletionTokens;
            chat.ReasoningTokens += result.Usage.ReasoningTokens;
            chat.TotalCostUsd += result.Usage.CostUsd;
            chat.UpdatedAtUtc = DateTime.UtcNow;

            RenderMessages();
            UpdateUsagePanel();
            _storage.Save(_state);
        }
        catch (OperationCanceledException)
        {
            assistantMessage.Content = string.IsNullOrWhiteSpace(answerBuffer) ? "Остановлено пользователем." : answerBuffer + "\n\n[Остановлено]";
            RenderMessages();
        }
        catch (Exception exception)
        {
            assistantMessage.Content = exception.Message;
            assistantMessage.IsError = true;
            RenderMessages();
        }
        finally
        {
            SetBusy(false);
            _currentRequest?.Dispose();
            _currentRequest = null;
            _storage.Save(_state);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _currentRequest?.Cancel();
    }

    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            foreach (var fileName in dialog.FileNames)
            {
                AttachmentList.Items.Add(fileName);
            }
        }
    }

    private void ClearAttachmentsButton_Click(object sender, RoutedEventArgs e)
    {
        AttachmentList.Items.Clear();
    }

    private void PromptBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            _ = SendPromptAsync();
        }
    }

    private void ChooseImageSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var fileName = ChooseImageFile();
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            ImageSourceBox.Text = fileName;
        }
    }

    private async void GenerateImageButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = EnsureApiKey();
        if (apiKey is null)
        {
            return;
        }

        var conversation = CurrentImageConversation();
        if (conversation is null)
        {
            return;
        }

        SyncSettingsFromControls();
        var prompt = ImagePromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        GenerateImageButton.IsEnabled = false;
        ImageResultText.Text = "Отправляю запрос на генерацию изображения...";
        _currentRequest = new CancellationTokenSource();

        try
        {
            var results = await _xaiClient.GenerateImageAsync(_state.Settings, prompt, ImageSourceBox.Text.Trim(), apiKey, _currentRequest.Token);
            if (results.Count == 0)
            {
                throw new InvalidDataException("xAI не вернул изображение.");
            }

            foreach (var result in results)
            {
                var item = new MediaItem
                {
                    Prompt = prompt,
                    Source = string.IsNullOrWhiteSpace(ImageSourceBox.Text) ? null : ImageSourceBox.Text.Trim(),
                    Url = result.Url,
                    Model = _state.Settings.ImageModel,
                    Status = "done",
                    RevisedPrompt = result.RevisedPrompt,
                    CostUsd = result.Usage.CostUsd
                };
                conversation.Items.Insert(0, item);
                _selectedImageItem = item;
            }

            conversation.Title = prompt.Length <= 42 ? prompt : prompt[..42] + "...";
            conversation.UpdatedAtUtc = DateTime.UtcNow;
            RefreshConversationList(conversation);
            RefreshImageHistory();
            ShowImageItem(_selectedImageItem);
            _storage.Save(_state);
        }
        catch (OperationCanceledException)
        {
            ImageResultText.Text = "Генерация остановлена.";
        }
        catch (Exception exception)
        {
            var item = new MediaItem
            {
                Prompt = prompt,
                Source = ImageSourceBox.Text.Trim(),
                Model = _state.Settings.ImageModel,
                Status = "failed",
                Error = exception.Message
            };
            conversation.Items.Insert(0, item);
            _selectedImageItem = item;
            RefreshImageHistory();
            ShowImageItem(item);
            _storage.Save(_state);
        }
        finally
        {
            GenerateImageButton.IsEnabled = true;
            _currentRequest?.Dispose();
            _currentRequest = null;
        }
    }

    private async void SaveImageButton_Click(object sender, RoutedEventArgs e)
    {
        var item = _selectedImageItem;
        if (item?.Url is null && item?.LocalPath is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "JPEG image (*.jpg)|*.jpg|PNG image (*.png)|*.png|All files (*.*)|*.*",
            FileName = "grok-image.jpg"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(item.LocalPath) && File.Exists(item.LocalPath))
            {
                File.Copy(item.LocalPath, dialog.FileName, true);
            }
            else if (!string.IsNullOrWhiteSpace(item.Url))
            {
                await _xaiClient.DownloadAsync(item.Url, dialog.FileName, CancellationToken.None);
            }

            item.LocalPath = dialog.FileName;
            ImageResultText.Text = "Сохранено: " + dialog.FileName;
            _storage.Save(_state);
        }
        catch (Exception exception)
        {
            ImageResultText.Text = "Не удалось сохранить: " + exception.Message;
        }
    }

    private void ImageHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageHistoryList.SelectedItem is MediaItem item)
        {
            _selectedImageItem = item;
            ShowImageItem(item);
        }
    }

    private void ChooseVideoSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var fileName = ChooseImageFile();
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            VideoSourceBox.Text = fileName;
        }
    }

    private async void GenerateVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = EnsureApiKey();
        if (apiKey is null)
        {
            return;
        }

        var conversation = CurrentVideoConversation();
        if (conversation is null)
        {
            return;
        }

        SyncSettingsFromControls();
        var prompt = VideoPromptBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        GenerateVideoButton.IsEnabled = false;
        PollVideoButton.IsEnabled = false;
        VideoStatusText.Text = "Отправляю запрос на генерацию видео...";
        _currentRequest = new CancellationTokenSource();

        var item = new MediaItem
        {
            Prompt = prompt,
            Source = string.IsNullOrWhiteSpace(VideoSourceBox.Text) ? null : VideoSourceBox.Text.Trim(),
            Model = _state.Settings.VideoModel,
            Status = "pending"
        };
        conversation.Items.Insert(0, item);
        _selectedVideoItem = item;
        RefreshVideoHistory();

        try
        {
            var request = await _xaiClient.StartVideoAsync(_state.Settings, prompt, VideoSourceBox.Text.Trim(), apiKey, _currentRequest.Token);
            item.RequestId = request.RequestId;
            VideoStatusText.Text = "requestId: " + request.RequestId + " · pending";
            PollVideoButton.IsEnabled = true;
            conversation.Title = prompt.Length <= 42 ? prompt : prompt[..42] + "...";
            conversation.UpdatedAtUtc = DateTime.UtcNow;
            RefreshConversationList(conversation);
            await PollVideoUntilTerminalAsync(item, apiKey, _currentRequest.Token);
            _storage.Save(_state);
        }
        catch (OperationCanceledException)
        {
            item.Status = "pending";
            VideoStatusText.Text = "Polling остановлен. Можно продолжить кнопкой Poll.";
        }
        catch (Exception exception)
        {
            item.Status = "failed";
            item.Error = exception.Message;
            VideoStatusText.Text = "Ошибка: " + exception.Message;
        }
        finally
        {
            GenerateVideoButton.IsEnabled = true;
            RefreshVideoHistory();
            _currentRequest?.Dispose();
            _currentRequest = null;
            _storage.Save(_state);
        }
    }

    private async void PollVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = EnsureApiKey();
        if (apiKey is null || _selectedVideoItem?.RequestId is null)
        {
            return;
        }

        SyncSettingsFromControls();
        _currentRequest = new CancellationTokenSource();
        try
        {
            await PollVideoUntilTerminalAsync(_selectedVideoItem, apiKey, _currentRequest.Token);
            _storage.Save(_state);
        }
        catch (OperationCanceledException)
        {
            VideoStatusText.Text = "Polling остановлен.";
        }
        catch (Exception exception)
        {
            VideoStatusText.Text = "Ошибка polling: " + exception.Message;
        }
        finally
        {
            RefreshVideoHistory();
            _currentRequest?.Dispose();
            _currentRequest = null;
        }
    }

    private async Task PollVideoUntilTerminalAsync(MediaItem item, string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(item.RequestId))
        {
            return;
        }

        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await _xaiClient.GetVideoStatusAsync(_state.Settings, item.RequestId, apiKey, cancellationToken);
            item.Status = status.Status;
            item.Url = status.Url ?? item.Url;
            item.Model = status.Model ?? item.Model;
            item.Error = status.Error;
            VideoStatusText.Text = $"requestId: {item.RequestId} · {item.Status}";
            RefreshVideoHistory();

            if (status.Status is "done" or "failed" or "expired")
            {
                ShowVideoItem(item);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        VideoStatusText.Text = "Polling timeout. Можно продолжить кнопкой Poll.";
    }

    private async void SaveVideoButton_Click(object sender, RoutedEventArgs e)
    {
        var item = _selectedVideoItem;
        if (item?.Url is null && item?.LocalPath is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "MP4 video (*.mp4)|*.mp4|All files (*.*)|*.*",
            FileName = "grok-video.mp4"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(item.LocalPath) && File.Exists(item.LocalPath))
            {
                File.Copy(item.LocalPath, dialog.FileName, true);
            }
            else if (!string.IsNullOrWhiteSpace(item.Url))
            {
                await _xaiClient.DownloadAsync(item.Url, dialog.FileName, CancellationToken.None);
            }

            item.LocalPath = dialog.FileName;
            VideoStatusText.Text = "Сохранено: " + dialog.FileName;
            _storage.Save(_state);
        }
        catch (Exception exception)
        {
            VideoStatusText.Text = "Не удалось сохранить: " + exception.Message;
        }
    }

    private void VideoHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideoHistoryList.SelectedItem is MediaItem item)
        {
            _selectedVideoItem = item;
            ShowVideoItem(item);
        }
    }

    private void PlayVideoButton_Click(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Play();
    }

    private void PauseVideoButton_Click(object sender, RoutedEventArgs e)
    {
        VideoPlayer.Pause();
    }

    private async void RefreshModelsButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = EnsureApiKey(showSettingsOnMissing: false);
        SyncSettingsFromControls();
        ModelsStatusText.Text = "Загружаю модели...";
        RefreshModelsButton.IsEnabled = false;

        try
        {
            _modelCatalog = await _xaiClient.LoadModelsAsync(_state.Settings, apiKey, CancellationToken.None);
            ModelsStatusText.Text = _modelCatalog.Status;
            PopulateModelCombo();
            RefreshModelsList();
            UpdateCompatibilityPanel();
        }
        catch (Exception exception)
        {
            ModelsStatusText.Text = "Не удалось загрузить модели: " + exception.Message;
        }
        finally
        {
            RefreshModelsButton.IsEnabled = true;
        }
    }

    private void ActiveModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingControls || ActiveModelCombo.SelectedItem is null)
        {
            return;
        }

        var selected = ActiveModelCombo.SelectedItem.ToString();
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        if (_currentSection == "Text")
        {
            _state.Settings.TextModel = selected;
        }
        else if (_currentSection == "Images")
        {
            _state.Settings.ImageModel = selected;
        }
        else if (_currentSection == "Videos")
        {
            _state.Settings.VideoModel = selected;
        }

        UpdateCompatibilityPanel();
        _storage.Save(_state);
    }

    private void SettingsControl_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingControls)
        {
            return;
        }

        SyncSettingsFromControls();
        UpdateCompatibilityPanel();
    }

    private void SaveApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = ApiKeyBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show(this, "Введите новый API key или используйте Delete key для удаления.", "xAI Grok Chat PC", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _credentialVault.SaveApiKey(key);
            ApiKeyBox.Clear();
            RefreshCredentialStatus();
            MessageBox.Show(this, "API key сохранён в Windows Credential Manager.", "xAI Grok Chat PC", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "xAI Grok Chat PC", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _credentialVault.DeleteApiKey();
            ApiKeyBox.Clear();
            RefreshCredentialStatus();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "xAI Grok Chat PC", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportBackupButton_Click(object sender, RoutedEventArgs e)
    {
        SyncSettingsFromControls();
        var dialog = new SaveFileDialog
        {
            Filter = "JSON backup (*.json)|*.json|All files (*.*)|*.*",
            FileName = "xai-grok-chat-backup.json"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _storage.ExportBackup(_state, dialog.FileName);
            MessageBox.Show(this, "Backup экспортирован без API key.", "xAI Grok Chat PC", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ImportBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON backup (*.json)|*.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            _state = _storage.ImportBackup(dialog.FileName);
            ApplySettingsToControls();
            PopulateModelCombo();
            SelectSection("Text");
            _storage.Save(_state);
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length == 0)
        {
            return;
        }

        if (_currentSection == "Images")
        {
            ImageSourceBox.Text = files[0];
        }
        else if (_currentSection == "Videos")
        {
            VideoSourceBox.Text = files[0];
        }
        else
        {
            foreach (var file in files)
            {
                AttachmentList.Items.Add(file);
            }
        }
    }

    private void SelectSection(string section)
    {
        _currentSection = section;
        _loadingControls = true;

        TextView.Visibility = section == "Text" ? Visibility.Visible : Visibility.Collapsed;
        ImagesView.Visibility = section == "Images" ? Visibility.Visible : Visibility.Collapsed;
        VideosView.Visibility = section == "Videos" ? Visibility.Visible : Visibility.Collapsed;
        ModelsView.Visibility = section == "Models" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = section == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        SectionTitle.Text = section;
        SectionSubtitle.Text = section switch
        {
            "Text" => "Диалог через Responses API",
            "Images" => "Генерация и редактирование через Images API",
            "Videos" => "Асинхронная генерация и polling через Videos API",
            "Models" => "Language / image / video endpoints и fallback /models",
            "Settings" => "Ключ, base URL, backup и системная роль",
            _ => string.Empty
        };

        SetNavigationState();
        PopulateModelCombo();
        RefreshConversationList();
        RefreshModelsList();
        UpdateCompatibilityPanel();

        _loadingControls = false;
    }

    private void RefreshConversationList(object? selectItem = null)
    {
        if (ConversationList is null)
        {
            return;
        }

        var query = SearchBox.Text.Trim();
        IEnumerable<object> items = _currentSection switch
        {
            "Text" => _state.TextChats.Where(chat => MatchesSearch(chat.Title, query) || chat.Messages.Any(message => MatchesSearch(message.Content, query))).Cast<object>(),
            "Images" => _state.ImageChats.Where(chat => MatchesSearch(chat.Title, query) || chat.Items.Any(item => MatchesSearch(item.Prompt, query))).Cast<object>(),
            "Videos" => _state.VideoChats.Where(chat => MatchesSearch(chat.Title, query) || chat.Items.Any(item => MatchesSearch(item.Prompt, query))).Cast<object>(),
            _ => []
        };

        var list = items.ToList();
        ConversationList.ItemsSource = list;
        ConversationList.IsEnabled = _currentSection is "Text" or "Images" or "Videos";
        NewConversationButton.IsEnabled = ConversationList.IsEnabled;
        DuplicateConversationButton.IsEnabled = ConversationList.IsEnabled;
        DeleteConversationButton.IsEnabled = ConversationList.IsEnabled;

        if (selectItem is not null && list.Contains(selectItem))
        {
            ConversationList.SelectedItem = selectItem;
        }
        else if (ConversationList.SelectedItem is null && list.Count > 0)
        {
            ConversationList.SelectedIndex = 0;
        }
        else if (list.Count == 0)
        {
            MessagePanel.Children.Clear();
        }

        if (_currentSection == "Text")
        {
            RenderMessages();
        }
        else if (_currentSection == "Images")
        {
            RefreshImageHistory();
        }
        else if (_currentSection == "Videos")
        {
            RefreshVideoHistory();
        }
    }

    private static bool MatchesSearch(string? value, string query)
    {
        return string.IsNullOrWhiteSpace(query)
               || (!string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void RenderMessages()
    {
        MessagePanel.Children.Clear();
        var chat = CurrentTextChat();
        if (chat is null)
        {
            return;
        }

        foreach (var message in chat.Messages)
        {
            MessagePanel.Children.Add(CreateMessageBubble(message));
        }

        Dispatcher.BeginInvoke(() => MessageScroll.ScrollToEnd());
    }

    private UIElement CreateMessageBubble(ChatMessage message)
    {
        var isUser = message.Role == "user";
        var border = new Border
        {
            Tag = message,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(isUser ? 58 : 0, 0, isUser ? 0 : 58, 10),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Background = message.IsError
                ? new SolidColorBrush(Color.FromRgb(255, 242, 242))
                : isUser ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("PanelAltBrush")
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = $"{(isUser ? "You" : "Grok")} · {message.CreatedAtUtc.ToLocalTime():HH:mm}",
            FontWeight = FontWeights.SemiBold,
            Foreground = message.IsError ? (Brush)FindResource("DangerBrush") : (Brush)FindResource("MutedTextBrush")
        });

        var editor = new TextBox
        {
            Tag = message,
            Text = message.Content,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(0)
        };
        stack.Children.Add(editor);

        if (message.Attachments.Count > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Attachments:\n" + string.Join("\n", message.Attachments),
                Foreground = (Brush)FindResource("MutedTextBrush"),
                Margin = new Thickness(0, 8, 0, 0)
            });
        }

        border.Child = stack;
        return border;
    }

    private TextBox? FindMessageEditor(ChatMessage message)
    {
        foreach (var child in MessagePanel.Children.OfType<Border>())
        {
            if (!ReferenceEquals(child.Tag, message) || child.Child is not StackPanel stack)
            {
                continue;
            }

            return stack.Children.OfType<TextBox>().FirstOrDefault(editor => ReferenceEquals(editor.Tag, message));
        }

        return null;
    }

    private void RefreshImageHistory()
    {
        var conversation = CurrentImageConversation();
        ImageHistoryList.ItemsSource = conversation?.Items ?? [];
        if (_selectedImageItem is not null && conversation?.Items.Contains(_selectedImageItem) == true)
        {
            ImageHistoryList.SelectedItem = _selectedImageItem;
        }
        else if (conversation?.Items.Count > 0)
        {
            ImageHistoryList.SelectedIndex = 0;
        }
    }

    private void RefreshVideoHistory()
    {
        var conversation = CurrentVideoConversation();
        VideoHistoryList.ItemsSource = conversation?.Items ?? [];
        if (_selectedVideoItem is not null && conversation?.Items.Contains(_selectedVideoItem) == true)
        {
            VideoHistoryList.SelectedItem = _selectedVideoItem;
        }
        else if (conversation?.Items.Count > 0)
        {
            VideoHistoryList.SelectedIndex = 0;
        }
    }

    private void ShowImageItem(MediaItem? item)
    {
        SaveImageButton.IsEnabled = false;
        if (item is null)
        {
            ImageResultText.Text = "Результат появится здесь.";
            GeneratedImagePreview.Source = null;
            return;
        }

        ImageResultText.Text = item.Status == "failed"
            ? "Ошибка: " + item.Error
            : $"{item.Status} · {item.Url ?? item.LocalPath ?? "нет URL"}";

        var source = item.LocalPath ?? item.Url;
        if (string.IsNullOrWhiteSpace(source))
        {
            GeneratedImagePreview.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(Path.GetFullPath(source));
            bitmap.EndInit();
            GeneratedImagePreview.Source = bitmap;
            SaveImageButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            ImageResultText.Text = "Не удалось показать изображение: " + exception.Message;
        }
    }

    private void ShowVideoItem(MediaItem? item)
    {
        SaveVideoButton.IsEnabled = false;
        PlayVideoButton.IsEnabled = false;
        PauseVideoButton.IsEnabled = false;

        if (item is null)
        {
            VideoStatusText.Text = "Статус видео появится здесь.";
            VideoPlayer.Source = null;
            return;
        }

        VideoStatusText.Text = string.IsNullOrWhiteSpace(item.RequestId)
            ? item.Status
            : $"requestId: {item.RequestId} · {item.Status}";

        var source = item.LocalPath ?? item.Url;
        if (string.IsNullOrWhiteSpace(source))
        {
            VideoPlayer.Source = null;
            return;
        }

        VideoPlayer.Source = Uri.TryCreate(source, UriKind.Absolute, out var absoluteUri)
            ? absoluteUri
            : new Uri(Path.GetFullPath(source));
        SaveVideoButton.IsEnabled = true;
        PlayVideoButton.IsEnabled = true;
        PauseVideoButton.IsEnabled = true;
    }

    private ChatSession? CurrentTextChat()
    {
        return ConversationList.SelectedItem as ChatSession ?? _state.TextChats.FirstOrDefault();
    }

    private MediaConversation? CurrentImageConversation()
    {
        return ConversationList.SelectedItem as MediaConversation ?? _state.ImageChats.FirstOrDefault();
    }

    private MediaConversation? CurrentVideoConversation()
    {
        return ConversationList.SelectedItem as MediaConversation ?? _state.VideoChats.FirstOrDefault();
    }

    private void PopulateModelCombo()
    {
        if (ActiveModelCombo is null)
        {
            return;
        }

        _loadingControls = true;
        var selected = _currentSection switch
        {
            "Images" => _state.Settings.ImageModel,
            "Videos" => _state.Settings.VideoModel,
            _ => _state.Settings.TextModel
        };

        var models = _currentSection switch
        {
            "Images" => _modelCatalog.ImageModels.Select(model => model.Id).ToList(),
            "Videos" => _modelCatalog.VideoModels.Select(model => model.Id).ToList(),
            _ => _modelCatalog.LanguageModels.Select(model => model.Id).ToList()
        };

        if (!models.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            models.Insert(0, selected);
        }

        ActiveModelCombo.ItemsSource = models;
        ActiveModelCombo.SelectedItem = models.FirstOrDefault(model => string.Equals(model, selected, StringComparison.OrdinalIgnoreCase));
        _loadingControls = false;
    }

    private void RefreshModelsList()
    {
        if (ModelsList is null)
        {
            return;
        }

        ModelsList.ItemsSource = _modelCatalog.LanguageModels
            .Concat(_modelCatalog.ImageModels)
            .Concat(_modelCatalog.VideoModels)
            .ToList();
        ModelsStatusText.Text = _modelCatalog.Status;
    }

    private void ApplySettingsToControls()
    {
        _loadingControls = true;
        BaseUrlBox.Text = _state.Settings.BaseUrl;
        SystemPromptBox.Text = _state.Settings.SystemPrompt;
        TemperatureBox.Text = _state.Settings.Temperature.ToString(CultureInfo.InvariantCulture);
        TopPBox.Text = _state.Settings.TopP.ToString(CultureInfo.InvariantCulture);
        MaxTokensBox.Text = _state.Settings.MaxOutputTokens.ToString(CultureInfo.InvariantCulture);
        FrequencyPenaltyBox.Text = _state.Settings.FrequencyPenalty.ToString(CultureInfo.InvariantCulture);
        PresencePenaltyBox.Text = _state.Settings.PresencePenalty.ToString(CultureInfo.InvariantCulture);
        ContextLimitBox.Text = _state.Settings.ContextMessageLimit.ToString(CultureInfo.InvariantCulture);
        ImageCountBox.Text = _state.Settings.ImageCount.ToString(CultureInfo.InvariantCulture);
        VideoDurationBox.Text = _state.Settings.VideoDurationSeconds.ToString(CultureInfo.InvariantCulture);
        WebSearchCheck.IsChecked = _state.Settings.WebSearchEnabled;
        XSearchCheck.IsChecked = _state.Settings.XSearchEnabled;
        UsePreviousResponseCheck.IsChecked = _state.Settings.UsePreviousResponseId;
        StoreServerResponsesCheck.IsChecked = _state.Settings.StoreServerResponses;
        SelectComboValue(ReasoningEffortCombo, _state.Settings.ReasoningEffort);
        SelectComboValue(ImageAspectCombo, _state.Settings.ImageAspectRatio);
        SelectComboValue(ImageResolutionCombo, _state.Settings.ImageResolution);
        SelectComboValue(VideoAspectCombo, _state.Settings.VideoAspectRatio);
        SelectComboValue(VideoResolutionCombo, _state.Settings.VideoResolution);
        _loadingControls = false;
    }

    private void SyncSettingsFromControls()
    {
        if (_state.Settings is null)
        {
            return;
        }

        _state.Settings.BaseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text) ? "https://api.x.ai/v1" : BaseUrlBox.Text.Trim();
        _state.Settings.SystemPrompt = SystemPromptBox.Text.Trim();
        _state.Settings.ReasoningEffort = GetComboValue(ReasoningEffortCombo, "low");
        _state.Settings.Temperature = Clamp(ParseDouble(TemperatureBox.Text, _state.Settings.Temperature), 0, 2);
        _state.Settings.TopP = Clamp(ParseDouble(TopPBox.Text, _state.Settings.TopP), 0, 1);
        _state.Settings.FrequencyPenalty = Clamp(ParseDouble(FrequencyPenaltyBox.Text, _state.Settings.FrequencyPenalty), -2, 2);
        _state.Settings.PresencePenalty = Clamp(ParseDouble(PresencePenaltyBox.Text, _state.Settings.PresencePenalty), -2, 2);
        _state.Settings.MaxOutputTokens = Math.Clamp(ParseInt(MaxTokensBox.Text, _state.Settings.MaxOutputTokens), 0, 1_000_000);
        _state.Settings.ContextMessageLimit = Math.Clamp(ParseInt(ContextLimitBox.Text, _state.Settings.ContextMessageLimit), 1, 200);
        _state.Settings.WebSearchEnabled = WebSearchCheck.IsChecked == true;
        _state.Settings.XSearchEnabled = XSearchCheck.IsChecked == true;
        _state.Settings.UsePreviousResponseId = UsePreviousResponseCheck.IsChecked == true;
        _state.Settings.StoreServerResponses = StoreServerResponsesCheck.IsChecked == true;
        _state.Settings.ImageAspectRatio = GetComboValue(ImageAspectCombo, "auto");
        _state.Settings.ImageResolution = GetComboValue(ImageResolutionCombo, "1k");
        _state.Settings.ImageCount = Math.Clamp(ParseInt(ImageCountBox.Text, _state.Settings.ImageCount), 1, 4);
        _state.Settings.VideoAspectRatio = GetComboValue(VideoAspectCombo, "16:9");
        _state.Settings.VideoResolution = GetComboValue(VideoResolutionCombo, "480p");
        _state.Settings.VideoDurationSeconds = Math.Clamp(ParseInt(VideoDurationBox.Text, _state.Settings.VideoDurationSeconds), 1, 15);
    }

    private void UpdateCompatibilityPanel()
    {
        if (CompatibilityText is null)
        {
            return;
        }

        var model = _state.Settings.TextModel;
        var isMultiAgent = model.Contains("multi-agent", StringComparison.OrdinalIgnoreCase)
                           || model.Contains("4.20", StringComparison.OrdinalIgnoreCase);
        var isReasoning = model.Contains("grok-4", StringComparison.OrdinalIgnoreCase)
                          || model.Contains("reasoning", StringComparison.OrdinalIgnoreCase);

        MaxTokensBox.IsEnabled = !isMultiAgent;
        FrequencyPenaltyBox.IsEnabled = !isMultiAgent && !isReasoning;
        PresencePenaltyBox.IsEnabled = !isMultiAgent && !isReasoning;

        CompatibilityText.Text = isMultiAgent
            ? "Multi-Agent: max tokens и penalties отключены; effort управляет числом агентов."
            : isReasoning
                ? "Reasoning model: penalties отключены, effort управляет глубиной reasoning."
                : "Параметры совместимы с обычной текстовой моделью.";
    }

    private void UpdateUsagePanel()
    {
        if (CurrentTextChat() is { } chat)
        {
            UsageText.Text = $"Prompt: {chat.PromptTokens:N0}, completion: {chat.CompletionTokens:N0}, reasoning: {chat.ReasoningTokens:N0}, cost: ${chat.TotalCostUsd:0.####}";
            LastResponseIdText.Text = string.IsNullOrWhiteSpace(chat.LastResponseId)
                ? "previousResponseId: нет"
                : "previousResponseId: " + chat.LastResponseId;
        }
    }

    private void RefreshCredentialStatus()
    {
        var hasKey = _credentialVault.HasApiKey();
        CredentialStatusText.Text = hasKey
            ? "API key сохранён"
            : "API key не сохранён";
        CredentialStatusText.Foreground = hasKey
            ? (Brush)FindResource("SuccessBrush")
            : (Brush)FindResource("WarningBrush");
    }

    private string? EnsureApiKey(bool showSettingsOnMissing = true)
    {
        var apiKey = _credentialVault.ReadApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return apiKey;
        }

        if (showSettingsOnMissing)
        {
            MessageBox.Show(this, "Сначала сохраните xAI API key в Settings.", "xAI Grok Chat PC", MessageBoxButton.OK, MessageBoxImage.Information);
            SelectSection("Settings");
        }

        return null;
    }

    private void SetBusy(bool isBusy)
    {
        SendButton.IsEnabled = !isBusy;
        StopButton.IsEnabled = isBusy;
        RefreshModelsButton.IsEnabled = !isBusy;
    }

    private void SetNavigationState()
    {
        SetNavButton(TextSectionButton, _currentSection == "Text");
        SetNavButton(ImagesSectionButton, _currentSection == "Images");
        SetNavButton(VideosSectionButton, _currentSection == "Videos");
        SetNavButton(ModelsSectionButton, _currentSection == "Models");
        SetNavButton(SettingsSectionButton, _currentSection == "Settings");
    }

    private void SetNavButton(Button button, bool selected)
    {
        button.Background = selected ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("PanelBrush");
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private static string? ChooseImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|All files (*.*)|*.*"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static string GetComboValue(ComboBox comboBox, string fallback)
    {
        return comboBox.SelectedItem switch
        {
            ComboBoxItem item when item.Content is not null => item.Content.ToString() ?? fallback,
            string value => value,
            _ => fallback
        };
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
               || int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed)
            ? parsed
            : fallback;
    }

    private static double ParseDouble(string value, double fallback)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
               || double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed)
            ? parsed
            : fallback;
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}
