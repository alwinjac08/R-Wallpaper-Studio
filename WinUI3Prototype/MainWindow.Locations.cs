using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Devices.Geolocation;

namespace WallpaperStudio;

public sealed partial class MainWindow
{
    private const string MapOrigin="https://wallpaper-studio.local";
    private readonly Canvas locationsCard=new() { Width=980,Height=271 };
    private WebView2 mapView=null!;
    private Grid mapLayers=null!;
    private DispatcherTimer? mapMaintenance;
    private DateTimeOffset lastMapFix=DateTimeOffset.MinValue;
    private TextBlock mapStatus=null!;
    private Border mapFallback=null!;
    private LocationStore locationStore=null!;
    private readonly List<SavedLocation> savedLocations=[];
    private MapPoint? pendingLocation;
    private string? selectedLocationId;
    private bool mapReady,mapInitializing,windowActive,locationBusy,savingLocation,locationsReadFailed;
    private Geolocator? geolocator;
    private readonly TaskCompletionSource<bool> mapReadySignal=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string DataDirectory => verification || capture
        ? Path.Combine(AppContext.BaseDirectory,"VerificationData")
        : WallpaperStudioShared.StudioPaths.DataDirectory;

    private void BuildLocations()
    {
        locationStore=new LocationStore(DataDirectory);
        if(!verification && !capture)
        {
            try { savedLocations.AddRange(locationStore.Load()); }
            catch { locationsReadFailed=true; }
        }
        Surface(locationsCard,0,0,980,271,Card);
        Text(locationsCard,"Saved Locations",17,17,18,semibold:true);
        mapStatus=Text(locationsCard,"Loading map…",190,21,12,"#49454F",width:760);
        mapStatus.TextTrimming=TextTrimming.CharacterEllipsis;
        var holder=Surface(locationsCard,17,62,946,192,Paper);
        var layers=new Grid(); mapLayers=layers;holder.Child=layers;
        mapView=new WebView2 { Width=946,Height=192,DefaultBackgroundColor=Microsoft.UI.Colors.Transparent };
        layers.Children.Add(mapView);
        mapFallback=new Border { Background=Brush(Paper),CornerRadius=new CornerRadius(14),Visibility=Visibility.Collapsed };
        layers.Children.Add(mapFallback);
        mapView.Loaded+=async (_,_)=>await InitializeMapAsync();
        Place(wallpaperContent,locationsCard,17,857);
        if(!verification&&!capture&&!releaseVerification)
        {
            mapMaintenance=new DispatcherTimer { Interval=TimeSpan.FromSeconds(15) };
            mapMaintenance.Tick+=async (_,_)=> {
                if(closed||!windowActive)return;
                if(!mapReady&&!mapInitializing) { RecreateMapBrowser();await InitializeMapAsync(); }
                else if(mapReady) {
                    if(DateTimeOffset.UtcNow-lastMapFix>TimeSpan.FromSeconds(30))LocationUnavailable("Refreshing current location…");
                    await RequestCurrentLocationAsync(false);
                }
            };
            mapMaintenance.Start();
        }
    }
    private void RecreateMapBrowser()
    {
        mapReady=false;
        mapLayers.Children.Remove(mapView);
        try { mapView.Close(); } catch { }
        mapView=new WebView2 { Width=946,Height=192,DefaultBackgroundColor=Microsoft.UI.Colors.Transparent };
        mapLayers.Children.Insert(0,mapView);
    }

    private async Task InitializeMapAsync()
    {
        if(mapInitializing || mapReady || closed || releaseVerification) return;
        mapInitializing=true;
        try
        {
            Directory.CreateDirectory(DataDirectory);
            var environment=await CoreWebView2Environment.CreateWithOptionsAsync(null,Path.Combine(DataDirectory,"MapBrowser"),new CoreWebView2EnvironmentOptions()).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            await mapView.EnsureCoreWebView2Async(environment).AsTask().WaitAsync(TimeSpan.FromSeconds(20));
            var core=mapView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled=false;
            core.Settings.AreDevToolsEnabled=verification;
            core.Settings.IsStatusBarEnabled=false;
            core.Settings.IsZoomControlEnabled=false;
            core.Settings.UserAgent+=" RWallpaperStudio/2.1";
            core.SetVirtualHostNameToFolderMapping("wallpaper-studio.local",Path.Combine(AppContext.BaseDirectory,"Assets"),CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting+=(_,args)=> {
                if(!Uri.TryCreate(args.Uri,UriKind.Absolute,out var uri) || uri.GetLeftPart(UriPartial.Authority)!=MapOrigin) args.Cancel=true;
            };
            core.NewWindowRequested+=async (_,args)=> {
                args.Handled=true;
                if(Uri.TryCreate(args.Uri,UriKind.Absolute,out var uri) && uri.Scheme=="https" && uri.Host=="www.openstreetmap.org")
                    await Windows.System.Launcher.LaunchUriAsync(uri);
            };
            core.PermissionRequested+=(_,args)=>args.State=CoreWebView2PermissionState.Deny; // Location is supplied by the native Windows API.
            core.WebMessageReceived+=MapMessageReceived;
            core.ProcessFailed+=(_,_)=>MapFailed("Map interrupted. Reconnecting automatically…");
            core.NavigationCompleted+=(_,args)=> {
                if(!args.IsSuccess) MapFailed("The map could not be loaded. Close and reopen the app to retry.");
            };
            core.Navigate(MapOrigin+"/Map/index.html"+(verification || capture?"?test=1":""));
        }
        catch(Exception ex)
        {
            MapFailed("Unable to start the map. Check that Microsoft Edge WebView2 Runtime is installed.");
            mapReadySignal.TrySetException(ex);
        }
        finally { mapInitializing=false; }
    }
    private void MapFailed(string message)
    {
        if(closed) return;
        mapReady=false;
        mapStatus.Text=message;
        mapFallback.Child=new TextBlock { Text=message,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(24),VerticalAlignment=VerticalAlignment.Center,Foreground=Brush("#49454F"),FontFamily=inter,FontSize=14 };
        mapFallback.Visibility=Visibility.Visible;
    }
    private async void MapMessageReceived(CoreWebView2 sender,CoreWebView2WebMessageReceivedEventArgs args)
    {
        if(closed || !args.Source.StartsWith(MapOrigin+"/Map/",StringComparison.Ordinal)) return;
        try
        {
            using var json=JsonDocument.Parse(args.WebMessageAsJson);
            var root=json.RootElement;
            switch(root.GetProperty("type").GetString())
            {
                case "ready":
                    mapReady=true;
                    mapFallback.Visibility=Visibility.Collapsed;
                    PostLocations(true,false);
                    mapStatus.Text=locationsReadFailed?"Saved locations could not be read. The original file has been kept.":$"{savedLocations.Count} saved · Click on the map to choose a place";
                    if(verification || capture) PostMap(new { type="status",message="Test map — sample coordinates are used for checks." });
                    mapReadySignal.TrySetResult(true);
                    if(windowActive && !verification && !capture) await RequestCurrentLocationAsync(false);
                    break;
                case "pick":
                    if(root.TryGetProperty("latitude",out var lat) && root.TryGetProperty("longitude",out var lon)
                        && lat.TryGetDouble(out double latitude) && lon.TryGetDouble(out double longitude))
                        SetPendingLocation(new MapPoint(latitude,longitude));
                    break;
                case "save": await SavePendingLocationAsync(); break;
                case "select": SelectSavedLocation(root.GetProperty("id").GetString()); break;
                case "remove": await RemoveSavedLocationAsync(); break;
                case "locate": if(!verification && !capture) await RequestCurrentLocationAsync(true); break;
            }
        }
        catch(JsonException) { /* Ignore malformed messages; the bridge never executes received code. */ }
        catch(Exception ex) { mapStatus.Text="Map action could not be completed: "+ex.Message; }
    }
    private void PostMap(object message)
    {
        if(mapReady && !closed) mapView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message,LocationStore.JsonOptions));
    }
    private void PostLocations(bool initial=false,bool clearPending=false)=>
        PostMap(new { type="locations",locations=savedLocations,initial,clearPending });
    private void SetPendingLocation(MapPoint point)
    {
        if(!point.IsValid || savingLocation) return;
        pendingLocation=point;
        bool duplicate=savedLocations.Any(l=>l.Point.IsNear(point));
        selectedLocationId=null;
        if(duplicate) { SelectSavedLocation(savedLocations.First(l=>l.Point.IsNear(point)).Id); return; }
        bool canAdd=!duplicate && !locationsReadFailed;
        string message=locationsReadFailed?"Saved locations could not be read; adding is unavailable.":duplicate?"This place is already saved.":"Selected "+point.Description+" · Click + to save";
        PostMap(new { type="candidate",canAdd,duplicate,message }); mapStatus.Text=message;
    }
    private void SelectSavedLocation(string? id)
    {
        if(savingLocation || !savedLocations.Any(l=>l.Id==id)) return;
        selectedLocationId=id; pendingLocation=null;
        PostMap(new { type="selected",id,canRemove=!locationsReadFailed });
        mapStatus.Text="Saved place selected · Click Remove to delete it";
    }
    private async Task RemoveSavedLocationAsync()
    {
        if(savingLocation || locationsReadFailed || selectedLocationId==null) return;
        var selected=savedLocations.Find(l=>l.Id==selectedLocationId); if(selected==null) return;
        savingLocation=true;
        try
        {
            var next=savedLocations.Where(l=>l.Id!=selected.Id).ToList();
            await locationStore.SaveAsync(next);
            savedLocations.Remove(selected); selectedLocationId=null; pendingLocation=null;
            PostLocations(false,true); mapStatus.Text=$"{savedLocations.Count} saved · Place removed";
        }
        catch(Exception ex)
        {
            PostMap(new { type="selected",id=selected.Id,canRemove=true });
            mapStatus.Text="Unable to remove location: "+ex.Message;
        }
        finally { savingLocation=false; }
    }
    private async Task SavePendingLocationAsync()
    {
        if(savingLocation || locationsReadFailed || pendingLocation is not { IsValid:true } point || savedLocations.Any(l=>l.Point.IsNear(point))) return;
        savingLocation=true;
        try
        {
            var added=new SavedLocation(Guid.NewGuid().ToString("N"),"Saved location "+(savedLocations.Count+1),point.Latitude,point.Longitude);
            var next=savedLocations.Append(added).ToList();
            await locationStore.SaveAsync(next);
            savedLocations.Add(added); pendingLocation=null; PostLocations(false,true);
            mapStatus.Text=$"{savedLocations.Count} saved · Location saved";
        }
        catch(Exception ex)
        {
            mapStatus.Text="Unable to save this location: "+ex.Message;
            PostMap(new { type="candidate",canAdd=true,message="Could not save the location. Click + to retry." });
        }
        finally { savingLocation=false; }
    }
    private async Task RequestCurrentLocationAsync(bool center)
    {
        if(closed || locationBusy || !windowActive) return;
        locationBusy=true;
        try
        {
            mapStatus.Text="Finding your current location…";
            var access=geolocator==null?await Geolocator.RequestAccessAsync():GeolocationAccessStatus.Allowed;
            if(closed) return;
            if(access!=GeolocationAccessStatus.Allowed)
            {
                LocationUnavailable("Location access is unavailable. Allow it in Windows to show the blue pin; you can still save places."); return;
            }
            if(geolocator!=null)
            {
                geolocator.PositionChanged-=LocationChanged;geolocator.StatusChanged-=LocationStatusChanged;
                geolocator=null;
            }
            if(geolocator==null)
            {
                geolocator=new Geolocator { DesiredAccuracyInMeters=50,MovementThreshold=25,ReportInterval=10000 };
                geolocator.PositionChanged+=LocationChanged;
                geolocator.StatusChanged+=LocationStatusChanged;
            }
            var position=await geolocator.GetGeopositionAsync(TimeSpan.Zero,TimeSpan.FromSeconds(15));
            if(!closed) ShowPosition(position,center);
        }
        catch(UnauthorizedAccessException) { LocationUnavailable("Windows location access is off. You can still choose and save places on the map."); }
        catch { LocationUnavailable("Current location could not be found. Use ◎ to retry; you can still save places."); }
        finally { locationBusy=false; }
    }
    private void ShowCurrentLocation(BasicGeoposition point,bool center)
    {
        if(closed || !new MapPoint(point.Latitude,point.Longitude).IsValid) return;
        PostMap(new { type="current",latitude=point.Latitude,longitude=point.Longitude,center });
        bool saved=savedLocations.Any(l=>l.Point.IsNear(new MapPoint(point.Latitude,point.Longitude)));
        mapStatus.Text=$"{savedLocations.Count} saved · "+(saved?"Current location is saved":"Current location is not saved · Click + to add");
    }
    private void ShowPosition(Geoposition position,bool center)
    {
        if(!LocationQuality.IsUsable(position.Coordinate,DateTimeOffset.UtcNow.AddSeconds(-30)))
        {
            LocationUnavailable("Windows location is old or too approximate (accuracy "+Math.Round(position.Coordinate.Accuracy)+" m). Waiting for a fresh fix…");return;
        }
        lastMapFix=position.Coordinate.Timestamp;
        ShowCurrentLocation(position.Coordinate.Point.Position,center);
    }
    private void LocationChanged(Geolocator sender,PositionChangedEventArgs args)=>
        DispatcherQueue.TryEnqueue(()=>ShowPosition(args.Position,false));
    private void LocationStatusChanged(Geolocator sender,StatusChangedEventArgs args)
    {
        if(args.Status is PositionStatus.Disabled or PositionStatus.NotAvailable or PositionStatus.NoData)
            DispatcherQueue.TryEnqueue(()=>LocationUnavailable("Current location is unavailable. Use ◎ to retry; saved places remain on the map."));
    }
    private void LocationUnavailable(string message)
    {
        if(closed) return;
        mapStatus.Text=message; PostMap(new { type="locationUnavailable",message });
    }
    private void DisposeMap()
    {
        mapMaintenance?.Stop();
        if(geolocator!=null) { geolocator.PositionChanged-=LocationChanged; geolocator.StatusChanged-=LocationStatusChanged; }
        mapView?.Close();
    }
}
