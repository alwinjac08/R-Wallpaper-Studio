using System.Runtime.InteropServices;

namespace WallpaperStudio;

public sealed partial class MainWindow
{
    [DllImport("user32.dll",EntryPoint="SendMessageW")]
    private static extern IntPtr GetWindowIcon(IntPtr hwnd,uint message,IntPtr type,IntPtr unused);

    private async Task VerifyFirstRunAsync()
    {
        var checks=new List<string>();
        void Check(bool condition,string label)=>checks.Add((condition?"PASS: ":"FAIL: ")+label);
        Check(backendLoaded,"First launch loads successfully without a bundled configuration");
        Check(!File.Exists(SettingsPath),"Opening settings does not apply or save wallpaper settings");
        Check(savedLocations.Count==0,"First launch has no developer locations or implicit (0,0) pin");
        Check(document.CarouselImages.Count==0 && !document.CarouselEnabled,"First launch has an empty, disabled carousel");
        Check(WallpaperStudioShared.StudioPaths.IsPortable && DataDirectory==Path.Combine(AppContext.BaseDirectory,"Data"),"Portable settings use the adjacent Data folder");
        var handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Check(GetWindowIcon(handle,0x7F,new IntPtr(1),IntPtr.Zero)!=IntPtr.Zero,"Settings window has an explicit taskbar icon");
        await File.WriteAllLinesAsync(Path.Combine(AppContext.BaseDirectory,"First-run-checks.txt"),checks);
        Close();
    }
}
