using System.IO;
using System.Text.Json;
using FunderFinder.Models;

namespace FunderFinder.Services;

public static class DataStore
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FunderFinder");

    private static readonly string FilePath = Path.Combine(Folder, "data.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static AppData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(FilePath), Options);
                if (data is not null) return data;
            }
        }
        catch
        {
            // Corrupt or unreadable file — start fresh rather than crash on startup.
        }

        return new AppData();
    }

    public static void Save(AppData data)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Options));
        }
        catch
        {
            // Best-effort save; a failure here shouldn't crash a background app.
        }
    }
}
