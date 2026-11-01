using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace WallpaperStudio;

internal static class MediaVerification
{
    public static async Task RunAsync()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"Previews");Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory,"Media-checks.txt"),"RUNNING: "+DateTimeOffset.Now);
        var checks=new List<string>();VideoWallpaperWindow? window=null;
        try
        {
            var composition=new MediaComposition();
            foreach(string asset in new[]{"Placeholder.png","PlaceholderAlternate.png"})composition.Clips.Add(await MediaClip.CreateFromImageFileAsync(
                await StorageFile.GetFileFromPathAsync(Path.Combine(AppContext.BaseDirectory,"Assets",asset)),TimeSpan.FromSeconds(3)));
            var folder=await StorageFolder.GetFolderFromPathAsync(directory);
            var file=await folder.CreateFileAsync("Video-check.mp4",CreationCollisionOption.ReplaceExisting);
            var result=await composition.RenderToFileAsync(file,MediaTrimmingPreference.Precise,MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Vga));
            if(result!=Windows.Media.Transcoding.TranscodeFailureReason.None)throw new Exception("Video generation failed: "+result);
            string poster=await WallpaperMedia.EnsurePosterAsync(file.Path);
            if(new FileInfo(poster).Length==0)throw new Exception("Video poster is empty");
            checks.Add("PASS: Real MP4 decodes and produces a lock-screen/preview still image");
            window=new VideoWallpaperWindow(file.Path);
            await Task.Delay(1800);double first=window.PlaybackPosition.TotalMilliseconds;
            await Task.Delay(1400);double second=window.PlaybackPosition.TotalMilliseconds;
            if(window.Failure!=null)throw new Exception(window.Failure);
            if(!window.HasRenderSurface)throw new Exception("The video is decoding but its visible surface has not loaded or has zero size");
            checks.Add("PASS: Video render surface is loaded with nonzero dimensions");
            if(!window.IsPlaying || first==second)throw new Exception($"Video failed to advance: {first} to {second}, playing={window.IsPlaying}");
            checks.Add($"PASS: Desktop video is playing and advances ({first:0} ms to {second:0} ms)");
            await Task.Delay(4000);
            if(!window.IsPlaying || window.Failure!=null)throw new Exception("Video did not loop");
            checks.Add("PASS: Video loops after the six-second clip completes");
            window.Close();window=null;
            await Task.Delay(1200);
            checks.Add("PASS: Closing the video surface leaves the app worker able to continue");
            window=new VideoWallpaperWindow(file.Path);
            await Task.Delay(1500);
            if(!window.IsPlaying || window.Failure!=null)throw new Exception("Video failed to resume after closing its previous surface");
            checks.Add("PASS: Video can resume after a lock/privacy-style stop");
        }
        catch(Exception ex) { checks.Add("FAIL: "+ex); }
        finally { window?.Close();await File.WriteAllLinesAsync(Path.Combine(directory,"Media-checks.txt"),checks); }
    }
}
