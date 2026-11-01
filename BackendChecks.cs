using System;
using System.Device.Location;
using PrivacyWallpaperService;
class BackendChecks {
 static void Main() {
  string places="[{\"latitude\":20,\"longitude\":30},{\"latitude\":40,\"longitude\":50}]";
  Check(Program.IsInsideSavedPlace(new GeoCoordinate(20.0001,30),places,300),"First saved place activates location wallpapers");
  Check(Program.IsInsideSavedPlace(new GeoCoordinate(40.0001,50),places,300),"Second saved place activates location wallpapers");
  Check(!Program.IsInsideSavedPlace(new GeoCoordinate(0,0),places,300),"Outside all places uses default wallpaper");
  Check(!Program.IsInsideSavedPlace(new GeoCoordinate(20,30),"[]",300),"Removing the last place never restores legacy home coordinates");
  Check(!Program.IsInsideSavedPlace(GeoCoordinate.Unknown,places,300),"Unavailable location stays private");
  Check(!Program.IsInsideSavedPlace(new GeoCoordinate(20,30),"[{\"latitude\":999,\"longitude\":30}]",300),"Invalid saved coordinates are ignored");
  Check(!Program.IsInsideSavedPlace(new GeoCoordinate(20,30),places,-1),"Invalid radius stays private");
  Check(!Program.IsInsideSavedPlace(new GeoCoordinate(20.01,30),places,300),"Existing 300 metre radius is enforced");
  string fix="{\"available\":true,\"accuracy\":20,\"latitude\":20,\"longitude\":30,\"timestamp\":1000,\"providerId\":42}";
  Check(Program.IsInsideSavedPlace(Program.ReadFreshPosition(fix,42,1010),places,300),"Modern location fix activates a saved place");
  Check(Program.ReadFreshPosition(fix,42,1050).IsUnknown,"Expired location cannot activate personal wallpaper");
  Check(Program.ReadFreshPosition(fix,43,1010).IsUnknown,"Previous provider location is rejected after restart");
  Check(Program.ReadFreshPosition(fix.Replace("true","false"),42,1010).IsUnknown,"Denied location cannot activate personal wallpaper");
  Check(!Program.MayShowLocation(true,true,false),"Manual hide wins even at a saved place");
  Check(Program.MayShowLocation(true,false,true),"Locking preserves location-based selection");
  Check(Program.MayShowLocation(true,false,false),"Unhiding allows location wallpapers again");
  Check(Program.AdjacentIndex(3,0,-1)==2 && Program.AdjacentIndex(3,2,1)==0,"Previous and next wrap around the carousel");
  Check(Program.AdjacentIndex(0,-1,1)==-1 && Program.AdjacentIndex(1,0,-1)==0,"Empty and single-image carousels are safe");
  Check(Program.IsInsideSavedPlace(new GeoCoordinate(20.001,30),places,150),"A location approximately 111 metres away is inside the saved area");
  Check(!Program.IsInsideSavedPlace(new GeoCoordinate(20.002,30),places,150),"A location approximately 222 metres away is outside the new 150 metre area");
  var policy=new AppConfig { AwayWallpaper="default",LockScreenWallpaper="independent",MatchHomeScreen=true,MatchDefaultWallpaper=true };
  Check(Program.ChooseLockWallpaper(policy,true,"carousel-image")=="carousel-image","Match Home uses the current carousel wallpaper at a saved location");
  Check(Program.ChooseLockWallpaper(policy,false,"default")=="default","Match Default uses the default wallpaper outside saved locations");
  policy.MatchDefaultWallpaper=false;
  Check(Program.ChooseLockWallpaper(policy,false,"default")=="default","Match Home mirrors the default outside even when Match Default is off");
  policy.HideLocationWallpaper=true;
  Check(Program.ChooseLockWallpaper(policy,true,"personal")=="default","Keyboard privacy override wins over matched home wallpaper");
  policy.MatchHomeScreen=false;
  Check(Program.ChooseLockWallpaper(policy,true,"personal")=="independent","Without Match Home the independent lock image is retained");
  Check(Program.IsVideo("clip.MP4")&&!Program.IsVideo("photo.png"),"Video classification is case-insensitive and excludes images");
  string network="{\"online\":true,\"providerId\":42,\"timestamp\":1000}";
  Check(Program.HasFreshNetwork(network,42,1010),"Fresh connected network permits location wallpaper checks");
  Check(!Program.HasFreshNetwork(network.Replace("true","false"),42,1010),"Network loss overrides a still-valid cached location");
  Check(!Program.HasFreshNetwork(network,43,1010)&&!Program.HasFreshNetwork(network,42,1016),"Previous-worker and stale network reports fail closed");
  Check(!Program.HasFreshNetwork("",42,1010)&&!Program.HasFreshNetwork("broken",42,1010),"Missing or invalid network status fails closed");
  policy.MatchHomeScreen=true;policy.MatchDefaultWallpaper=true;policy.HideLocationWallpaper=false;
  Check(Program.ChooseLockWallpaper(policy,false,"default")=="default","Disconnected state replaces the matched location lock image with default");
  policy.MatchDefaultWallpaper=false;
  Check(Program.ChooseLockWallpaper(policy,false,"default")=="default","Disconnected state mirrors the default desktop when Match Home is enabled");
  string temporary=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wallpaper-mode-"+Guid.NewGuid()+".json");
  try { var settings=new AppConfig { HideLocationWallpaper=true,CarouselSelectionToken="new-selection" };settings.Save(temporary);Check(AppConfig.Load(temporary).HideLocationWallpaper,"Manual hide survives settings save and reload");Check(AppConfig.Load(temporary).CarouselSelectionToken=="new-selection","Explicit selection requests survive settings save and reload"); }
  finally { if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary); }
 }
 static void Check(bool condition,string message) { if(!condition)throw new Exception(message);Console.WriteLine("PASS: "+message); }
}
