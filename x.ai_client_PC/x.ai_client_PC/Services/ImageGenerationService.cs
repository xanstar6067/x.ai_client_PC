using x.ai_client_PC.Infrastructure;
using x.ai_client_PC.Models;
using x.ai_client_PC.Services.Api;

namespace x.ai_client_PC.Services;

public class ImageGenerationService
{
    private readonly XaiApiClient _api;
    private readonly DataRepository _repo;

    public ImageGenerationService(XaiApiClient api, DataRepository repo)
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
            Content = "Generating image...",
            IsStreaming = true,
            ParentMessageId = userMessage.Id
        };

        chat.Messages.Add(userMessage);
        chat.Messages.Add(assistant);
        await _repo.SaveChatAsync(chat);

        try
        {
            ImageGenerationResponse response;
            if (!string.IsNullOrWhiteSpace(chat.SourceImagePath) || !string.IsNullOrWhiteSpace(chat.SourceImageUrl))
            {
                var imageUrl = !string.IsNullOrWhiteSpace(chat.SourceImageUrl)
                    ? chat.SourceImageUrl!
                    : await ToDataUriAsync(chat.SourceImagePath!);

                response = await _api.EditImageAsync(new ImageEditRequest
                {
                    Model = chat.ModelId,
                    Prompt = prompt,
                    AspectRatio = chat.AspectRatio,
                    Resolution = chat.Resolution,
                    Image = new ImageSourceDto { Url = imageUrl, Type = "image_url" }
                }, ct);
            }
            else
            {
                response = await _api.GenerateImageAsync(new ImageGenerationRequest
                {
                    Model = chat.ModelId,
                    Prompt = prompt,
                    AspectRatio = chat.AspectRatio,
                    Resolution = chat.Resolution
                }, ct);
            }

            var imageData = response.Data.FirstOrDefault();
            if (imageData is null)
            {
                throw new InvalidOperationException("No image returned from API.");
            }

            var localPath = await SaveImageResultAsync(imageData, ct);
            assistant.MediaLocalPath = localPath;
            assistant.MediaUrl = imageData.Url;
            assistant.Content = "Image generated.";
            assistant.IsStreaming = false;

            var model = (await _repo.GetModelsAsync()).FirstOrDefault(m => m.Id == chat.ModelId);
            assistant.CostUsd = model?.ImagePrice ?? 0.07;
            chat.TotalCostUsd += assistant.CostUsd;
        }
        catch (Exception ex)
        {
            assistant.IsStreaming = false;
            assistant.ErrorMessage = ex.Message;
            assistant.Content = $"Error: {ex.Message}";
        }

        await _repo.SaveChatAsync(chat);
        return assistant;
    }

    private async Task<string> SaveImageResultAsync(ImageDataDto data, CancellationToken ct)
    {
        byte[] bytes;
        string ext;

        if (!string.IsNullOrWhiteSpace(data.B64Json))
        {
            bytes = Convert.FromBase64String(data.B64Json);
            ext = ".png";
        }
        else if (!string.IsNullOrWhiteSpace(data.Url))
        {
            bytes = await _api.DownloadBytesAsync(data.Url, ct);
            ext = ".png";
        }
        else
        {
            throw new InvalidOperationException("Image response has no data.");
        }

        var path = Path.Combine(AppPaths.MediaPath, $"{Guid.NewGuid():N}{ext}");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

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