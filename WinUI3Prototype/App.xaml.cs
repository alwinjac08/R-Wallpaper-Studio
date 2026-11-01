using Microsoft.UI.Xaml;

namespace WallpaperStudio;

public partial class App : Application
{
    private MainWindow? window;
    private Mutex? settingsMutex;
    private EventWaitHandle? openSettings;
    private RegisteredWaitHandle? activationWait;
    // The background worker also creates MediaPlayerElement for video wallpapers.
    // It needs the same WinUI theme resources as the settings process.
    public App() { InitializeComponent(); }
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments=Environment.GetCommandLineArgs();
        if(arguments.Contains("--video-wallpaper")||arguments.Contains("--verify-media"))
            DispatcherShutdownMode=DispatcherShutdownMode.OnExplicitShutdown;
        if(arguments.Contains("--verify-media")) { try { await MediaVerification.RunAsync(); } finally { Exit(); } return; }
        if(arguments.Length==5 && arguments[1]=="--video-wallpaper")
        {
            try { await VideoHost.RunAsync(int.Parse(arguments[2]),arguments[3],arguments[4]); }
            finally { Exit(); }
            return;
        }
        bool isolated=arguments.Contains("--verify")||arguments.Contains("--capture")||arguments.Contains("--verify-first-run");
        if(!isolated)
        {
            settingsMutex=new Mutex(true,WallpaperStudioShared.StudioPaths.SettingsMutex,out bool first);
            if(!first)
            {
                for(int i=0;i<30;i++)
                {
                    try { using var signal=EventWaitHandle.OpenExisting(WallpaperStudioShared.StudioPaths.OpenSettingsEvent);signal.Set();break; }
                    catch(WaitHandleCannotBeOpenedException) { await Task.Delay(100); }
                }
                Exit();return;
            }
            openSettings=new EventWaitHandle(false,EventResetMode.AutoReset,WallpaperStudioShared.StudioPaths.OpenSettingsEvent);
        }
        window = new MainWindow();
        if(openSettings!=null)activationWait=ThreadPool.RegisterWaitForSingleObject(openSettings,(_,_)=>window.DispatcherQueue.TryEnqueue(()=>
        {
            window.AppWindow.Show();
            var handle=WinRT.Interop.WindowNative.GetWindowHandle(window);
            ShowWindow(handle,9);window.Activate();SetForegroundWindow(handle);
        }),null,Timeout.Infinite,false);
        window.Closed+=(_,_)=>{ activationWait?.Unregister(null);openSettings?.Dispose();settingsMutex?.Dispose(); };
        window.Activate();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window,int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
