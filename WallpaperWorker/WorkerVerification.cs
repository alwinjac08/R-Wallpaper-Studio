using System.Diagnostics;

namespace WallpaperStudio;

internal static class WorkerVerification
{
    public static async Task RunAsync(string path)
    {
        var checks=new List<string>();RemoteVideoWindow? video=null;
        void Check(bool value,string label){if(!value)throw new Exception(label);checks.Add("PASS: "+label);}
        async Task WaitForPlayback()
        {
            for(int i=0;i<40 && video?.IsPlaying!=true;i++)
            { if(video?.Failure is {} failure)throw new Exception(failure);await Task.Delay(250); }
            Check(video?.IsPlaying==true && video.HasRenderSurface,"On-demand player starts with a visible video surface");
        }
        try
        {
            video=new RemoteVideoWindow(path);await WaitForPlayback();
            double first=video.PlaybackPosition;await Task.Delay(1500);
            Check(video.PlaybackPosition!=first,"Video advances across the worker/player boundary");
            int child=video.ProcessId;video.Close();video=null;
            Check(!Process.GetProcessesByName("WallpaperStudio").Any(p=>p.Id==child),"Stopping video releases the entire WinUI player process");
            video=new RemoteVideoWindow(path);await WaitForPlayback();
            Check(video.ProcessId!=child,"Playback resumes in a fresh on-demand player");
            using var current=Process.GetCurrentProcess();
            Check(!current.Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Contains("Microsoft.UI.Xaml",StringComparison.OrdinalIgnoreCase)),"Background worker never loads WinUI");
        }
        catch(Exception ex){checks.Add("FAIL: "+ex);}
        finally
        {
            video?.Close();Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,"Previews"));
            await File.WriteAllLinesAsync(Path.Combine(AppContext.BaseDirectory,"Previews","Worker-checks.txt"),checks);
        }
    }
}
