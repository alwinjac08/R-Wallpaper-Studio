using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using PrivacyWallpaperService;

class BackendTransitionChecks
{
    static void Set(string name,object value) { typeof(Program).GetField(name,BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,value); }
    static Dictionary<string,object> Output(string dir) { return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(dir,"wallpaper-output.json"))); }
    static void Check(bool value,string label) { if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label); }
    [STAThread] static void Main()
    {
        string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"VerificationData","NetworkTransitions");Directory.CreateDirectory(dir);
        string normal=Path.Combine(dir,"default.png"),one=Path.Combine(dir,"one.png"),two=Path.Combine(dir,"two.png"),locked=Path.Combine(dir,"lock.png");
        foreach(string path in new[]{normal,one,two,locked})File.WriteAllText(path,"Policy test placeholder; never rendered");
        string configPath=Path.Combine(dir,"config.json"),locations=Path.Combine(dir,"saved-locations.json"),position=Path.Combine(dir,"current-location.json"),network=Path.Combine(dir,"network-status.json");
        var config=new AppConfig { AwayWallpaper=normal,LockScreenWallpaper=locked,MatchHomeScreen=true,MatchDefaultWallpaper=true,CarouselEnabled=true,SelectedHomeWallpaper=one,HomeWallpapers=new List<string>{one,two},CarouselSelectionToken="first" };
        config.Save(configPath);
        File.WriteAllText(locations,"[{\"latitude\":20,\"longitude\":30}]");
        using(var process=Process.GetCurrentProcess())using(var timer=new System.Windows.Forms.Timer())
        {
            var json=new JavaScriptSerializer();double now=(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds;
            File.WriteAllText(position,json.Serialize(new { available=true,accuracy=20,latitude=20,longitude=30,timestamp=now,providerId=process.Id }));
            Set("configPath",configPath);Set("locationsPath",locations);Set("currentLocationPath",position);Set("locationProvider",process);Set("carouselTimer",timer);
            File.WriteAllText(network,json.Serialize(new { online=true,timestamp=now,providerId=process.Id }));
            Program.CheckLocationAndSetWallpaper();var output=Output(dir);
            Check((string)output["desktop"]==one&&(string)output["lockScreen"]==one,"Connected saved location applies the carousel image to both screens");
            File.WriteAllText(network,json.Serialize(new { online=false,timestamp=now,providerId=process.Id }));
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==normal&&(string)output["lockScreen"]==normal&&!timer.Enabled,"Network loss replaces both location wallpapers and stops rotation despite a fresh location fix");
            File.WriteAllText(network,json.Serialize(new { online=true,timestamp=now,providerId=process.Id }));
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==one&&(string)output["lockScreen"]==one&&timer.Enabled,"Reconnection resumes location wallpapers and carousel rotation");
            config.SelectedHomeWallpaper=two;config.CarouselSelectionToken="second";config.Save(configPath);
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==two&&(string)output["lockScreen"]==two&&timer.Enabled,"Set Active switches the running carousel and matched lock screen immediately");
            Set("currentWallpaper",one);config.CarouselSelectionToken="repeat";config.Save(configPath);
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==two,"Selecting the same configured image again overrides the carousel's current image");
            File.WriteAllText(position,json.Serialize(new {available=true,accuracy=20,latitude=20.03,longitude=30,timestamp=now,providerId=process.Id}));
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==normal&&(string)output["lockScreen"]==normal,"Moving kilometres outside the saved circle restores both defaults");
            Check((bool)output["allowVideo"],"Default video playback remains allowed outside even with location carousel enabled");
            File.WriteAllText(position,json.Serialize(new {available=true,accuracy=2500,latitude=20,longitude=30,timestamp=now,providerId=process.Id}));
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==normal,"A coarse fix centred on home cannot activate location wallpaper");
            File.WriteAllText(network,json.Serialize(new {online=true,changedAt=now+1,timestamp=now,providerId=process.Id}));
            File.WriteAllText(position,json.Serialize(new {available=true,accuracy=20,latitude=20,longitude=30,timestamp=now,providerId=process.Id}));
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==normal,"Network changes reject an otherwise fresh fix from the previous connection");
            File.WriteAllText(position,json.Serialize(new {available=true,accuracy=20,latitude=20,longitude=30,timestamp=now+2,providerId=process.Id}));
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==two,"A fresh precise home fix resumes automatically after reconnecting");
            config.HideLocationWallpaper=true;config.Save(configPath);
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==normal,"Manual default mode remains explicit even when home is confirmed");
            config.MatchDefaultWallpaper=false;config.Save(configPath);
            File.WriteAllText(position,json.Serialize(new {available=true,accuracy=20,latitude=20.03,longitude=30,timestamp=now+2,providerId=process.Id}));
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["lockScreen"]==normal,"Match Home mirrors manual default mode outside saved locations");
            config.MatchHomeScreen=false;config.Save(configPath);
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["lockScreen"]==locked,"Turning off both match options applies the independent lock image immediately");
            config.MatchHomeScreen=true;
            config.MatchDefaultWallpaper=false;
            File.WriteAllText(position,json.Serialize(new {available=true,accuracy=20,latitude=20,longitude=30,timestamp=now+2,providerId=process.Id}));
            config.HideLocationWallpaper=false;config.Save(configPath);
            Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==two,"Disabling manual default mode resumes using the current location");
            Program.SetSessionEndPolicy();Program.CheckLocationAndSetWallpaper();output=Output(dir);
            Check((string)output["desktop"]==normal&&(string)output["lockScreen"]==normal&&!(bool)output["allowVideo"]&&!timer.Enabled,
                "Shutdown publishes both defaults, stops playback and cannot be overridden by another location check");
            config.MatchHomeScreen=false;config.MatchDefaultWallpaper=false;Set("config",config);
            Program.SetSessionEndPolicy();output=Output(dir);
            Check((string)output["desktop"]==normal&&(string)output["lockScreen"]==locked,"Shutdown preserves the independent lock image when both match options are off");
        }
    }
}
