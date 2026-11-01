using System.Globalization;
using System.Text.Json;

namespace WallpaperStudio;

public enum WallpaperRole { Default, LockScreen, Location }

public sealed record WallpaperImage(string Id, string Name, string DisplayPath, string Source,
    string SizeText, string ModifiedText);

public sealed class StudioState
{
    public WallpaperImage DefaultImage { get; set; } = BuiltIn("default", "Sample-default.png", "Placeholder.png", "1.33MB");
    public WallpaperImage IndependentLockImage { get; set; } = BuiltIn("lock", "Sample-lock.png", "PlaceholderAlternate.png", "9.78MB");
    public WallpaperImage LocationImage { get; set; }
    public bool MatchDefault { get; set; }
    public bool MatchHomeScreen { get; set; }
    public bool CarouselEnabled { get; set; }
    public int RotationMinutes { get; set; }=15;
    public string? SelectedImageId { get; private set; }
    public List<WallpaperImage> CarouselImages { get; }
    public WallpaperImage EffectiveLockImage => MatchDefault ? DefaultImage : IndependentLockImage;
    public WallpaperImage? SelectedImage => CarouselImages.Find(i => i.Id == SelectedImageId);

    public StudioState()
    {
        var cafe = BuiltIn("cafe", "Sample-location.png", "Placeholder.png", "1.86MB");
        CarouselImages = [BuiltIn("home", "Sample-alternate.png", "PlaceholderAlternate.png", "1.86MB"), cafe];
        LocationImage = cafe;
    }

    private static WallpaperImage BuiltIn(string id, string name, string asset, string size) =>
        new(id, name, Path.Combine(AppContext.BaseDirectory,"Assets",asset), "ms-appx:///Assets/" + asset, size, "10-09-2026");

    public WallpaperImage ForRole(WallpaperRole role) => role switch
    {
        WallpaperRole.Default => DefaultImage,
        WallpaperRole.LockScreen => EffectiveLockImage,
        _ => LocationImage
    };

    public void ChooseImage(WallpaperRole role, WallpaperImage image)
    {
        switch(role)
        {
            case WallpaperRole.Default: DefaultImage=image; break;
            case WallpaperRole.LockScreen: IndependentLockImage=image; break;
            case WallpaperRole.Location:
                LocationImage=image;
                if(!WallpaperMedia.IsVideo(image.DisplayPath)) AddImage(image);
                SelectedImageId=image.Id;
                break;
        }
    }

    public void AddImage(WallpaperImage image)
    {
        if(!CarouselImages.Any(i=>i.Id==image.Id)) CarouselImages.Add(image);
    }

    public void SelectImage(string id)
    {
        if(CarouselImages.Any(i=>i.Id==id)) SelectedImageId=id;
    }
    public void ClearSelection()=>SelectedImageId=null;
    public void MoveCarouselImage(string id,int destination)
    {
        int source=CarouselImages.FindIndex(image=>image.Id==id);
        if(source<0)return;
        destination=Math.Clamp(destination,0,CarouselImages.Count-1);
        if(source==destination)return;
        var image=CarouselImages[source];CarouselImages.RemoveAt(source);CarouselImages.Insert(destination,image);
    }

    public void SetSelectedActive()
    {
        if(SelectedImage is { } image) LocationImage=image;
    }

    public void RemoveSelected()
    {
        if(SelectedImage is not { } selected) return;
        int index=CarouselImages.IndexOf(selected);
        CarouselImages.Remove(selected);
        SelectedImageId=CarouselImages.Count==0 ? null : CarouselImages[Math.Min(index,CarouselImages.Count-1)].Id;
    }
}

public sealed record MapPoint(double Latitude, double Longitude)
{
    public bool IsValid => double.IsFinite(Latitude) && double.IsFinite(Longitude)
        && Latitude>=-90 && Latitude<=90 && Longitude>=-180 && Longitude<=180;
    public string Description => string.Create(CultureInfo.InvariantCulture,$"{Latitude:F5}, {Longitude:F5}");
    public bool IsNear(MapPoint other)
    {
        // One metre tolerance, including longitude wrap at the antimeridian.
        double lat=(other.Latitude-Latitude)*Math.PI/180;
        double lon=(other.Longitude-Longitude)*Math.PI/180;
        double a=Math.Pow(Math.Sin(lat/2),2)+Math.Cos(Latitude*Math.PI/180)*Math.Cos(other.Latitude*Math.PI/180)*Math.Pow(Math.Sin(lon/2),2);
        return 6371000*2*Math.Asin(Math.Sqrt(Math.Clamp(a,0,1)))<1;
    }
}

public sealed record SavedLocation(string Id, string Name, double Latitude, double Longitude)
{
    public MapPoint Point => new(Latitude,Longitude);
}

public sealed class LocationStore(string directory)
{
    public string FilePath => Path.Combine(directory,"saved-locations.json");
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase, WriteIndented=true };
    public List<SavedLocation> Load()
    {
        if(!File.Exists(FilePath)) return [];
        var items=JsonSerializer.Deserialize<List<SavedLocation>>(File.ReadAllText(FilePath),JsonOptions)
            ?? throw new InvalidDataException("The saved locations file is empty.");
        if(items.Any(p=>!p.Point.IsValid || string.IsNullOrWhiteSpace(p.Id)))
            throw new InvalidDataException("The saved locations file contains an invalid location.");
        return items;
    }
    public async Task SaveAsync(IReadOnlyCollection<SavedLocation> items)
    {
        if(items.Any(p=>!p.Point.IsValid)) throw new ArgumentException("Invalid map coordinates.");
        Directory.CreateDirectory(directory);
        string temporary=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(items,JsonOptions));
            File.Move(temporary,FilePath,true);
        }
        finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
}
