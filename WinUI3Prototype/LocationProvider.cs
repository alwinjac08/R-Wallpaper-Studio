using System.Diagnostics;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace WallpaperStudio;

// A windowless worker owned by the legacy tray engine. Uses the same Windows
// location API as the map, independently of whether the settings window is open.
internal static class LocationProvider
{
    public static async Task RunAsync(int parentId)
    {
        using var parent=Process.GetProcessById(parentId);
        if(!string.Equals(parent.MainModule?.FileName,Path.Combine(AppContext.BaseDirectory,"WallpaperService.exe"),StringComparison.OrdinalIgnoreCase))return;
        string directory=WallpaperStudioShared.StudioPaths.DataDirectory;
        Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,"current-location.json");
        while(!parent.HasExited)
        {
            object result;
            try
            {
                // Recreate the provider and request a new fix even in manual-default mode.
                var locator=new Geolocator { DesiredAccuracyInMeters=50 };
                var position=await locator.GetGeopositionAsync(TimeSpan.Zero,TimeSpan.FromSeconds(15));
                var point=position.Coordinate.Point.Position;
                // A successful cached fix can precede LocationStatus changing to Ready.
                // Validate the returned position; the engine independently enforces freshness.
                result=new { available=LocationQuality.IsUsable(position.Coordinate,DateTimeOffset.UtcNow.AddSeconds(-30)),latitude=point.Latitude,longitude=point.Longitude,
                    accuracy=position.Coordinate.Accuracy,source=position.Coordinate.PositionSource.ToString(),
                    timestamp=position.Coordinate.Timestamp.ToUnixTimeSeconds(),providerId=Environment.ProcessId };
            }
            catch { result=new { available=false,timestamp=DateTimeOffset.UtcNow.ToUnixTimeSeconds(),providerId=Environment.ProcessId }; }
            if(parent.HasExited)return;
            string temp=path+"."+Environment.ProcessId+".tmp";
            try { await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(result)); if(!parent.HasExited)File.Move(temp,path,true); }
            finally { if(File.Exists(temp))File.Delete(temp); }
            for(int i=0;i<5&&!parent.HasExited;i++)await Task.Delay(1000);
        }
    }
}
