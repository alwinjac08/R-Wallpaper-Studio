using Windows.Devices.Geolocation;
namespace WallpaperStudio;
internal static class LocationQuality
{
    internal static bool IsUsable(Geocoordinate c,DateTimeOffset minimum)
    {
        var p=c.Point.Position;
        return double.IsFinite(p.Latitude)&&double.IsFinite(p.Longitude)&&Math.Abs(p.Latitude)<=90&&Math.Abs(p.Longitude)<=180
            && double.IsFinite(c.Accuracy)&&c.Accuracy>0&&c.Accuracy<=150
            && c.PositionSource!=PositionSource.Default&&c.PositionSource!=PositionSource.IPAddress
            && c.Timestamp>=minimum&&DateTimeOffset.UtcNow-c.Timestamp<=TimeSpan.FromSeconds(30)
            && c.Timestamp<=DateTimeOffset.UtcNow.AddSeconds(5);
    }
}
