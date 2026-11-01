using System.Security.Cryptography;
using System.Text;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace WallpaperStudio;

internal static class WallpaperMedia
{
    public static readonly string[] VideoExtensions=[".mp4",".m4v",".mov",".wmv",".avi",".mkv",".webm"];
    public static bool IsVideo(string path)=>VideoExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static string PosterPath(string path)
    {
        var file=new FileInfo(path);
        string key=file.FullName.ToUpperInvariant()+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
        return Path.Combine(WallpaperStudioShared.StudioPaths.DataDirectory,"VideoPosters",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))+".jpg");
    }
    public static async Task<string> EnsurePosterAsync(string path)
    {
        if(!IsVideo(path))return path;
        string poster=PosterPath(path);
        if(File.Exists(poster))return poster;
        var clip=await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(path));
        if(clip.OriginalDuration<=TimeSpan.Zero)throw new InvalidDataException("This video cannot be played. Try an H.264 MP4 video.");
        var composition=new MediaComposition();composition.Clips.Add(clip);
        using var frame=await composition.GetThumbnailAsync(TimeSpan.Zero,1920,1080,VideoFramePrecision.NearestFrame);
        Directory.CreateDirectory(Path.GetDirectoryName(poster)!);
        string temporary=poster+"."+Guid.NewGuid()+".tmp";
        try
        {
            using(var input=frame.AsStreamForRead())using(var output=File.Create(temporary))await input.CopyToAsync(output);
            File.Move(temporary,poster,true);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
        return poster;
    }
}
