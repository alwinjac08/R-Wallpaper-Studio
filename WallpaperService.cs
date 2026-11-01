using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Device.Location;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace PrivacyWallpaperService
{
    public class AppConfig
    {
        public string Theme { get; set; }
        public string AwayWallpaper { get; set; }
        public string LockScreenWallpaper { get; set; }
        public bool MatchDefaultWallpaper { get; set; }
        public bool MatchHomeScreen { get; set; }
        public List<string> HomeWallpapers { get; set; }
        public string SelectedHomeWallpaper { get; set; }
        public string CarouselSelectionToken { get; set; }
        public bool CarouselEnabled { get; set; }
        public bool HideLocationWallpaper { get; set; }
        public int CarouselIntervalMinutes { get; set; }
        public double HomeLatitude { get; set; }
        public double HomeLongitude { get; set; }
        public double RadiusMeters { get; set; }

        public AppConfig()
        {
            Theme = "Light";
            HomeWallpapers = new List<string>();
            CarouselEnabled = false;
            CarouselIntervalMinutes = 15;
            RadiusMeters = 150.0;
            HomeLatitude = 0;
            HomeLongitude = 0;
            AwayWallpaper = "";
            LockScreenWallpaper = "";
            MatchDefaultWallpaper = true;
            SelectedHomeWallpaper = "";
        }

        public static AppConfig Load(string path)
        {
            AppConfig cfg = new AppConfig();
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    Dictionary<string, object> dict = serializer.Deserialize<Dictionary<string, object>>(json);

                    if (dict != null)
                    {
                        if (dict.ContainsKey("Theme") && dict["Theme"] != null)
                            cfg.Theme = dict["Theme"].ToString();

                        if (dict.ContainsKey("AwayWallpaper") && dict["AwayWallpaper"] != null)
                            cfg.AwayWallpaper = dict["AwayWallpaper"].ToString();

                        if (dict.ContainsKey("LockScreenWallpaper") && dict["LockScreenWallpaper"] != null)
                            cfg.LockScreenWallpaper = dict["LockScreenWallpaper"].ToString();
                        else
                            cfg.LockScreenWallpaper = cfg.AwayWallpaper;

                        if (dict.ContainsKey("MatchDefaultWallpaper"))
                            cfg.MatchDefaultWallpaper = Convert.ToBoolean(dict["MatchDefaultWallpaper"]);
                        if (dict.ContainsKey("MatchHomeScreen"))
                            cfg.MatchHomeScreen = Convert.ToBoolean(dict["MatchHomeScreen"]);

                        if (dict.ContainsKey("HomeWallpapers") && dict["HomeWallpapers"] is ArrayList)
                        {
                            ArrayList list = (ArrayList)dict["HomeWallpapers"];
                            foreach (object item in list)
                            {
                                if (item != null && !string.IsNullOrEmpty(item.ToString()))
                                    cfg.HomeWallpapers.Add(item.ToString());
                            }
                        }
                        else if (dict.ContainsKey("HomeWallpaper") && dict["HomeWallpaper"] != null)
                        {
                            cfg.HomeWallpapers.Add(dict["HomeWallpaper"].ToString());
                        }

                        if (dict.ContainsKey("SelectedHomeWallpaper") && dict["SelectedHomeWallpaper"] != null)
                            cfg.SelectedHomeWallpaper = dict["SelectedHomeWallpaper"].ToString();
                        else if (cfg.HomeWallpapers.Count > 0)
                            cfg.SelectedHomeWallpaper = cfg.HomeWallpapers[0];
                        if(dict.ContainsKey("CarouselSelectionToken") && dict["CarouselSelectionToken"]!=null)
                            cfg.CarouselSelectionToken=dict["CarouselSelectionToken"].ToString();

                        if (dict.ContainsKey("CarouselEnabled"))
                            cfg.CarouselEnabled = Convert.ToBoolean(dict["CarouselEnabled"]);
                        if (dict.ContainsKey("HideLocationWallpaper"))
                            cfg.HideLocationWallpaper = Convert.ToBoolean(dict["HideLocationWallpaper"]);

                        if (dict.ContainsKey("CarouselIntervalMinutes"))
                            cfg.CarouselIntervalMinutes = Convert.ToInt32(dict["CarouselIntervalMinutes"]);

                        if (dict.ContainsKey("HomeLatitude"))
                            cfg.HomeLatitude = Convert.ToDouble(dict["HomeLatitude"]);

                        if (dict.ContainsKey("HomeLongitude"))
                            cfg.HomeLongitude = Convert.ToDouble(dict["HomeLongitude"]);

                        cfg.RadiusMeters = 150.0;

                        return cfg;
                    }
                }
            }
            catch {}
            return GetDefault();
        }

        public static AppConfig GetDefault()
        {
            AppConfig def = new AppConfig();
            def.SelectedHomeWallpaper = "";
            return def;
        }

        public void Save(string path)
        {
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(this));
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            } finally { if(File.Exists(temporary))File.Delete(temporary); }
        }
    }

    static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        const int SPI_SETDESKWALLPAPER = 20;
        const int SPIF_UPDATEINIFILE = 0x01;
        const int SPIF_SENDCHANGE = 0x02;

        static AppConfig config;
        static string configPath;
        static string appDir;
        static string currentWallpaper = "";
        static bool isChecking = false;
        static bool isAtHome = false;

        static NotifyIcon trayIcon;
        static ToolStripMenuItem menuCarouselToggle;
        static ToolStripMenuItem menuPrivacy,menuPrevious,menuNext;
        static string resumeWallpaper="";
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
        sealed class HotkeyControl : Form
        {
            // A hidden top-level window receives shutdown/cancel messages; a parked child control may not.
            public HotkeyControl() { ShowInTaskbar=false;FormBorderStyle=FormBorderStyle.None; }
            protected override void WndProc(ref Message message)
            {
                if(message.Msg==0x0312 && message.WParam.ToInt32()==1) { TogglePrivacy();return; }
                if(message.Msg==0x0011) { PrepareForSessionEnd();message.Result=new IntPtr(1);return; }
                if(message.Msg==0x0016 && message.WParam==IntPtr.Zero && sessionEnding)
                { sessionEnding=false;privacyMode=false;RequireFreshLocation();CheckLocationAndSetWallpaper(); }
                base.WndProc(ref message);
            }
        }
        public static bool MayShowLocation(bool atHome,bool hidden,bool locked) { return atHome&&!hidden; }
        public static bool IsVideo(string path) { return Array.IndexOf(new[]{".mp4",".m4v",".mov",".wmv",".avi",".mkv",".webm"},Path.GetExtension(path??"").ToLowerInvariant())>=0; }
        public static string ChooseLockWallpaper(AppConfig settings,bool atHome,string desktop)
        {
            if(settings.MatchHomeScreen)return settings.HideLocationWallpaper?settings.AwayWallpaper:desktop;
            return settings.MatchDefaultWallpaper?settings.AwayWallpaper:settings.LockScreenWallpaper;
        }
        static string lastOutput="";
        static string lastNetworkText="",lastSelectionToken="";
        static string NetworkPath { get { return Path.Combine(Path.GetDirectoryName(configPath),"network-status.json"); } }
        public class NetworkState
        {
            public double changedAt { get; set; }
            public bool online { get; set; }
            public int providerId { get; set; }
            public double timestamp { get; set; }
        }
        public static bool HasFreshNetwork(string json,int providerId,double now)
        {
            try {
                var state=new JavaScriptSerializer().Deserialize<NetworkState>(json);
                return state!=null && state.online && state.providerId==providerId && now-state.timestamp<=15 && state.timestamp<=now+5;
            } catch { return false; }
        }
        static bool NetworkReady()
        {
            return NetworkInterface.GetIsNetworkAvailable() && locationProvider!=null && !locationProvider.HasExited &&
                HasFreshNetwork(File.Exists(NetworkPath)?File.ReadAllText(NetworkPath):"",locationProvider.Id,(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds);
        }
        static void PublishOutput()
        {
            if(config==null || string.IsNullOrEmpty(currentWallpaper))return;
            string text=new JavaScriptSerializer().Serialize(new { desktop=currentWallpaper,
                lockScreen=ChooseLockWallpaper(config,isAtHome,currentWallpaper),paused=privacyMode,
                allowVideo=!sessionEnding&&(!config.CarouselEnabled||!isAtHome||config.HideLocationWallpaper),sessionEnding=sessionEnding,parentId=Process.GetCurrentProcess().Id });
            if(text==lastOutput)return;
            string path=Path.Combine(Path.GetDirectoryName(configPath),"wallpaper-output.json"),temporary=path+".tmp";
            File.WriteAllText(temporary,text);
            if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            lastOutput=text;
        }
        public static int AdjacentIndex(int count,int current,int direction)
        { return count==0?-1:current<0?(direction<0?count-1:0):(current+direction+count)%count; }
        static void StopLocationProvider()
        {
            if(locationProvider==null)return;
            try { if(!locationProvider.HasExited)locationProvider.Kill(); } catch { }
            locationProvider.Dispose();locationProvider=null;retryProviderAfter=DateTime.MinValue;
        }
        static void TogglePrivacy()
        {
            try {
                config=AppConfig.Load(configPath);
                config.HideLocationWallpaper=!config.HideLocationWallpaper;
                config.Save(configPath);
                if(config.HideLocationWallpaper) {
                    resumeWallpaper=currentWallpaper;carouselTimer.Stop();SetWallpaper(config.AwayWallpaper);PublishOutput();
                } else { isAtHome=false;CheckLocationAndSetWallpaper(); }
                UpdateTrayMenu();PublishOutput();
            } catch(Exception ex) { trayIcon.ShowBalloonTip(3000,"Unable to change wallpaper mode",ex.Message,ToolTipIcon.Error); }
        }
        static void StepCarousel(int direction)
        {
            if(!NetworkReady()) { CheckLocationAndSetWallpaper();return; }
            if(!MayShowLocation(isAtHome,config.HideLocationWallpaper,privacyMode)||!config.CarouselEnabled)return;
            var valid=GetValidHomeWallpapers();
            int index=valid.FindIndex(p=>string.Equals(p,currentWallpaper,StringComparison.OrdinalIgnoreCase));
            int next=AdjacentIndex(valid.Count,index,direction);
            if(next>=0) { SetWallpaper(valid[next]);carouselTimer.Stop();carouselTimer.Start(); }
        }
        static System.Windows.Forms.Timer carouselTimer;
        static Random rand = new Random();

        public static void SetWallpaper(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return;
                if (string.Equals(currentWallpaper, path, StringComparison.OrdinalIgnoreCase)) return;

                if (File.Exists(path))
                {
                    currentWallpaper = path;
                    PublishOutput();
                }
            }
            catch {}
        }

        static bool privacyMode;
        static bool sessionEnding;
        static double minimumFixTime;
        static void RequireFreshLocation()
        {
            minimumFixTime=(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;
            isAtHome=false;carouselTimer.Stop();SetWallpaper(config.AwayWallpaper);PublishOutput();
        }
        internal static void SetSessionEndPolicy()
        {
            sessionEnding=true;privacyMode=true;isAtHome=false;
            carouselTimer.Stop();SetWallpaper(config.AwayWallpaper);PublishOutput();
        }
        static void ApplyDefaultDesktopNow()
        {
            try { string still=WallpaperStudioShared.StudioPaths.StillPath(config.AwayWallpaper);
                if(File.Exists(still))SystemParametersInfo(SPI_SETDESKWALLPAPER,0,still,SPIF_UPDATEINIFILE|SPIF_SENDCHANGE); } catch { }
        }
        static void PrepareForSessionEnd()
        {
            if(sessionEnding || config==null)return;
            SetSessionEndPolicy();ApplyDefaultDesktopNow();
            // Give the existing worker a bounded opportunity to persist the default lock image.
            var deadline=Stopwatch.StartNew();
            while(deadline.ElapsedMilliseconds<4000 && locationProvider!=null && !locationProvider.HasExited)
            {
                try {
                    var status=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(configPath),"wallpaper-status.json")));
                    if(status.ContainsKey("resetAcknowledged") && Convert.ToBoolean(status["resetAcknowledged"]) &&
                        status.ContainsKey("parentId") && Convert.ToInt32(status["parentId"])==Process.GetCurrentProcess().Id)break;
                } catch { }
                Thread.Sleep(50);
            }
        }
        static string lastConfigText = "";
        static string locationsPath;
        static Control dispatcher;
        static System.Windows.Forms.Timer locationTimer;
        static DateTime nextLocationCheck = DateTime.MinValue;
        static string lastLocationsText = "";
        static string currentLocationPath;
        static string lastPositionText = "";
        static Process locationProvider;
        static DateTime retryProviderAfter=DateTime.MinValue;

        public class LocationFix
        {
            public double? accuracy { get; set; }
            public string source { get; set; }
            public bool available { get; set; }
            public double latitude { get; set; }
            public double longitude { get; set; }
            public double timestamp { get; set; }
            public int providerId { get; set; }
        }
        public static GeoCoordinate ReadFreshPosition(string json,int providerId,double now)
        {
            var fix=new JavaScriptSerializer().Deserialize<LocationFix>(json);
            if(fix==null || !fix.available || fix.providerId!=providerId || now-fix.timestamp>30 || fix.timestamp>now+5 ||
                !fix.accuracy.HasValue || double.IsNaN(fix.accuracy.Value) || fix.accuracy<=0 || fix.accuracy>150 || fix.source=="Default" || fix.source=="IPAddress" ||
                double.IsNaN(fix.latitude) || double.IsNaN(fix.longitude) || Math.Abs(fix.latitude)>90 || Math.Abs(fix.longitude)>180)
                return GeoCoordinate.Unknown;
            return new GeoCoordinate(fix.latitude,fix.longitude);
        }
        static void EnsureLocationProvider()
        {
            if(locationProvider!=null && !locationProvider.HasExited)return;
            if(DateTime.UtcNow<retryProviderAfter)return;
            retryProviderAfter=DateTime.UtcNow.AddSeconds(30);
            if(locationProvider!=null)locationProvider.Dispose();
            locationProvider=Process.Start(new ProcessStartInfo(Path.Combine(appDir,"WallpaperWorker.exe"),"--location-provider "+Process.GetCurrentProcess().Id) {
                UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=appDir });
        }

        public class SavedPlace
        {
            public double latitude { get; set; }
            public double longitude { get; set; }
        }
        public static bool IsInsideSavedPlace(GeoCoordinate current, string json, double radius)
        {
            if (current == null || current.IsUnknown || double.IsNaN(radius) || radius <= 0) return false;
            var places = new JavaScriptSerializer().Deserialize<List<SavedPlace>>(json);
            if (places == null) return false;
            foreach (var place in places)
            {
                if (double.IsNaN(place.latitude) || double.IsNaN(place.longitude) || Math.Abs(place.latitude)>90 || Math.Abs(place.longitude)>180) continue;
                if(current.GetDistanceTo(new GeoCoordinate(place.latitude, place.longitude)) <= radius) return true;
            }
            return false;
        }
        static void OnUI(Action action)
        {
            if (dispatcher == null || dispatcher.IsDisposed) return;
            if (dispatcher.InvokeRequired) dispatcher.BeginInvoke(action); else action();
        }
        public static void CheckLocationAndSetWallpaper()
        {
            if (isChecking) return;
            isChecking = true;
            try
            {
                string configText=File.ReadAllText(configPath);
                if(sessionEnding){isAtHome=false;carouselTimer.Stop();SetWallpaper(config.AwayWallpaper);return;}
                bool changed=configText!=lastConfigText;
                if(changed || config==null)config=AppConfig.Load(configPath);
                lastConfigText=configText;
                if(config.CarouselSelectionToken!=lastSelectionToken) { resumeWallpaper="";lastSelectionToken=config.CarouselSelectionToken; }
                UpdateTrayMenu();
                string locations=File.Exists(locationsPath)?File.ReadAllText(locationsPath):"[]";
                lastLocationsText=locations;
                EnsureLocationProvider();
                lastNetworkText=File.Exists(NetworkPath)?File.ReadAllText(NetworkPath):"";
                var networkState=new JavaScriptSerializer().Deserialize<NetworkState>(lastNetworkText);
                if(networkState!=null)minimumFixTime=Math.Max(minimumFixTime,networkState.changedAt);
                if(!NetworkReady())
                {
                    isAtHome=false;carouselTimer.Stop();SetWallpaper(config.AwayWallpaper);return;
                }
                EnsureLocationProvider();
                lastPositionText=File.Exists(currentLocationPath)?File.ReadAllText(currentLocationPath):"";
                var fix=lastPositionText.Length>0?new JavaScriptSerializer().Deserialize<LocationFix>(lastPositionText):null;
                GeoCoordinate current=locationProvider!=null && !locationProvider.HasExited && lastPositionText.Length>0
                    && fix!=null && fix.timestamp>=minimumFixTime
                    ? ReadFreshPosition(lastPositionText,locationProvider.Id,(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds)
                    : GeoCoordinate.Unknown;
                bool wasAtHome=isAtHome;
                isAtHome=IsInsideSavedPlace(current,locations,config.RadiusMeters);
                if(config.HideLocationWallpaper) { carouselTimer.Stop();SetWallpaper(config.AwayWallpaper);return; }
                if(isAtHome)
                {
                    if(!wasAtHome || changed || string.IsNullOrEmpty(currentWallpaper)) ApplyHomeWallpaper();
                }
                else { carouselTimer.Stop();SetWallpaper(config.AwayWallpaper); }
            }
            catch { isAtHome=false;carouselTimer.Stop();SetWallpaper(config.AwayWallpaper); }
            finally { isChecking=false;nextLocationCheck=DateTime.UtcNow.AddSeconds(30);UpdateTrayMenu();PublishOutput(); }
        }
        public static void ApplyHomeWallpaper()
        {
            if(!NetworkReady()) { isAtHome=false;carouselTimer.Stop();SetWallpaper(config.AwayWallpaper);PublishOutput();return; }
            if(!MayShowLocation(isAtHome,config.HideLocationWallpaper,privacyMode))return;
            if (config.CarouselEnabled)
            {
                List<string> valid = GetValidHomeWallpapers();
                if (valid.Count > 0)
                {
                    string chosen = valid.Contains(resumeWallpaper)?resumeWallpaper:valid.Contains(config.SelectedHomeWallpaper) ? config.SelectedHomeWallpaper : PickRandomWallpaper(valid);
                    resumeWallpaper="";
                    SetWallpaper(chosen);
                }
                else
                {
                    SetWallpaper(config.AwayWallpaper);
                }

                int intervalMs = Math.Max(1, Math.Min(120,config.CarouselIntervalMinutes)) * 60 * 1000;
                carouselTimer.Interval = intervalMs;
                carouselTimer.Stop();
                if(!privacyMode && valid.Count>1)carouselTimer.Start();
            }
            else
            {
                carouselTimer.Stop();
                string chosen = !string.IsNullOrEmpty(config.SelectedHomeWallpaper) && File.Exists(config.SelectedHomeWallpaper)
                    ? config.SelectedHomeWallpaper
                    : (config.HomeWallpapers != null && config.HomeWallpapers.Count > 0 ? config.HomeWallpapers[0] : config.AwayWallpaper);
                SetWallpaper(chosen);
            }
        }

        static List<string> GetValidHomeWallpapers()
        {
            List<string> valid = new List<string>();
            if (config.HomeWallpapers != null)
            {
                foreach (string path in config.HomeWallpapers)
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path) && !IsVideo(path))
                        valid.Add(path);
                }
            }
            return valid;
        }

        static string PickRandomWallpaper(List<string> valid)
        {
            if (valid.Count == 0) return config.AwayWallpaper;
            if (valid.Count == 1) return valid[0];

            List<string> candidates = new List<string>();
            foreach (string p in valid)
            {
                if (!string.Equals(p, currentWallpaper, StringComparison.OrdinalIgnoreCase))
                    candidates.Add(p);
            }

            if (candidates.Count > 0)
                return candidates[rand.Next(candidates.Count)];

            return valid[rand.Next(valid.Count)];
        }

        static void NextCarouselWallpaper()
        {
            if(!NetworkReady()) { CheckLocationAndSetWallpaper();return; }
            if (privacyMode || config.HideLocationWallpaper) return;
            if (!isAtHome) return;
            if (!config.CarouselEnabled) return;

            List<string> valid = GetValidHomeWallpapers();
            if (valid.Count > 1)
            {
                string next = PickRandomWallpaper(valid);
                SetWallpaper(next);
            }
        }

        static void UpdateTrayMenu()
        {
            if(menuPrivacy!=null) { menuPrivacy.Text=config.HideLocationWallpaper?"Show location wallpaper":"Hide location wallpaper";menuPrivacy.Checked=config.HideLocationWallpaper;menuPrivacy.Tag=config.HideLocationWallpaper?"\uE785":"\uE72E"; }
            bool canStep=MayShowLocation(isAtHome,config.HideLocationWallpaper,privacyMode)&&config.CarouselEnabled&&GetValidHomeWallpapers().Count>1;
            if(menuPrevious!=null)menuPrevious.Enabled=canStep;
            if(menuNext!=null)menuNext.Enabled=canStep;
            if (menuCarouselToggle != null)
            {
                if (config.CarouselEnabled)
                {
                    menuCarouselToggle.Text = string.Format("Turn off carousel ({0} min)", config.CarouselIntervalMinutes);
                    menuCarouselToggle.Checked = true;
                    menuCarouselToggle.Tag = "\uE769";
                }
                else
                {
                    menuCarouselToggle.Text = "Turn on carousel";
                    menuCarouselToggle.Checked = false;
                    menuCarouselToggle.Tag = "\uE768";
                }
            }

            if (trayIcon != null)
            {
                trayIcon.Text = "R - Wallpaper Studio · "+(config.HideLocationWallpaper?"Normal wallpaper":config.CarouselEnabled?"Carousel ON":"Carousel OFF");
            }
        }

        static void ToggleCarousel()
        {
            config=AppConfig.Load(configPath);
            config.CarouselEnabled = !config.CarouselEnabled;
            try { config.Save(configPath); } catch(Exception ex) { trayIcon.ShowBalloonTip(3000,"Unable to save carousel mode",ex.Message,ToolTipIcon.Error);return; }
            UpdateTrayMenu();

            if (config.CarouselEnabled)
            {
                trayIcon.ShowBalloonTip(1500, "Wallpaper Carousel", string.Format("Carousel enabled. Changes randomly every {0} minutes.", config.CarouselIntervalMinutes), ToolTipIcon.Info);
                if (isAtHome)
                {
                    ApplyHomeWallpaper();
                }
            }
            else
            {
                trayIcon.ShowBalloonTip(1500, "Wallpaper Carousel", "Carousel disabled. Showing selected single wallpaper.", ToolTipIcon.Info);
                if (isAtHome)
                {
                    ApplyHomeWallpaper();
                }
            }
        }

        static void OpenConfigApp()
        {
            try
            {
                string configExe = Path.Combine(appDir, "WallpaperStudio.exe");
                AllowSetForegroundWindow(-1);
                try { using(var signal=EventWaitHandle.OpenExisting(WallpaperStudioShared.StudioPaths.OpenSettingsEvent)) { signal.Set();return; } }
                catch(WaitHandleCannotBeOpenedException) { }
                if (File.Exists(configExe))
                {
                    Process.Start(new ProcessStartInfo(configExe) { WorkingDirectory = appDir });
                }
            }
            catch {}
        }
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

        [DllImport("kernel32.dll")] static extern bool SetProcessShutdownParameters(uint level,uint flags);
        [STAThread]
        static void Main()
        {
            // Shut down before the media worker so it can persist the fallback lock image.
            SetProcessShutdownParameters(0x300,0);
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\PrivacyWallpaperService_Headless_Mutex_V4", out createdNew))
            {
                if (!createdNew) return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                appDir = AppDomain.CurrentDomain.BaseDirectory;
                string dataDir=WallpaperStudioShared.StudioPaths.DataDirectory;
                Directory.CreateDirectory(dataDir);
                configPath=Path.Combine(dataDir,"config.json");
                locationsPath=Path.Combine(dataDir,"saved-locations.json");
                currentLocationPath=Path.Combine(dataDir,"current-location.json");
                dispatcher=new HotkeyControl(); var handle=dispatcher.Handle;
                config = AppConfig.Load(configPath);


                carouselTimer = new System.Windows.Forms.Timer();
                carouselTimer.Interval = Math.Max(1, Math.Min(120,config.CarouselIntervalMinutes)) * 60 * 1000;
                carouselTimer.Tick += (s, e) => NextCarouselWallpaper();

                // Setup Tray Icon
                var trayMenu = new ModernTrayMenu();
                var menuOpen = trayMenu.AddAction("Open Wallpaper Studio", "\uE8A7", (s, e) => OpenConfigApp());
                menuOpen.Font = trayMenu.StrongFont;
                trayMenu.AddDivider();
                menuCarouselToggle = trayMenu.AddAction("Turn on carousel", "\uE768", (s, e) => ToggleCarousel());
                menuPrevious = trayMenu.AddAction("Previous wallpaper", "\uE892", (s, e) => StepCarousel(-1));
                menuNext = trayMenu.AddAction("Next wallpaper", "\uE893", (s, e) => StepCarousel(1));
                menuPrivacy = trayMenu.AddAction("Hide location wallpaper", "\uE72E", (s, e) => TogglePrivacy());
                menuPrivacy.ShortcutKeyDisplayString = "Ctrl+Alt+P";
                trayMenu.AddAction("Refresh location", "\uE72C", (s, e) => CheckLocationAndSetWallpaper());
                trayMenu.AddDivider();
                trayMenu.AddAction("Exit Wallpaper Studio", "\uE7E8", (s, e) =>
                {
                    trayIcon.Visible = false;
                    carouselTimer.Stop();
                    Application.Exit();
                });
                trayMenu.Opening += (s, e) => UpdateTrayMenu();
                Icon appIcon = null;
                string iconPath = Path.Combine(appDir, "app_icon.ico");
                if (File.Exists(iconPath))
                {
                    try
                    {
                        using (FileStream fs = new FileStream(iconPath, FileMode.Open, FileAccess.Read))
                        {
                            appIcon = new Icon(fs);
                        }
                    }
                    catch {}
                }
                if (appIcon == null) appIcon = SystemIcons.Application;

                trayIcon = new NotifyIcon
                {
                    Icon = appIcon,
                    ContextMenuStrip = trayMenu,
                    Visible = true
                };

                trayIcon.MouseClick += (s, e) => { if(e.Button==MouseButtons.Left)OpenConfigApp(); };
                UpdateTrayMenu();
                bool shortcutRegistered=RegisterHotKey(dispatcher.Handle,1,0x4003,0x50);
                if(!shortcutRegistered)trayIcon.ShowBalloonTip(4000,"Wallpaper shortcut unavailable","Ctrl+Alt+P is already in use. Use Hide location-based wallpaper in this tray menu.",ToolTipIcon.Warning);

                SystemEvents.SessionEnding += (s,e) => {
                    if(dispatcher.InvokeRequired)dispatcher.Invoke(new Action(PrepareForSessionEnd));else PrepareForSessionEnd();
                };
                SystemEvents.SessionSwitch += (s,e) => OnUI(() => {
                    if(e.Reason==SessionSwitchReason.SessionLock)
                    {
                        privacyMode=true;carouselTimer.Stop();CheckLocationAndSetWallpaper();PublishOutput();
                    }
                    else if(e.Reason==SessionSwitchReason.SessionUnlock)
                    { privacyMode=false;RequireFreshLocation();CheckLocationAndSetWallpaper(); }
                });
                SystemEvents.PowerModeChanged += (s,e) => OnUI(() => {
                    if(e.Mode==PowerModes.Suspend) { privacyMode=true;RequireFreshLocation();ApplyDefaultDesktopNow(); }
                    else if(e.Mode==PowerModes.Resume) { privacyMode=false;RequireFreshLocation();CheckLocationAndSetWallpaper(); }
                });
                NetworkChange.NetworkAvailabilityChanged += (s,e) => OnUI(CheckLocationAndSetWallpaper);
                locationTimer=new System.Windows.Forms.Timer { Interval=1000 };
                locationTimer.Tick += (s,e) => {
                    try {
                        if(DateTime.UtcNow>=nextLocationCheck || File.ReadAllText(configPath)!=lastConfigText ||
                            (File.Exists(locationsPath)?File.ReadAllText(locationsPath):"[]")!=lastLocationsText ||
                            (File.Exists(currentLocationPath)?File.ReadAllText(currentLocationPath):"")!=lastPositionText ||
                            (File.Exists(NetworkPath)?File.ReadAllText(NetworkPath):"")!=lastNetworkText || (isAtHome&&!NetworkReady()))
                            CheckLocationAndSetWallpaper();
                    } catch { carouselTimer.Stop();SetWallpaper(config.AwayWallpaper); }
                };
                locationTimer.Start();
                RequireFreshLocation();ApplyDefaultDesktopNow();
                CheckLocationAndSetWallpaper();
                // Run message loop
                Application.Run(new ApplicationContext());
                PrepareForSessionEnd();
                if(shortcutRegistered)UnregisterHotKey(dispatcher.Handle,1);
                StopLocationProvider();trayIcon.Dispose();trayMenu.Dispose();dispatcher.Dispose();
                GC.KeepAlive(mutex);
            }
        }
    }
}
