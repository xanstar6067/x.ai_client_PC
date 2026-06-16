using System.Text.Json;
using x.ai_client_PC.Models;

namespace x.ai_client_PC.Services;

public class BackupService
{
    private readonly DataRepository _repo;

    public BackupService(DataRepository repo)
    {
        _repo = repo;
    }

    public async Task ExportAsync(string filePath)
    {
        var settings = await _repo.GetSettingsAsync();
        var backup = new BackupDto
        {
            Version = 1,
            ExportedAt = DateTime.UtcNow,
            Settings = settings,
            Roles = await _repo.GetRolesAsync(),
            Models = await _repo.GetModelsAsync(),
            TextChats = await LoadChatsAsync(ChatKind.Text),
            ImageChats = await LoadChatsAsync(ChatKind.Image),
            VideoChats = await LoadChatsAsync(ChatKind.Video)
        };

        var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task ImportAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        var backup = JsonSerializer.Deserialize<BackupDto>(json)
            ?? throw new InvalidOperationException("Invalid backup file.");

        await _repo.SaveSettingsAsync(backup.Settings);
        await _repo.SaveModelsAsync(backup.Models);

        foreach (var role in backup.Roles)
        {
            await _repo.SaveRoleAsync(role);
        }

        foreach (var chat in backup.TextChats.Concat(backup.ImageChats).Concat(backup.VideoChats))
        {
            await _repo.SaveChatAsync(chat);
        }
    }

    private async Task<List<ChatSession>> LoadChatsAsync(ChatKind kind)
    {
        var ids = (await _repo.GetChatsAsync(kind)).Select(c => c.Id);
        var chats = new List<ChatSession>();
        foreach (var id in ids)
        {
            var chat = await _repo.GetChatAsync(id);
            if (chat is not null)
            {
                chats.Add(chat);
            }
        }

        return chats;
    }

    private class BackupDto
    {
        public int Version { get; set; }
        public DateTime ExportedAt { get; set; }
        public AppSettingsEntity Settings { get; set; } = new();
        public List<SystemRole> Roles { get; set; } = [];
        public List<ModelInfo> Models { get; set; } = [];
        public List<ChatSession> TextChats { get; set; } = [];
        public List<ChatSession> ImageChats { get; set; } = [];
        public List<ChatSession> VideoChats { get; set; } = [];
    }
}