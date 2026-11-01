using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Storage;
#if HEADLESS_OUTPUT
using VideoWallpaperWindow = WallpaperStudio.RemoteVideoWindow;
#endif

namespace WallpaperStudio;

// The tray process owns the policy; this worker owns Windows Runtime media APIs.
internal static class WallpaperOutput
{
    internal sealed record Request(string desktop,string lockScreen,bool paused,bool allowVideo,int parentId,bool sessionEnding=false);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    private static extern bool SystemParametersInfo(uint action,uint parameter,string value,uint flags);
    public static async Task RunAsync(int parentId)
    {
        using var parent=Process.GetProcessById(parentId);
        if(!string.Equals(parent.MainModule?.FileName,Path.Combine(AppContext.BaseDirectory,"WallpaperService.exe"),StringComparison.OrdinalIgnoreCase))return;
        string directory=WallpaperStudioShared.StudioPaths.DataDirectory;
        string desktop="",lockScreen="",lastStatus="";
        VideoWallpaperWindow? video=null;
        DateTime retryAfter=DateTime.MinValue,lockRetryAfter=DateTime.MinValue;
        string lastLockRequest="",videoError="",lockError="";
        string? requestText=null;
        Request? cachedRequest=null;
        try
        {
            while(!parent.HasExited)
            {
                try
                {
                    string text=await File.ReadAllTextAsync(Path.Combine(directory,"wallpaper-output.json"));
                    if(text!=requestText) { cachedRequest=JsonSerializer.Deserialize<Request>(text);requestText=text; }
                    var request=cachedRequest;
                    if(request==null || request.parentId!=parentId) { await Task.Delay(500);continue; }
                    if(desktop!=request.desktop)
                    {
                        string still=await WallpaperMedia.EnsurePosterAsync(request.desktop);
                        video?.Close();video=null;
                        if(!SystemParametersInfo(20,0,still,3))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                        desktop=request.desktop;retryAfter=DateTime.MinValue;videoError="";
                    }
                    bool play=WallpaperMedia.IsVideo(desktop)&&request.allowVideo&&!request.paused;
                    if(!play) { video?.Close();video=null;videoError=""; }
                    else if(video==null && DateTime.UtcNow>=retryAfter)
                    {
                        try { video=new VideoWallpaperWindow(desktop);videoError=""; }
                        catch(Exception ex) { videoError="Video wallpaper: "+ex.Message;retryAfter=DateTime.UtcNow.AddSeconds(30); }
                    }
                    if(video?.Failure is { } failure) { videoError="Video wallpaper: "+failure;video.Close();video=null;retryAfter=DateTime.UtcNow.AddSeconds(30); }
                    if(lastLockRequest!=request.lockScreen) { lastLockRequest=request.lockScreen;lockRetryAfter=DateTime.MinValue; }
                    if(lockScreen!=request.lockScreen && (request.sessionEnding || DateTime.UtcNow>=lockRetryAfter))
                    {
                        try
                        {
                            string lockStill=await WallpaperMedia.EnsurePosterAsync(request.lockScreen);
                            await SetLockImageAsync(lockStill);
                            lockScreen=request.lockScreen;lockError="";
                        }
                        catch(Exception ex) { lockError="Lock screen: "+ex.Message;lockRetryAfter=DateTime.UtcNow.AddSeconds(30); }
                    }
                    string status=JsonSerializer.Serialize(new { desktop,lockScreen,parentId,resetAcknowledged=request.sessionEnding&&desktop==request.desktop&&lockScreen==request.lockScreen,
                        videoPlaying=video?.IsPlaying==true,videoSurfaceReady=video?.HasRenderSurface==true,paused=request.paused,error=string.Join(" · ",new[]{videoError,lockError}.Where(e=>e.Length>0)) });
                    if(status!=lastStatus)
                    {
                        string temporary=Path.Combine(directory,"wallpaper-status.json.tmp");await File.WriteAllTextAsync(temporary,status);
                        File.Move(temporary,Path.Combine(directory,"wallpaper-status.json"),true);lastStatus=status;
                    }
                }
                catch(Exception ex)
                {
                    video?.Close();video=null;
                    // Keep failures inspectable, without a popup from a background process.
                    try
                    {
                        await File.WriteAllTextAsync(Path.Combine(directory,"wallpaper-output-error.txt"),ex.ToString());
                        string temporary=Path.Combine(directory,"wallpaper-status.json.tmp");
                        await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new { desktop,lockScreen,videoPlaying=false,paused=false,error=ex.Message }));
                        File.Move(temporary,Path.Combine(directory,"wallpaper-status.json"),true);lastStatus="";
                    } catch { }
                }
                await Task.Delay(500);
            }
        }
        finally { video?.Close(); }
    }

    private static async Task SetLockImageAsync(string path)
    {
        // Windows can reject repeat filenames. Give each update a new local identity.
        string cache=Path.Combine(WallpaperStudioShared.StudioPaths.DataDirectory,"LockImages");
        Directory.CreateDirectory(cache);
        string staged=Path.Combine(cache,Guid.NewGuid().ToString("N")+Path.GetExtension(path));
        File.Copy(path,staged);
        var file=await StorageFile.GetFileFromPathAsync(staged);
        bool accepted=false;
        try
        {
            if(Windows.System.UserProfile.UserProfilePersonalizationSettings.IsSupported())
                accepted=await Windows.System.UserProfile.UserProfilePersonalizationSettings.Current.TrySetLockScreenImageAsync(file);
        }
        catch(System.Runtime.InteropServices.COMException) { }
        if(!accepted)
        {
            // The stream API also supports the unpackaged desktop app.
            using var input=await file.OpenReadAsync();
            await Windows.System.UserProfile.LockScreen.SetImageStreamAsync(input);
        }
        // Read back Windows' stored image instead of assuming the setter took effect.
        using(var actual=Windows.System.UserProfile.LockScreen.GetImageStream())
        using(var actualStream=actual.AsStreamForRead())
        using(var expected=File.OpenRead(staged))
        {
            var a=await System.Security.Cryptography.SHA256.HashDataAsync(actualStream);
            var b=await System.Security.Cryptography.SHA256.HashDataAsync(expected);
            if(!a.SequenceEqual(b))
            {
                // Windows may re-encode or resize the image; compare normalized pixels too.
                actual.Seek(0);
                using var source=await file.OpenReadAsync();
                async Task<byte[]> Pixels(Windows.Storage.Streams.IRandomAccessStream stream)
                {
                    var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                    var pixels=await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                        Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,new Windows.Graphics.Imaging.BitmapTransform { ScaledWidth=64,ScaledHeight=64 },
                        Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation,Windows.Graphics.Imaging.ColorManagementMode.ColorManageToSRgb);
                    return pixels.DetachPixelData();
                }
                byte[] observed=await Pixels(actual),wanted=await Pixels(source);
                double difference=observed.Length==wanted.Length?observed.Zip(wanted,(x,y)=>Math.Abs(x-y)).Average():255;
                if(difference>8)throw new InvalidOperationException("Windows retained a different lock screen image; the update will be retried (difference "+difference.ToString("0.0")+").");
            }
        }
        foreach(string old in Directory.EnumerateFiles(cache))
            if(old!=staged)try { File.Delete(old); } catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
}
