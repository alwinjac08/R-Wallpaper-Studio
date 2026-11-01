using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace WallpaperStudio;

public sealed partial class MainWindow
{
    private bool verificationStarted;
    private async Task VerifyAndCaptureAsync()
    {
        if(verificationStarted) return; verificationStarted=true;
        string output=Path.Combine(AppContext.BaseDirectory,"Previews"); Directory.CreateDirectory(output);
        var checks=new List<string>();
        void Check(bool condition,string label) { if(!condition) throw new InvalidOperationException(label); checks.Add("PASS: "+label); }
        try
        {
            // WebView2 initializes its compositor only when its viewport is visible.
            AppWindow.Show();
            wallpaperScroll.UpdateLayout();
            wallpaperScroll.ChangeView(null,wallpaperScroll.ScrollableHeight,null,true);
            await mapReadySignal.Task.WaitAsync(TimeSpan.FromSeconds(35));
            await Task.Delay(400);
            document.CarouselEnabled=false; RefreshLayout(); wallpaperScroll.UpdateLayout();
            wallpaperScroll.ChangeView(null,0,null,true); await Task.Delay(150);
            await CaptureArtboardAsync(Path.Combine(output,"Design-home.png"));
            double before=wallpaperScroll.ScrollableHeight;
            document.CarouselEnabled=true; RefreshLayout(); wallpaperScroll.UpdateLayout(); await Task.Delay(150);
            Check(wallpaperScroll.VerticalOffset==0 && wallpaperScroll.ScrollableHeight>before,"Expanding carousel preserves position and pushes Saved Locations below it");
            Check(!cards[WallpaperRole.Location].Browse.IsEnabled && cards[WallpaperRole.Default].Browse.IsEnabled,"Carousel disables only the location card Browse button");
            wallpaperScroll.ChangeView(null,450,null,true); await Task.Delay(150);
            double offset=wallpaperScroll.VerticalOffset;
            string[] originals=document.CarouselImages.Select(i=>i.Source).ToArray();
            SelectCarouselImage("home");
            Check(document.SelectedImage?.Id=="home" && carouselName.Text.Contains("Sample-alternate"),"First row selects its own image");
            var activeBeforeDrag=document.LocationImage;
            Check(carouselRowViews.Values.All(view=>view.Grip.Opacity==0),"Drag handles are hidden until row hover or keyboard focus");
            BeginCarouselDrag("home",43.5);dragViewportY=120;UpdateCarouselDrag();
            Check(Math.Abs(Canvas.GetTop(carouselRowViews["home"].Row)-76.5)<0.1 && document.CarouselImages[1].Id=="home","Dragged row follows the pointer continuously while its neighbour changes position");
            RefreshCarousel();
            Check(draggedImageId=="home"&&carouselRowViews["home"].Grip.Opacity==1,"Background refresh preserves the captured row during dragging");
            FinishCarouselDrag(true);
            Check(document.CarouselImages[0].Id=="home"&&document.SelectedImageId=="home"&&document.LocationImage==activeBeforeDrag,"Cancel restores the original order without changing selection or active wallpaper");
            BeginCarouselDrag("home",43.5);dragViewportY=140.5;UpdateCarouselDrag();FinishCarouselDrag(false);
            Check(document.CarouselImages[1].Id=="home"&&Canvas.GetTop(carouselRowViews["home"].Row)==97,"Dropping commits the new row position");
            document.MoveCarouselImage("home",0);RefreshCarousel();
            var extra=document.CarouselImages[0] with { Id="drag-scroll-test" };document.AddImage(extra);RefreshCarousel();carouselScroll.UpdateLayout();
            BeginCarouselDrag("home",43.5);dragViewportY=200;UpdateCarouselDrag();
            dragScrollTimer??=CreateDragScrollTimer();dragScrollTimer.Start();await Task.Delay(220);
            Check(carouselScroll.VerticalOffset>0,"Dragging near the list edge automatically scrolls longer lists");
            FinishCarouselDrag(true);document.CarouselImages.Remove(extra);RefreshCarousel();carouselScroll.ChangeView(null,0,null,true);await Task.Delay(100);
            Check(!IsCarouselBlankClick(carouselRowViews["home"].Grip,new Windows.Foundation.Point(617,121)),"Dragging the handle never clears the selected row");
            double savedPageOffset=wallpaperScroll.VerticalOffset;
            wallpaperScroll.ChangeView(null,857,null,true);await Task.Delay(150);
            carouselRowViews["home"].Grip.Opacity=1;
            await CaptureArtboardAsync(Path.Combine(output,"Carousel-drag-handle.png"));
            carouselRowViews["home"].Grip.Opacity=0;
            wallpaperScroll.ChangeView(null,savedPageOffset,null,true);await Task.Delay(150);
            bool ColorIs(Button button,string color)=>((Microsoft.UI.Xaml.Media.SolidColorBrush)((TextBlock)button.Content).Foreground).Color==Brush(color).Color;
            Check(ColorIs(removeImage,Inactive)&&ColorIs(setActive,Active)&&ColorIs(addImage,Active)&&ColorIs(selectFolder,Active),"Selected action colors match A6083F / 082FA6 exactly");
            await SetSelectedCarouselActiveAsync();
            Check(document.LocationImage.Id=="home"&&setActive.IsEnabled,"Set Active uses the selected image and remains available for reselection");
            Check(IsCarouselBlankClick(carouselRows,new Windows.Foundation.Point(30,280))&&!IsCarouselBlankClick(setActive,new Windows.Foundation.Point(610,309))&&!IsCarouselBlankClick(carouselRows,new Windows.Foundation.Point(700,280)),"Only blank space inside the list box deselects; action buttons and outside clicks do not");
            document.ClearSelection();RefreshCarousel();
            Check(document.SelectedImage==null&&!removeImage.IsEnabled&&!setActive.IsEnabled&&ColorIs(removeImage,"#49454F")&&ColorIs(setActive,"#49454F")&&removeImage.Opacity==1&&setActive.Opacity==1,"Deselection disables actions with exact 49454F text at full opacity");
            Check(ColorIs(addImage,Active)&&ColorIs(selectFolder,Active),"Add Image and Select Folder stay 082FA6 without a selection");
            SelectCarouselImage("cafe");
            await SetSelectedCarouselActiveAsync();
            Check(document.SelectedImage?.Id=="cafe" && carouselName.Text.Contains("Sample-location"),"Second row selects its own image");
            Check(originals.SequenceEqual(document.CarouselImages.Select(i=>i.Source)),"Selecting rows never swaps their source images");
            await Task.Delay(100);
            Check(Math.Abs(offset-wallpaperScroll.VerticalOffset)<0.5,"Selection preserves document scroll position");

            var independent=document.IndependentLockImage;
            document.MatchDefault=true; RefreshCards();
            Check(document.EffectiveLockImage==document.DefaultImage && !cards[WallpaperRole.LockScreen].Browse.IsEnabled,"Match Default mirrors the default and disables the independent picker");
            var imported=await ReadImageAsync(await StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory,"Assets","PlaceholderAlternate.png")));
            document.ChooseImage(WallpaperRole.Default,imported); RefreshCards();
            Check(document.EffectiveLockImage==imported && cards[WallpaperRole.Default].Path.Text==imported.DisplayPath,"Image import decodes a real file, updates metadata, and updates a matched lock preview");
            document.MatchDefault=false; RefreshCards();
            Check(document.EffectiveLockImage==independent && cards[WallpaperRole.LockScreen].Browse.IsEnabled,"Unmatching restores the independent lock image");
            document.MatchHomeScreen=true;RefreshCards();
            Check(matchHomeCheckbox.IsChecked==true && !document.MatchDefault,"Match Home Screen is separate from Match Default Wallpaper");

            PostMap(new { type="current",latitude=20.0,longitude=30.0,center=true });
            await WaitMapAsync(s=>s.GetProperty("current").ValueKind==JsonValueKind.Object);
            var map=await InspectMapAsync();
            Check(map.GetProperty("currentColor").GetString()==Active,"Current-location pin uses the Active text blue");
            await WaitMapAsync(s=>s.GetProperty("canAdd").GetBoolean());
            Check(pendingLocation==new MapPoint(20,30),"Unsaved current location automatically offers Add for its own coordinates");
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.save()");
            await WaitMapAsync(s=>s.GetProperty("saved").GetArrayLength()==1 && !s.GetProperty("actionVisible").GetBoolean());
            map=await InspectMapAsync();
            Check(map.GetProperty("currentColor").GetString()==Inactive && map.GetProperty("bluePinCount").GetInt32()==0,"Saving current location shows one red pin with no blue overlay and hides Add");
            PostMap(new { type="current",latitude=20.0,longitude=30.0,center=true });
            await Task.Delay(150);map=await InspectMapAsync();
            Check(!map.GetProperty("actionVisible").GetBoolean(),"A repeated current-location update keeps a saved location red and Add hidden");
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.select()");
            await WaitMapAsync(s=>s.GetProperty("canRemove").GetBoolean());
            Check(selectedLocationId==savedLocations[0].Id,"The saved current-location red pin still offers Remove when clicked");
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.save()");
            await WaitMapAsync(s=>s.GetProperty("saved").GetArrayLength()==0 && s.GetProperty("canAdd").GetBoolean());
            map=await InspectMapAsync();
            Check(map.GetProperty("currentColor").GetString()==Active,"Removing the saved current location restores blue and offers Add again");
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.click(20.001,30.002)");
            await WaitMapAsync(s=>s.GetProperty("canAdd").GetBoolean());
            Check(pendingLocation is { IsValid:true },"Map click reaches native code and enables Add");
            var manualSelection=pendingLocation;
            PostMap(new { type="current",latitude=20.00001,longitude=30.0,center=false });
            await Task.Delay(150);
            Check(pendingLocation==manualSelection,"Live location updates do not replace a manually selected place");
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.save()");
            await WaitMapAsync(s=>s.GetProperty("saved").GetArrayLength()==1);
            map=await InspectMapAsync();
            Check(map.GetProperty("savedColors")[0].GetString()==Inactive && map.GetProperty("currentColor").GetString()==Active,"Saving adds a red pin while retaining the blue current-location pin");
            Check(!map.GetProperty("canAdd").GetBoolean() && map.GetProperty("pending").ValueKind==JsonValueKind.Null,"Saving clears the draft pin and disables Add");
            Check(locationStore.Load().Count==1,"Saved coordinates survive a disk round-trip");
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.click(20.001,30.002)");
            await Task.Delay(150); map=await InspectMapAsync();
            Check(!map.GetProperty("canAdd").GetBoolean(),"Already-saved coordinates cannot be added twice");
            Check(map.GetProperty("pending").ValueKind==JsonValueKind.Null,"Selecting a saved place keeps its red pin visible");
            Check(map.GetProperty("canRemove").GetBoolean(),"Selecting saved coordinates offers Remove instead of Add");
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.select()");
            await WaitMapAsync(s=>s.GetProperty("canRemove").GetBoolean());
            Check(selectedLocationId==savedLocations[0].Id,"Clicking a red pin selects that saved location");
            var previous=pendingLocation; SetPendingLocation(new MapPoint(double.NaN,200));
            Check(pendingLocation==previous,"Invalid map coordinates are rejected");
            double oldLng=map.GetProperty("center").GetProperty("lng").GetDouble();
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.pan(70,0)");
            await Task.Delay(150); map=await InspectMapAsync();
            Check(map.GetProperty("dragging").GetBoolean() && map.GetProperty("center").GetProperty("lng").GetDouble()!=oldLng,"Map is pannable and mouse dragging is enabled");
            PostMap(new { type="locationUnavailable",message="Test: location unavailable" });
            await WaitMapAsync(s=>s.GetProperty("current").ValueKind==JsonValueKind.Null);
            map=await InspectMapAsync();
            Check(map.GetProperty("saved").GetArrayLength()==1,"Denied/unavailable geolocation removes the current pin without removing saved places");
            await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.save()");
            await WaitMapAsync(s=>s.GetProperty("saved").GetArrayLength()==0);
            Check(locationStore.Load().Count==0,"Remove persists deletion of the last saved place");
            map=await InspectMapAsync();
            Check(!map.GetProperty("canRemove").GetBoolean() && !map.GetProperty("canAdd").GetBoolean(),"Removal restores disabled Add until a new point is chosen");
            document.DefaultImage=imported;document.IndependentLockImage=imported;document.LocationImage=imported;
            document.CarouselImages.Clear();document.AddImage(imported);document.RotationMinutes=5;
            var settings=PrepareSettings();
            string testSettings=Path.Combine(DataDirectory,"backend-test.json");await settings.SaveAsync(testSettings);
            var loaded=BackendSettings.Load(testSettings);
            Check(loaded.MatchHomeScreen && loaded.RadiusMeters==150,"Apply persists Match Home Screen and the 150 metre location radius");
            Check(loaded.AwayWallpaper==imported.DisplayPath && loaded.SelectedHomeWallpaper==imported.DisplayPath && loaded.CarouselIntervalMinutes==5 && loaded.HomeWallpapers.Count==1,"Apply prepares and persists backend image paths, carousel and rotation settings");
            var secondImported=await ReadImageAsync(await StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory,"Assets","Placeholder.png")));
            document.AddImage(secondImported);document.MoveCarouselImage(secondImported.Id,0);
            await PrepareSettings().SaveAsync(testSettings);
            Check(BackendSettings.Load(testSettings).HomeWallpapers.SequenceEqual(new[]{secondImported.DisplayPath,imported.DisplayPath}),"Apply saves reordered carousel paths in their new order");
            document.CarouselImages.Remove(secondImported);RefreshCarousel();
            document.MatchDefault=true;document.IndependentLockImage=independent;
            Check(PrepareSettings().LockScreenWallpaper==independent.DisplayPath,"Matching the default preserves the independent lock-screen choice");
            var videoPicker=ImagePicker(true);var stillPicker=ImagePicker();
            Check(videoPicker.FileTypeFilter.Contains(".mp4")&&!stillPicker.FileTypeFilter.Contains(".mp4"),"Only the single-wallpaper picker offers videos; carousel remains image-only");
            document.CarouselEnabled=true;
            Check(WallpaperPicker(WallpaperRole.Default).FileTypeFilter.Contains(".mp4")&&WallpaperPicker(WallpaperRole.LockScreen).FileTypeFilter.Contains(".mp4")&&!WallpaperPicker(WallpaperRole.Location).FileTypeFilter.Contains(".mp4"),"Default and lock screen accept video independently of the location carousel");

            document.CarouselEnabled=false; RefreshLayout(); await Task.Delay(150);
            Check(wallpaperScroll.ScrollableHeight<before+1,"Collapsing carousel restores the shorter document");
            Check(cards[WallpaperRole.Location].Browse.IsEnabled,"Disabling carousel restores location Browse");
            await CaptureArtboardAsync(Path.Combine(output,"PlaceholderAlternate.png"));
            document.CarouselEnabled=true; document.SelectImage("home"); RefreshLayout(); RefreshCarousel();
            wallpaperScroll.UpdateLayout(); wallpaperScroll.ChangeView(null,wallpaperScroll.ScrollableHeight,null,true);
            await Task.Delay(400);
            await CaptureArtboardAsync(Path.Combine(output,"Saved-locations.png"));
            var file=await CreateOutputAsync(Path.Combine(output,"Map-check.png"));
            using(var stream=await file.OpenAsync(FileAccessMode.ReadWrite))
                await mapView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
            await new UiPreferences().SaveAsync(UiPreferencesPath);
            var confirmation=ShowApplyConfirmationAsync("");await Task.Delay(250);
            Check(applyConfirmationDialog!=null&&suppressConfirmationCheckbox?.IsChecked==false,"Success dialog offers an unchecked Don't show again option");
            await CaptureArtboardAsync(Path.Combine(output,"Apply-confirmation.png"),applyConfirmationDialog);
            applyConfirmationDialog!.Hide();await confirmation;
            Check(!closed&&ShouldShowApplyConfirmation(""),"Dismissing confirmation keeps Settings open and retains future confirmations");
            confirmation=ShowApplyConfirmationAsync("");await Task.Delay(150);
            SetCheckVisual(suppressConfirmationCheckbox!,true);applyConfirmationDialog!.Hide();await confirmation;
            Check(!closed&&UiPreferences.Load(UiPreferencesPath).SuppressApplyConfirmation,"Don't show again persists after dismissal without closing Settings");
            await ShowApplyConfirmationAsync("");
            Check(applyConfirmationDialog==null&&!closed,"Subsequent successful applies skip the dialog and keep Settings open");
            Check(ShouldShowApplyConfirmation("Startup warning"),"Don't show again never suppresses apply warnings");
            MapFailed("Simulated browser failure");
            Check(!mapReady&&mapFallback.Visibility==Visibility.Visible,"Map failures expose recovery state instead of a permanently blank map");
            RecreateMapBrowser();await InitializeMapAsync();
            for(int i=0;i<100&&!mapReady;i++)await Task.Delay(100);
            Check(mapReady&&mapFallback.Visibility==Visibility.Collapsed,"Map recreates its browser and reconnects after a failure");
            await WaitMapAsync(s=>s.GetProperty("saved").GetArrayLength()==savedLocations.Count);
            await File.WriteAllLinesAsync(Path.Combine(output,"Interaction-checks.txt"),checks);
        }
        catch(Exception ex)
        {
            string diagnostics="";
            try { if(mapView.CoreWebView2!=null) diagnostics=await mapView.CoreWebView2.ExecuteScriptAsync("JSON.stringify({url:location.href,ready:document.readyState,body:document.body?.innerText,leaflet:typeof L,bridge:!!window.chrome?.webview})"); } catch { }
            await File.WriteAllTextAsync(Path.Combine(output,"Interaction-checks.txt"),string.Join(Environment.NewLine,checks)+Environment.NewLine+"FAIL: "+ex+Environment.NewLine+diagnostics);
        }
        finally { Application.Current.Exit(); }
    }
    private async Task<JsonElement> InspectMapAsync()
    {
        using var json=JsonDocument.Parse(await mapView.CoreWebView2.ExecuteScriptAsync("window.studioMapTest.inspect()"));
        return json.RootElement.Clone();
    }
    private async Task WaitMapAsync(Func<JsonElement,bool> predicate)
    {
        for(int i=0;i<60;i++) { if(predicate(await InspectMapAsync())) return; await Task.Delay(100); }
        throw new TimeoutException("Map state did not update.");
    }
    private static async Task<StorageFile> CreateOutputAsync(string path)
    { await File.WriteAllBytesAsync(path,[]); return await StorageFile.GetFileFromPathAsync(path); }
    private async Task CaptureArtboardAsync(string path,UIElement? element=null)
    {
        var bitmap=new RenderTargetBitmap();
        if(element==null)await bitmap.RenderAsync(artboard,1014,1110);else await bitmap.RenderAsync(element);
        var pixels=await bitmap.GetPixelsAsync(); var file=await CreateOutputAsync(path);
        using var stream=await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());
        await encoder.FlushAsync();
    }
}
