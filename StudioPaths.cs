using System;
using System.IO;

namespace WallpaperStudioShared
{
    // Both executables must use the same settings directory in portable mode.
    internal static class StudioPaths
    {
        public static string StillPath(string path)
        {
            if(string.IsNullOrEmpty(path))return "";
            if(Array.IndexOf(new[]{".mp4",".m4v",".mov",".wmv",".avi",".mkv",".webm"},Path.GetExtension(path).ToLowerInvariant())<0)return path;
            var file=new FileInfo(path);
            string key=file.FullName.ToUpperInvariant()+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
            using(var hash=System.Security.Cryptography.SHA256.Create())
                return Path.Combine(DataDirectory,"VideoPosters",BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-","")+".jpg");
        }
        public static bool IsPortable { get { return File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"portable.flag")); } }
        public static string DataDirectory { get { return IsPortable
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Data")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RWallpaperStudio"); } }
        public static string SettingsMutex { get { return "Local\\RWallpaperStudio.Settings" + Suffix; } }
        public static string OpenSettingsEvent { get { return "Local\\RWallpaperStudio.OpenSettings" + Suffix; } }
        private static string Suffix { get { return IsPortable ? ".Portable" : ""; } }
    }
}
