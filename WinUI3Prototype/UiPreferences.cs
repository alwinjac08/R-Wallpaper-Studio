using System.Text.Json;

namespace WallpaperStudio;

internal sealed class UiPreferences
{
    public bool SuppressApplyConfirmation { get; set; }
    public static UiPreferences Load(string path)=>File.Exists(path)
        ? JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(path))??new()
        : new();
    public async Task SaveAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(this));File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
