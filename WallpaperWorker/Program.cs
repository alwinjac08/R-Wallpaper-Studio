using WallpaperStudio;
using System.Runtime.InteropServices;

ShutdownOrder.SetProcessShutdownParameters(0x200,0);

if(args.Length==2 && args[0]=="--verify-video") { await WorkerVerification.RunAsync(args[1]);return; }
if(args.Length!=2 || args[0]!="--location-provider" || !int.TryParse(args[1],out int parentId))return;
try
{
    await Task.WhenAll(LocationProvider.RunAsync(parentId),NetworkMonitor.RunAsync(parentId),WallpaperOutput.RunAsync(parentId));
}
catch(Exception ex)
{
    try { File.WriteAllText(Path.Combine(WallpaperStudioShared.StudioPaths.DataDirectory,"worker-error.txt"),ex.ToString()); } catch { }
}

internal static class ShutdownOrder
{
    [DllImport("kernel32.dll")] internal static extern bool SetProcessShutdownParameters(uint level,uint flags);
}
