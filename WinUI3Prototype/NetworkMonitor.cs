using System.Diagnostics;
using System.Text.Json;
using Windows.Networking.Connectivity;

namespace WallpaperStudio;

internal static class NetworkMonitor
{
    public static async Task RunAsync(int parentId)
    {
        using var parent=Process.GetProcessById(parentId);
        if(!string.Equals(parent.MainModule?.FileName,Path.Combine(AppContext.BaseDirectory,"WallpaperService.exe"),StringComparison.OrdinalIgnoreCase))return;
        string directory=WallpaperStudioShared.StudioPaths.DataDirectory;
        Directory.CreateDirectory(directory);
        using var changed=new SemaphoreSlim(0,1);
        NetworkStatusChangedEventHandler handler=_=>{ try { changed.Release(); } catch(SemaphoreFullException) { } catch(ObjectDisposedException) { } };
        NetworkInformation.NetworkStatusChanged+=handler;
        string lastIdentity="";long changedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            while(!parent.HasExited)
            {
                bool online=false;
                string identity="offline";
                try {
                    var profile=NetworkInformation.GetInternetConnectionProfile();
                    online=profile?.GetNetworkConnectivityLevel()==NetworkConnectivityLevel.InternetAccess;
                    identity=online?string.Join("|",profile!.GetNetworkNames())+profile.NetworkAdapter?.NetworkAdapterId:"offline";
                } catch { }
                if(identity!=lastIdentity){changedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();lastIdentity=identity;}
                string path=Path.Combine(directory,"network-status.json"),temporary=path+"."+Environment.ProcessId+".tmp";
                try
                {
                    await File.WriteAllTextAsync(temporary,JsonSerializer.Serialize(new { online,changedAt,providerId=Environment.ProcessId,timestamp=DateTimeOffset.UtcNow.ToUnixTimeSeconds() }));
                    if(!parent.HasExited)File.Move(temporary,path,true);
                }
                finally { if(File.Exists(temporary))File.Delete(temporary); }
                await changed.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally { NetworkInformation.NetworkStatusChanged-=handler; }
    }
}
