using Microsoft.EntityFrameworkCore;
using x.ai_client_PC.Data;
using x.ai_client_PC.Models;

namespace x.ai_client_PC.Services;

public class DataRepository
{
    private readonly AppDbContext _db;

    public DataRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task InitializeAsync()
    {
        await _db.Database.EnsureCreatedAsync();

        if (!await _db.Settings.AnyAsync())
        {
            _db.Settings.Add(new AppSettingsEntity());
            await _db.SaveChangesAsync();
        }

        if (!await _db.Roles.AnyAsync())
        {
            _db.Roles.Add(new SystemRole
            {
                Name = "Default Assistant",
                Content = "You are Grok, a helpful AI assistant.",
                IsDefault = true
            });
            await _db.SaveChangesAsync();
        }
    }

    public async Task<AppSettingsEntity> GetSettingsAsync() =>
        await _db.Settings.FirstAsync();

    public async Task SaveSettingsAsync(AppSettingsEntity settings)
    {
        _db.Settings.Update(settings);
        await _db.SaveChangesAsync();
    }

    public async Task<List<ChatSession>> GetChatsAsync(ChatKind kind)
    {
        return await _db.Chats
            .Where(c => c.Kind == kind)
            .OrderByDescending(c => c.UpdatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<ChatSession?> GetChatAsync(string id)
    {
        return await _db.Chats
            .Include(c => c.Messages)
            .ThenInclude(m => m.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task SaveChatAsync(ChatSession chat)
    {
        chat.UpdatedAt = DateTime.UtcNow;
        var exists = await _db.Chats.AnyAsync(c => c.Id == chat.Id);
        if (exists)
        {
            _db.Chats.Update(chat);
        }
        else
        {
            _db.Chats.Add(chat);
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteChatAsync(string id)
    {
        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == id);
        if (chat is null)
        {
            return;
        }

        _db.Chats.Remove(chat);
        await _db.SaveChangesAsync();
    }

    public async Task<ChatSession> DuplicateChatAsync(string id)
    {
        var source = await GetChatAsync(id) ?? throw new InvalidOperationException("Chat not found.");
        var copy = new ChatSession
        {
            Kind = source.Kind,
            Title = source.Title + " (copy)",
            ModelId = source.ModelId,
            SystemRoleId = source.SystemRoleId,
            Temperature = source.Temperature,
            TopP = source.TopP,
            FrequencyPenalty = source.FrequencyPenalty,
            PresencePenalty = source.PresencePenalty,
            MaxTokens = source.MaxTokens,
            ReasoningEffort = source.ReasoningEffort,
            ContextMessageLimit = source.ContextMessageLimit,
            WebSearchEnabled = source.WebSearchEnabled,
            AspectRatio = source.AspectRatio,
            Resolution = source.Resolution,
            VideoDurationSeconds = source.VideoDurationSeconds,
            SourceImagePath = source.SourceImagePath,
            SourceImageUrl = source.SourceImageUrl
        };

        foreach (var msg in source.Messages.Where(m => m.IsActiveVersion).OrderBy(m => m.CreatedAt))
        {
            var newMsg = new ChatMessage
            {
                ChatId = copy.Id,
                Role = msg.Role,
                Content = msg.Content,
                ReasoningContent = msg.ReasoningContent,
                VersionIndex = msg.VersionIndex,
                IsActiveVersion = true,
                MediaLocalPath = msg.MediaLocalPath,
                MediaUrl = msg.MediaUrl,
                Attachments = msg.Attachments.Select(a => new MessageAttachment
                {
                    FileName = a.FileName,
                    LocalPath = a.LocalPath,
                    MimeType = a.MimeType,
                    RemoteUrl = a.RemoteUrl
                }).ToList()
            };
            copy.Messages.Add(newMsg);
        }

        _db.Chats.Add(copy);
        await _db.SaveChangesAsync();
        return copy;
    }

    public async Task<List<ModelInfo>> GetModelsAsync() =>
        await _db.Models.OrderBy(m => m.DisplayName).AsNoTracking().ToListAsync();

    public async Task SaveModelsAsync(IEnumerable<ModelInfo> models)
    {
        _db.Models.RemoveRange(_db.Models);
        _db.Models.AddRange(models);
        await _db.SaveChangesAsync();
    }

    public async Task<List<SystemRole>> GetRolesAsync() =>
        await _db.Roles.OrderBy(r => r.Name).AsNoTracking().ToListAsync();

    public async Task SaveRoleAsync(SystemRole role)
    {
        var exists = await _db.Roles.AnyAsync(r => r.Id == role.Id);
        if (exists)
        {
            _db.Roles.Update(role);
        }
        else
        {
            _db.Roles.Add(role);
        }

        await _db.SaveChangesAsync();
    }

    public async Task DeleteRoleAsync(string id)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == id);
        if (role is null)
        {
            return;
        }

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync();
    }
}