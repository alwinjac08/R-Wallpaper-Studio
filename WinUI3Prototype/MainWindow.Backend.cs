using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.Storage;

namespace WallpaperStudio;

public sealed partial class MainWindow
{
    private BackendSettings backend=new();
    private Button applyButton=null!,rotationButton=null!;
    private TextBlock rotationLabel=null!;
    private TextBlock applicationStatus=null!;
    private bool backendLoaded,applying;
    private bool settingActive;
    private async Task SetSelectedCarouselActiveAsync()
    {
        if(settingActive || document.SelectedImage is not { } selected)return;
        settingActive=true;RefreshCarousel();
        try
        {
            if(!verification && !capture)
            {
                if(!backendLoaded)throw new InvalidOperationException("Wait for settings to finish loading.");
                if(!File.Exists(selected.DisplayPath)||WallpaperMedia.IsVideo(selected.DisplayPath))throw new InvalidOperationException("Select an existing still image.");
                var next=File.Exists(SettingsPath)?BackendSettings.Load(SettingsPath):backend;
                next.SelectedHomeWallpaper=selected.DisplayPath;next.CarouselEnabled=true;
                if(!next.HomeWallpapers.Contains(selected.DisplayPath,StringComparer.OrdinalIgnoreCase))next.HomeWallpapers.Add(selected.DisplayPath);
                next.CarouselSelectionToken=Guid.NewGuid().ToString("N");
                await next.SaveAsync(SettingsPath);
                backend.SelectedHomeWallpaper=selected.DisplayPath;backend.CarouselSelectionToken=next.CarouselSelectionToken;
                string engine=Path.Combine(AppContext.BaseDirectory,"WallpaperService.exe");
                bool running=false;
                foreach(var process in Process.GetProcessesByName("WallpaperService"))using(process)
                    try { running|=string.Equals(process.MainModule?.FileName,engine,StringComparison.OrdinalIgnoreCase); } catch { }
                if(!running)Process.Start(new ProcessStartInfo(engine) { WorkingDirectory=AppContext.BaseDirectory,UseShellExecute=false,CreateNoWindow=true })?.Dispose();
            }
            document.LocationImage=selected;
            RefreshCards();RefreshCarousel();
        }
        catch(Exception ex) { await ShowErrorAsync("Unable to set active wallpaper",ex.Message); }
        finally { settingActive=false;if(!closed)RefreshCarousel(); }
    }
    private string SettingsPath=>Path.Combine(DataDirectory,"config.json");
    private DispatcherTimer? backendStatusTimer;
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]
    private static extern bool SystemParametersInfo(uint action,uint param,StringBuilder value,uint flags);

    private async Task LoadBackendAsync()
    {
        if(verification || capture || backendLoaded)return;
        try
        {
            string source=SettingsPath;
            bool firstRun=!File.Exists(source);
            backend=firstRun?new BackendSettings():BackendSettings.Load(source);
            if(firstRun)
            {
                // Start from this person's desktop; never distribute the developer's settings.
                var current=new StringBuilder(32768);
                if(SystemParametersInfo(0x73,(uint)current.Capacity,current,0) && File.Exists(current.ToString()))
                {
                    backend.AwayWallpaper=current.ToString();backend.LockScreenWallpaper=current.ToString();
                    backend.SelectedHomeWallpaper=current.ToString();backend.MatchDefaultWallpaper=true;
                }
                await locationStore.SaveAsync(savedLocations);
            }
            document.DefaultImage=LoadConfiguredImage(backend.AwayWallpaper);
            document.IndependentLockImage=LoadConfiguredImage(string.IsNullOrWhiteSpace(backend.LockScreenWallpaper)?backend.AwayWallpaper:backend.LockScreenWallpaper);
            document.LocationImage=LoadConfiguredImage(backend.SelectedHomeWallpaper);
            document.CarouselImages.Clear();
            foreach(string path in backend.HomeWallpapers) document.AddImage(LoadConfiguredImage(path));
            document.MatchDefault=backend.MatchDefaultWallpaper; document.MatchHomeScreen=backend.MatchHomeScreen; document.CarouselEnabled=backend.CarouselEnabled;
            document.RotationMinutes=Math.Clamp(backend.CarouselIntervalMinutes,1,30);
            if(!firstRun && !File.Exists(locationStore.FilePath) && !locationsReadFailed)
            {
                var point=new MapPoint(backend.HomeLatitude,backend.HomeLongitude);
                if(point.IsValid) { savedLocations.Add(new SavedLocation(Guid.NewGuid().ToString("N"),"Home",point.Latitude,point.Longitude)); await locationStore.SaveAsync(savedLocations); }
                PostLocations(true,false);
            }
            backendLoaded=true; applyButton.IsEnabled=true;
            RefreshCards(); RefreshCarousel(); RefreshLayout(); RefreshRotation(); RefreshBackendStatus();
            backendStatusTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
            backendStatusTimer.Tick+=(_,_)=>RefreshBackendStatus(); backendStatusTimer.Start();
            Closed+=(_,_)=>backendStatusTimer.Stop();
        }
        catch(Exception ex) { applicationStatus.Text="Settings could not be loaded"; await ShowErrorAsync("Unable to load wallpaper settings",ex.Message); }
    }
    private WallpaperImage LoadConfiguredImage(string path)
    {
        if(string.IsNullOrWhiteSpace(path))return new("missing","No image selected","","ms-appx:///Assets/Placeholder.png","—","—");
        var file=new FileInfo(path); string id=path.ToUpperInvariant();
        // A missing original remains visible as a missing file, never silently saved as demo artwork.
        string preview=file.Exists && WallpaperMedia.IsVideo(path)?WallpaperMedia.PosterPath(path):file.FullName;
        return new(id,file.Name,path,File.Exists(preview)?new Uri(preview).AbsoluteUri:"ms-appx:///Assets/Placeholder.png",
            file.Exists?$"{file.Length/1048576.0:0.##}MB":"Missing file",file.Exists?file.LastWriteTime.ToString("dd-MM-yyyy"):"—");
    }
    private void RefreshRotation()
    {
        if(rotationLabel!=null)rotationLabel.Text=$"Rotation time: {document.RotationMinutes} min";
    }
    private void ShowRotationOptions()
    {
        var presenter=new Style(typeof(MenuFlyoutPresenter));
        presenter.Setters.Add(new Setter(Control.BackgroundProperty,Brush(Paper)));
        presenter.Setters.Add(new Setter(Control.BorderBrushProperty,Brush("#DFDFF1")));
        presenter.Setters.Add(new Setter(Control.BorderThicknessProperty,new Thickness(1)));
        presenter.Setters.Add(new Setter(Control.CornerRadiusProperty,new CornerRadius(14)));
        presenter.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(6)));
        presenter.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,186d));
        presenter.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty,320d));
        var menu=new MenuFlyout { MenuFlyoutPresenterStyle=presenter,Placement=Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight };
        foreach(int minutes in new[]{1,5,10,15,30})
        {
            var item=new ToggleMenuFlyoutItem { Text=$"{minutes} min",IsChecked=minutes==document.RotationMinutes,
                FontFamily=inter,FontSize=14,Foreground=Brush(Navy),CornerRadius=new CornerRadius(8),
                Background=Brush(minutes==document.RotationMinutes?"#DFDFF1":Paper) };
            item.Resources["MenuFlyoutItemBackgroundPointerOver"]=Brush("#EBEBF9");
            item.Resources["MenuFlyoutItemBackgroundPressed"]=Brush("#DFDFF1");
            item.Click+=(_,_)=> { document.RotationMinutes=minutes; RefreshRotation();menu.Hide(); };
            menu.Items.Add(item);
        }
        menu.ShowAt(rotationButton);
    }
    private BackendSettings PrepareSettings()
    {
        string Require(WallpaperImage image) { if(!File.Exists(image.DisplayPath))throw new FileNotFoundException("Choose an existing image for: "+image.Name); return image.DisplayPath; }
        var next=System.Text.Json.JsonSerializer.Deserialize<BackendSettings>(System.Text.Json.JsonSerializer.Serialize(backend))!;
        next.AwayWallpaper=Require(document.DefaultImage);
        next.LockScreenWallpaper=document.MatchDefault?document.IndependentLockImage.DisplayPath:Require(document.IndependentLockImage);
        next.MatchDefaultWallpaper=document.MatchDefault;
        next.MatchHomeScreen=document.MatchHomeScreen;next.RadiusMeters=150;
        next.SelectedHomeWallpaper=Require(document.LocationImage);
        next.HomeWallpapers=document.CarouselImages.Select(Require).ToList();
        if(document.CarouselEnabled && next.HomeWallpapers.Count==0)throw new InvalidOperationException("Add at least one image to the carousel.");
        if(next.HomeWallpapers.Any(WallpaperMedia.IsVideo))throw new InvalidOperationException("The carousel supports still images only. Remove videos from the list.");
        next.CarouselEnabled=document.CarouselEnabled;next.CarouselIntervalMinutes=document.RotationMinutes;
        if(!verification && !capture && File.Exists(SettingsPath))next.HideLocationWallpaper=BackendSettings.Load(SettingsPath).HideLocationWallpaper;
        return next;
    }
    private async Task ApplyBackendAsync()
    {
        if(applying || !backendLoaded || verification || capture)return;
        applying=true;applyButton.IsEnabled=false;
        try
        {
            var next=PrepareSettings();
            string engine=Path.Combine(AppContext.BaseDirectory,"WallpaperService.exe");
            if(!File.Exists(engine))throw new FileNotFoundException("The wallpaper backend is missing. Reinstall the complete App folder.");
            if(File.Exists(SettingsPath))File.Copy(SettingsPath,SettingsPath+".bak",true);
            await next.SaveAsync(SettingsPath);backend=next;
            // Replace only this product's legacy engine; unrelated processes are never stopped.
            foreach(var process in Process.GetProcessesByName("WallpaperService"))
            using(process)
            {
                string? path=process.MainModule?.FileName;
                if(path==null)continue;
                bool own=string.Equals(path,engine,StringComparison.OrdinalIgnoreCase);
                bool legacy=string.Equals(Path.GetDirectoryName(path),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Documents","WallpaperTask"),StringComparison.OrdinalIgnoreCase);
                if(own || legacy) { process.Kill(); await process.WaitForExitAsync(); }
                else throw new InvalidOperationException("Another wallpaper backend is running from "+path+". Close that backend before applying.");
            }
            if(!WallpaperStudioShared.StudioPaths.IsPortable)
                using(var run=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    run.SetValue("WallpaperService","\""+engine+"\"");
            using var started=Process.Start(new ProcessStartInfo(engine) { WorkingDirectory=AppContext.BaseDirectory,UseShellExecute=false,CreateNoWindow=true });
            await Task.Delay(800);
            if(started==null || started.HasExited)throw new InvalidOperationException("The wallpaper backend did not stay running. Check for another active instance.");
            string startupMessage=WallpaperStudioShared.StudioPaths.IsPortable?"":await UpdateLegacyStartupTaskAsync(engine);
            await ShowApplyConfirmationAsync(startupMessage);
        }
        catch(Exception ex) { await ShowErrorAsync("Unable to finish applying settings",ex.Message); }
        finally { applying=false;if(!closed){applyButton.IsEnabled=true;RefreshBackendStatus();} }
    }
    private string actualWallpaper="";
    private static async Task<string> UpdateLegacyStartupTaskAsync(string engine)
    {
        async Task<int> Run(string executable,params string[] args)
        {
            var info=new ProcessStartInfo(executable) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,RedirectStandardInput=true };
            foreach(string arg in args)info.ArgumentList.Add(arg);
            using var process=Process.Start(info)!;
            process.StandardInput.Close();
            var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch(TimeoutException) { process.Kill(true);return -1; }
            await output;await error;return process.ExitCode;
        }
        string system=Environment.GetFolderPath(Environment.SpecialFolder.System);
        if(await Run(Path.Combine(system,"schtasks.exe"),"/Query","/TN","WallpaperService")!=0)return "";
        string script="$ErrorActionPreference='Stop'; Set-ScheduledTask -TaskName 'WallpaperService' -Action (New-ScheduledTaskAction -Execute '"+engine.Replace("'","''")+"') | Out-Null";
        string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return await Run(Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe"),"-NoProfile","-NonInteractive","-EncodedCommand",encoded)==0?"":
            "\nThe old WallpaperService scheduled task could not be updated. Update its program path to this App folder to avoid starting the old version at sign-in.";
    }
    private void RefreshBackendStatus()
    {
        string previous=actualWallpaper;
        string actualLock="",outputError="";
        var value=new StringBuilder(32768);
        if(SystemParametersInfo(0x73,(uint)value.Capacity,value,0))actualWallpaper=value.ToString();
        try
        {
            using var status=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory,"wallpaper-status.json")));
            actualLock=status.RootElement.GetProperty("lockScreen").GetString()??"";
            outputError=status.RootElement.GetProperty("error").GetString()??"";
            if(status.RootElement.GetProperty("videoPlaying").GetBoolean())actualWallpaper=status.RootElement.GetProperty("desktop").GetString()??actualWallpaper;
        } catch { }
        if(previous!=actualWallpaper)RefreshCarousel();
        bool own=false,legacy=false;
        foreach(var process in Process.GetProcessesByName("WallpaperService"))using(process)
        { try { own|=string.Equals(process.MainModule?.FileName,Path.Combine(AppContext.BaseDirectory,"WallpaperService.exe"),StringComparison.OrdinalIgnoreCase);legacy=true; } catch { } }
        applicationStatus.Text=own?"Wallpaper service running":legacy?"Existing service running · Click Apply to connect":"Click Apply to start wallpaper service";
        if(own)
        {
            bool hidden=false;try { hidden=BackendSettings.Load(SettingsPath).HideLocationWallpaper; } catch { }
            applicationStatus.Text=hidden?"Normal wallpaper mode · Ctrl+Alt+P to resume":"Wallpaper service running · Ctrl+Alt+P to hide location wallpaper";
            if(!hidden)
            {
                try {
                    using var fix=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDirectory,"current-location.json")));
                    var root=fix.RootElement;
                    if(root.TryGetProperty("accuracy",out var accuracy)&&accuracy.GetDouble()>150)
                        applicationStatus.Text=$"Default wallpaper · Windows location is too approximate (±{accuracy.GetDouble():0} m)";
                    else if(!root.GetProperty("available").GetBoolean()||DateTimeOffset.UtcNow.ToUnixTimeSeconds()-root.GetProperty("timestamp").GetDouble()>30)
                        applicationStatus.Text="Default wallpaper · Waiting for a fresh Windows location";
                } catch { }
            }
        }
        foreach(var (role,view) in cards)
        {
            bool active=string.Equals(document.ForRole(role).DisplayPath,role==WallpaperRole.LockScreen?actualLock:actualWallpaper,StringComparison.OrdinalIgnoreCase);
            view.Status.Text=active?"Active":"Not Active";
            view.Status.Foreground=Brush(active?Active:Inactive);
        }
        if(outputError.Length>0)applicationStatus.Text=outputError;
        else if(DateTime.UtcNow<appliedNoticeUntil)applicationStatus.Text="Settings applied · "+applicationStatus.Text;
        ToolTipService.SetToolTip(applicationStatus,applicationStatus.Text);
    }
}
