using System.Diagnostics;
using System.Text.Json;

namespace WallpaperStudio;

internal static class VideoHost
{
    public static async Task RunAsync(int parentId,string path,string statusPath)
    {
        using var parent=Process.GetProcessById(parentId);
        if(!string.Equals(parent.MainModule?.FileName,Path.Combine(AppContext.BaseDirectory,"WallpaperWorker.exe"),StringComparison.OrdinalIgnoreCase))return;
        VideoWallpaperWindow? video=null;
        try
        {
            video=new VideoWallpaperWindow(path);
            while(!parent.HasExited)
            {
                string text=JsonSerializer.Serialize(new { playing=video.IsPlaying,ready=video.HasRenderSurface,position=video.PlaybackPosition.TotalMilliseconds,error=video.Failure });
                await File.WriteAllTextAsync(statusPath+".tmp",text);File.Move(statusPath+".tmp",statusPath,true);
                if(video.Failure!=null)break;
                await Task.Delay(500);
            }
        }
        catch(Exception ex)
        {
            await File.WriteAllTextAsync(statusPath,JsonSerializer.Serialize(new { playing=false,ready=false,error=ex.Message }));
        }
        finally { video?.Close(); }
    }
}
