using x.ai_client_PC.Infrastructure;
using x.ai_client_PC.Models;
using x.ai_client_PC.Services.Api;

namespace x.ai_client_PC.Services;

public class VideoGenerationService
{
    private readonly XaiApiClient _api;
    private readonly DataRepository _repo;

    public VideoGenerationService(XaiApiClient api, DataRepository repo)
    {
        _api = api;
        _repo = repo;
    }

    public async Task<ChatMessage> GenerateAsync(ChatSession chat, string prompt, CancellationToken ct)
    {
        var userMessage = new ChatMessage
        {
            ChatId = chat.Id,
            Role = MessageRole.User,
            Content = prompt
        };

        var assistant = new ChatMessage
        {
            ChatId = chat.Id,
            Role = MessageRole.Assistant,
            Content = "Starting video generation...",
            IsStreaming = true,
            VideoStatus = VideoGenerationStatus.Pending,
            ParentMessageId = userMessage.Id
        };

        chat.Messages.Add(userMessage);
        chat.Messages.Add(assistant);
        await _repo.SaveChatAsync(chat);

        try
        {
            ImageSourceDto? image = null;
            if (!string.IsNullOrWhiteSpace(chat.SourceImageUrl))
            {
                image = new ImageSourceDto { Url = chat.SourceImageUrl, Type = "image_url" };
            }
            else if (!string.IsNullOrWhiteSpace(chat.SourceImagePath))
            {
                image = new ImageSourceDto { Url = await ToDataUriAsync(chat.SourceImagePath), Type = "image_url" };
            }

            var start = await _api.StartVideoGenerationAsync(new VideoGenerationRequest
            {
                Model = chat.ModelId,
                Prompt = prompt,
                Duration = chat.VideoDurationSeconds ?? 5,
                AspectRatio = chat.AspectRatio,
                Resolution = chat.Resolution,
                Image = image
            }, ct);

            assistant.VideoRequestId = start.RequestId;
            assistant.VideoStatus = MapStatus(start.Status ?? "pending");
            assistant.Content = $"Request ID: {start.RequestId}\nStatus: {assistant.VideoStatus}";
            await _repo.SaveChatAsync(chat);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                var status = await _api.GetVideoStatusAsync(start.RequestId, ct);
                assistant.VideoStatus = MapStatus(status.Status);
                assistant.Content = $"Request ID: {start.RequestId}\nStatus: {status.Status}";

                if (status.Status is "done")
                {
                    var url = status.Video?.Url;
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        var localPath = Path.Combine(AppPaths.MediaPath, $"{Guid.NewGuid():N}.mp4");
                        var bytes = await _api.DownloadBytesAsync(url, ct);
                        await File.WriteAllBytesAsync(localPath, bytes, ct);
                        assistant.MediaLocalPath = localPath;
                        assistant.MediaUrl = url;
                    }

                    assistant.Content = "Video generated successfully.";
                    var model = (await _repo.GetModelsAsync()).FirstOrDefault(m => m.Id == chat.ModelId);
                    var duration = chat.VideoDurationSeconds ?? 5;
                    assistant.CostUsd = (model?.VideoPricePerSecond ?? 0.05) * duration;
                    chat.TotalCostUsd += assistant.CostUsd;
                    break;
                }

                if (status.Status is "failed" or "expired")
                {
                    assistant.ErrorMessage = status.Error?.ToString() ?? status.Status;
                    assistant.Content = $"Video generation {status.Status}.";
                    break;
                }

                await _repo.SaveChatAsync(chat);
            }
        }
        catch (Exception ex)
        {
            assistant.ErrorMessage = ex.Message;
            assistant.Content = $"Error: {ex.Message}";
            assistant.VideoStatus = VideoGenerationStatus.Failed;
        }
        finally
        {
            assistant.IsStreaming = false;
            await _repo.SaveChatAsync(chat);
        }

        return assistant;
    }

    private static VideoGenerationStatus MapStatus(string status) => status.ToLowerInvariant() switch
    {
        "done" => VideoGenerationStatus.Done,
        "failed" => VideoGenerationStatus.Failed,
        "expired" => VideoGenerationStatus.Expired,
        "in_progress" or "progress" => VideoGenerationStatus.InProgress,
        _ => VideoGenerationStatus.Pending
    };

    private static async Task<string> ToDataUriAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var mime = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/png"
        };
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}