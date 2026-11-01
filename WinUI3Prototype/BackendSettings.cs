using System.Text.Json;
using System.Text.Json.Serialization;

namespace WallpaperStudio;

public sealed class BackendSettings
{
    public string Theme { get; set; }="Light";
    public string AwayWallpaper { get; set; }="";
    public string LockScreenWallpaper { get; set; }="";
    public bool MatchDefaultWallpaper { get; set; }
    public bool MatchHomeScreen { get; set; }
    public List<string> HomeWallpapers { get; set; }=[];
    public string SelectedHomeWallpaper { get; set; }="";
    public string CarouselSelectionToken { get; set; }="";
    public bool CarouselEnabled { get; set; }
    public bool HideLocationWallpaper { get; set; }
    public int CarouselIntervalMinutes { get; set; }=15;
    public double HomeLatitude { get; set; }
    public double HomeLongitude { get; set; }
    public double RadiusMeters { get; set; }=150;
    [JsonExtensionData] public Dictionary<string,JsonElement>? Extra { get; set; }
    public static BackendSettings Load(string path)=>JsonSerializer.Deserialize<BackendSettings>(File.ReadAllText(path))
        ?? throw new InvalidDataException("Wallpaper settings are empty.");
    public async Task SaveAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions { WriteIndented=true })); File.Move(temp,path,true); }
        finally { if(File.Exists(temp))File.Delete(temp); }
    }
}
