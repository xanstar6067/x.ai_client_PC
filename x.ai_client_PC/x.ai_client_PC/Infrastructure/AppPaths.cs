namespace x.ai_client_PC.Infrastructure;

public static class AppPaths
{
    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "xAI_Client_PC");

    public static string DatabasePath => Path.Combine(AppDataRoot, "app.db");
    public static string AttachmentsPath => Path.Combine(AppDataRoot, "attachments");
    public static string MediaPath => Path.Combine(AppDataRoot, "media");
    public static string BackupsPath => Path.Combine(AppDataRoot, "backups");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(AttachmentsPath);
        Directory.CreateDirectory(MediaPath);
        Directory.CreateDirectory(BackupsPath);
    }
}