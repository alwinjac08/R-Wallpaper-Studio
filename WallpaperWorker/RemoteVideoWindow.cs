using System.Diagnostics;
using System.Text.Json;

namespace WallpaperStudio;

// WinUI and the video renderer exist only while a video is actually playing.
internal sealed class RemoteVideoWindow
{
    private readonly Process process;
    private readonly string statusPath;
    private bool closed;
    private DateTime lastRead;
    private VideoStatus? status;
    private sealed record VideoStatus(bool playing,bool ready,double position,string? error);
    internal int ProcessId=>process.Id;
    internal double PlaybackPosition=>Read()?.position??0;
    public RemoteVideoWindow(string path)
    {
        statusPath=Path.Combine(WallpaperStudioShared.StudioPaths.DataDirectory,"video-"+Guid.NewGuid().ToString("N")+".json");
        var info=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"WallpaperStudio.exe"))
            { UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=AppContext.BaseDirectory };
        foreach(string arg in new[]{"--video-wallpaper",Environment.ProcessId.ToString(),path,statusPath})info.ArgumentList.Add(arg);
        process=Process.Start(info)??throw new InvalidOperationException("Unable to start video player.");
    }
    private VideoStatus? Read()
    {
        if(DateTime.UtcNow-lastRead<TimeSpan.FromMilliseconds(200))return status;
        lastRead=DateTime.UtcNow;
        try { status=JsonSerializer.Deserialize<VideoStatus>(File.ReadAllText(statusPath)); } catch { }
        return status;
    }
    public bool IsPlaying=>!closed && !process.HasExited && Read()?.playing==true;
    public bool HasRenderSurface=>!closed && !process.HasExited && Read()?.ready==true;
    public string? Failure=>closed?null:Read()?.error ?? (process.HasExited?"Video player stopped unexpectedly.":null);
    public void Close()
    {
        if(closed)return;closed=true;
        try { if(!process.HasExited){process.Kill();process.WaitForExit(3000);} } catch { }
        process.Dispose();
        try { File.Delete(statusPath);File.Delete(statusPath+".tmp"); } catch { }
    }
}
