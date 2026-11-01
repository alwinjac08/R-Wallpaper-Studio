using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace WallpaperStudio;

// Explorer's wallpaper surface keeps video below icons and out of the taskbar.
internal sealed class VideoWallpaperWindow : Window
{
    private readonly MediaPlayer player=new() { IsMuted=true,IsLoopingEnabled=true };
    private readonly MediaPlayerElement surface=null!;
    public string? Failure { get; private set; }
    internal TimeSpan PlaybackPosition=>player.PlaybackSession.Position;
    internal bool IsPlaying=>player.PlaybackSession.PlaybackState==MediaPlaybackState.Playing;
    internal bool HasRenderSurface=>surface.IsLoaded&&surface.ActualWidth>0&&surface.ActualHeight>0;
    public VideoWallpaperWindow(string path)
    {
        Closed+=(_,_)=>{ surface?.SetMediaPlayer(null);player.Source=null;player.Dispose(); };
        try
        {
        IntPtr desktop=FindWallpaperSurface();
        if(desktop==IntPtr.Zero)throw new InvalidOperationException("Windows Explorer's wallpaper surface is unavailable. The video preview remains as the desktop image.");
        Title="R - Wallpaper Studio Video";
        var presenter=(OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;
        AppWindow.IsShownInSwitchers=false;
        surface=new MediaPlayerElement { AreTransportControlsEnabled=false,Stretch=Stretch.UniformToFill,IsHitTestVisible=false };
        Content=surface;
        player.CommandManager.IsEnabled=false;
        player.MediaFailed+=(_,e)=>Failure=e.ErrorMessage;
        surface.SetMediaPlayer(player);
        player.Source=MediaSource.CreateFromUri(new Uri(path));
        IntPtr handle=WinRT.Interop.WindowNative.GetWindowHandle(this);
        long style=GetWindowLongPtr(handle,-16).ToInt64();
        SetWindowLongPtr(handle,-16,new IntPtr((style & ~unchecked((long)0x80000000)) | 0x40000000));
        SetWindowLongPtr(handle,-20,new IntPtr(0x08000080L)); // No activation, tool window.
        SetParent(handle,desktop);
        if(GetParent(handle)!=desktop)throw new InvalidOperationException("Windows refused to attach the video to the desktop.");
        GetClientRect(desktop,out var bounds);
        SetWindowPos(handle,IntPtr.Zero,0,0,bounds.Right,bounds.Bottom,0x0010|0x0040|0x0020);
        player.Play();
        }
        catch { Close();throw; }
    }
    private static IntPtr FindWallpaperSurface()
    {
        IntPtr progman=FindWindow("Progman",null);
        SendMessageTimeout(progman,0x052C,new IntPtr(0xD),new IntPtr(1),0,1000,out _);
        IntPtr result=IntPtr.Zero;
        EnumWindows((window,_)=>
        {
            if(FindWindowEx(window,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero)
                result=FindWindowEx(IntPtr.Zero,window,"WorkerW",null);
            return result==IntPtr.Zero;
        },IntPtr.Zero);
        // Recent Explorer builds place WorkerW under Progman instead.
        if(result==IntPtr.Zero)result=FindWindowEx(progman,IntPtr.Zero,"WorkerW",null);
        return result;
    }
    private delegate bool EnumWindowProc(IntPtr window,IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindow(string name,string? title);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string name,string? title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback,IntPtr parameter);
    [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr child);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window,out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
}
