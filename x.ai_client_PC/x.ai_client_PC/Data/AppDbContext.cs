using Microsoft.EntityFrameworkCore;
using x.ai_client_PC.Models;

namespace x.ai_client_PC.Data;

public class AppDbContext : DbContext
{
    public DbSet<ChatSession> Chats => Set<ChatSession>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<MessageAttachment> Attachments => Set<MessageAttachment>();
    public DbSet<ModelInfo> Models => Set<ModelInfo>();
    public DbSet<SystemRole> Roles => Set<SystemRole>();
    public DbSet<AppSettingsEntity> Settings => Set<AppSettingsEntity>();

    private readonly string _dbPath;

    public AppDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ChatId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MessageAttachment>().HasKey(x => x.Id);
        modelBuilder.Entity<ModelInfo>().HasKey(x => x.Id);
        modelBuilder.Entity<SystemRole>().HasKey(x => x.Id);
        modelBuilder.Entity<AppSettingsEntity>().HasKey(x => x.Id);
    }
}