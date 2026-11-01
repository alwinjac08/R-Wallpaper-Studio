using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Text;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace WallpaperStudio;

// Native WinUI interface. Wallpaper application/service integration remains separate.
public sealed partial class MainWindow : Window
{
    private const double DesignWidth=1014, DesignHeight=1110;
    private const string Paper="#F6F6FF", Card="#EBEBF9", Navy="#0E2058", Active="#082FA6", Inactive="#A6083F";
    private readonly Canvas artboard=new() { Width=DesignWidth,Height=DesignHeight,Background=Brush(Paper) };
    private readonly Canvas wallpaperContent=new() { Width=DesignWidth,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top };
    private readonly ScrollViewer wallpaperScroll=new() {
        Width=DesignWidth,Height=865,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
        HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        VerticalScrollMode=ScrollMode.Enabled,HorizontalContentAlignment=HorizontalAlignment.Left,
        VerticalContentAlignment=VerticalAlignment.Top,ZoomMode=ZoomMode.Disabled
    };
    private readonly FontFamily inter=new("ms-appx:///Assets/Inter.ttf#Inter");
    private readonly StudioState document=new();
    private readonly Dictionary<string,ImageSource> imageCache=new();
    private readonly Dictionary<WallpaperRole,CardView> cards=new();
    private readonly Canvas carouselCard=new() { Width=980,Height=363 };
    private readonly Canvas carouselRows=new() { Width=643,VerticalAlignment=VerticalAlignment.Top };
    private readonly ScrollViewer carouselScroll=new() {
        Width=643,Height=194,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
        HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
        VerticalContentAlignment=VerticalAlignment.Top,ZoomMode=ZoomMode.Disabled
    };
    private Border carouselPreview=null!;
    private TextBlock carouselName=null!;
    private Button removeImage=null!, setActive=null!,addImage=null!,selectFolder=null!;
    private ToggleButton carouselCheckbox=null!, matchCheckbox=null!, matchHomeCheckbox=null!;
    private bool pickerOpen,closed;
    private readonly bool verification=Environment.GetCommandLineArgs().Contains("--verify");
    private readonly bool capture=Environment.GetCommandLineArgs().Contains("--capture");
    private readonly bool releaseVerification=Environment.GetCommandLineArgs().Contains("--verify-first-run");

    public MainWindow()
    {
        Title="R - Wallpaper Studio V2.1";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"app_icon.ico"));
        var host=new Grid { Background=Brush(Paper),RequestedTheme=ElementTheme.Light,
            KeyboardAcceleratorPlacementMode=KeyboardAcceleratorPlacementMode.Hidden };
        host.Children.Add(new Viewbox { Child=artboard,Stretch=Stretch.Uniform });
        Content=host;
        var presenter=(OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false,false); presenter.IsResizable=false;
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Primary).WorkArea;
        double fit=Math.Min(1,Math.Min((area.Width-80)/DesignWidth,(area.Height-60)/DesignHeight));
        int width=(int)Math.Round(DesignWidth*fit),height=(int)Math.Round(DesignHeight*fit);
        AppWindow.MoveAndResize(new RectInt32(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height));
        Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(AppWindow.Id).SetRegionRects(
            Microsoft.UI.Input.NonClientRegionKind.Caption,[new RectInt32(0,0,(int)(870*fit),(int)(48*fit))]);
        document.CarouselEnabled=Environment.GetCommandLineArgs().Any(a=>a is "--carousel" or "--selected");
        if(Environment.GetCommandLineArgs().Contains("--selected")) document.SelectImage("home");
        BuildChrome();
        BuildWallpaperCard(WallpaperRole.Default,0,252,"Default Wallpaper");
        BuildWallpaperCard(WallpaperRole.LockScreen,268,286,"Default Lock screen Wallpaper");
        BuildWallpaperCard(WallpaperRole.Location,570,271,"Location Based Wallpaper");
        BuildCarousel(); BuildLocations(); RefreshCards(); RefreshCarousel(); RefreshLayout();
        for(int i=0;i<3;i++)
        {
            int preview=i;
            var shortcut=new KeyboardAccelerator { Key=(Windows.System.VirtualKey)((int)Windows.System.VirtualKey.Number1+i),Modifiers=Windows.System.VirtualKeyModifiers.Control };
            shortcut.Invoked+=(_,e)=> { document.CarouselEnabled=preview!=0; if(preview==2) document.SelectImage("home"); RefreshCarousel(); RefreshLayout(); e.Handled=true; };
            host.KeyboardAccelerators.Add(shortcut);
        }
        Activated+=(_,e)=> { windowActive=e.WindowActivationState!=WindowActivationState.Deactivated;
            if(windowActive && mapReady && !verification && !capture) _=RequestCurrentLocationAsync(false); };
        Closed+=(_,_)=> { closed=true;FinishCarouselDrag(true); DisposeMap(); };
        if(verification || capture) artboard.Loaded+=async (_,_)=>await VerifyAndCaptureAsync();
        else artboard.Loaded+=async (_,_)=> { await LoadBackendAsync();if(releaseVerification)await VerifyFirstRunAsync(); };
    }
    private static SolidColorBrush Brush(string hex)=>new(ColorHelper.FromArgb(255,Convert.ToByte(hex.Substring(1,2),16),Convert.ToByte(hex.Substring(3,2),16),Convert.ToByte(hex.Substring(5,2),16)));
    private static T Place<T>(Canvas parent,T element,double x,double y) where T:UIElement
    { Canvas.SetLeft(element,x); Canvas.SetTop(element,y); parent.Children.Add(element); return element; }
    private TextBlock Text(Canvas parent,string value,double x,double y,double size=14,string color="#000000",bool semibold=false,double width=double.NaN,double height=double.NaN)=>
        Place(parent,new TextBlock { Text=value,FontFamily=inter,FontSize=size,FontWeight=semibold?FontWeights.SemiBold:FontWeights.Light,
            Foreground=Brush(color),Width=width,Height=height,TextWrapping=TextWrapping.NoWrap,LineHeight=size*1.35,LineStackingStrategy=LineStackingStrategy.BlockLineHeight },x,y);
    private static Border Surface(Canvas parent,double x,double y,double width,double height,string color,double radius=14)=>
        Place(parent,new Border { Width=width,Height=height,Background=Brush(color),CornerRadius=new CornerRadius(radius) },x,y);
    private ImageSource ImageFor(WallpaperImage image)
    {
        if(!imageCache.TryGetValue(image.Id,out var source)) imageCache[image.Id]=source=new BitmapImage(new Uri(image.Source)) { DecodePixelWidth=640 };
        return source;
    }
    private Border Photo(Canvas parent,WallpaperImage image,double x,double y,double width=283,double height=178)
    { var box=Surface(parent,x,y,width,height,Paper); SetPhoto(box,image); return box; }
    private void SetPhoto(Border box,WallpaperImage image)=>box.Background=new ImageBrush { ImageSource=ImageFor(image),Stretch=Stretch.UniformToFill };
    private static void IconAsset(Canvas parent,string asset,double x,double y,double width,double height)=>
        Place(parent,new Image { Width=width,Height=height,Source=new BitmapImage(new Uri("ms-appx:///Assets/"+asset)),Stretch=Stretch.Fill },x,y);
    private static ControlTemplate ButtonTemplate(string type="Button")=>(ControlTemplate)XamlReader.Load(
        "<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='"+type+"'>"+
        "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}'>"+
        "<ContentPresenter Content='{TemplateBinding Content}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border></ControlTemplate>");
    private Button ActionButton(Canvas parent,string label,double x,double y,Action? click=null,string fill=Navy,string foreground=Paper,double width=143,double height=55,double fontSize=18,bool semibold=true)
    {
        var button=new Button { Width=width,Height=height,Padding=new Thickness(0),BorderThickness=new Thickness(0),
            Background=Brush(fill),CornerRadius=new CornerRadius(14),Template=ButtonTemplate(),UseSystemFocusVisuals=true,
            Content=new TextBlock { Text=label,FontFamily=inter,FontSize=fontSize,FontWeight=semibold?FontWeights.SemiBold:FontWeights.Light,Foreground=Brush(foreground) } };
        AutomationProperties.SetName(button,label);
        button.IsEnabledChanged+=(_,_)=>button.Opacity=button.IsEnabled?1:0.45;
        if(click!=null) button.Click+=(_,_)=>click();
        return Place(parent,button,x,y);
    }
    private ToggleButton Check(Canvas parent,string label,double x,double y,bool isChecked,Action<bool> changed)
    {
        var box=new ToggleButton { Width=18,Height=18,MinWidth=0,MinHeight=0,Padding=new Thickness(0),
            CornerRadius=new CornerRadius(1),Template=ButtonTemplate("ToggleButton"),IsChecked=isChecked,UseSystemFocusVisuals=true };
        AutomationProperties.SetName(box,label); SetCheckVisual(box,isChecked);
        box.Click+=(_,_)=> { SetCheckVisual(box,box.IsChecked==true); changed(box.IsChecked==true); };
        return Place(parent,box,x,y);
    }
    private static void SetCheckVisual(ToggleButton box,bool enabled)
    {
        box.IsChecked=enabled; box.Background=Brush(enabled?Navy:Card);
        box.BorderBrush=Brush(enabled?Navy:"#49454F"); box.BorderThickness=new Thickness(enabled?0:2);
        box.Content=enabled?new FontIcon { Glyph="\uE73E",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=13,Foreground=Brush("#FFFFFF") }:null;
    }
    private void BuildChrome()
    {
        IconAsset(artboard,"Wallpaper.png",17,12,24,24); Text(artboard,"R - Wallpaper Studio V2.1",63,16);
        Caption("\uE921",870,()=>((OverlappedPresenter)AppWindow.Presenter).Minimize()); Caption("\uE922",918,null); Caption("\uE8BB",966,Close);
        Text(artboard,"R - Wallpaper Studio",17,65,24,semibold:true);
        Text(artboard,"Configure multi-wallpaper carousel, single wallpaper selection and location privacy.",17,98);
        wallpaperScroll.Content=wallpaperContent; AutomationProperties.SetName(wallpaperScroll,"Wallpaper settings"); Place(artboard,wallpaperScroll,0,146);
        applyButton=ActionButton(artboard,"Apply",688,1030,()=>_=ApplyBackendAsync()); applyButton.IsEnabled=false;
        applicationStatus=Text(artboard,"Loading wallpaper settings…",17,1050,12,"#49454F",width:650);
        ActionButton(artboard,"Close",852,1030,Close,"#CDCDDA","#000000");
    }
    private void Caption(string glyph,double x,Action? click)
    {
        var button=ActionButton(artboard,"",x,0,click,Paper,"#000000",48,48);
        button.Content=new FontIcon { FontFamily=new FontFamily("Segoe Fluent Icons"),Glyph=glyph,FontSize=10,Foreground=Brush("#000000") };
    }
    private sealed record CardView(Border Preview,TextBlock Path,TextBlock Name,TextBlock Size,TextBlock Date,Button Browse,TextBlock Status);
    private void BuildWallpaperCard(WallpaperRole role,double top,double height,string title)
    {
        bool location=role==WallpaperRole.Location,lockScreen=role==WallpaperRole.LockScreen;
        var card=new Canvas { Width=980,Height=height }; Surface(card,0,0,980,height,Card);
        var photo=Photo(card,document.ForRole(role),17,17); Text(card,title,325,17,18,semibold:true);
        Text(card,location?"Currently active location-based wallpaper.":"Active when your device is outside saved or marked locations, during startup, shutdown, restart,\nlocking, and all other situations.",325,44,width:638,height:42);
        if(location)
        {
            Text(card,"Wallpaper Carousel:",804,17);
            carouselCheckbox=Check(card,"Wallpaper Carousel",945,18,document.CarouselEnabled,v=> { document.CarouselEnabled=v; RefreshLayout(); });
        }
        if(lockScreen)
        {
            Text(card,"Match Home Screen:",325,101);
            matchHomeCheckbox=Check(card,"Match Home Screen",474,102,document.MatchHomeScreen,v=> { document.MatchHomeScreen=v; RefreshCards(); });
            Text(card,"Match Default Wallpaper",532,101);
            matchCheckbox=Check(card,"Match Default Wallpaper",704,102,document.MatchDefault,v=> { document.MatchDefault=v; RefreshCards(); });
        }
        var input=Surface(card,325,140,473,55,Paper);
        var path=new TextBlock { FontFamily=inter,FontSize=14,FontWeight=FontWeights.Light,Foreground=Brush("#000000"),Margin=new Thickness(18,0,0,0),VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.NoWrap };
        input.Child=path;
        var browse=ActionButton(card,"Browse",820,140,()=>_=BrowseAsync(role)); AutomationProperties.SetName(browse,"Browse "+title);
        var name=Text(card,"",17,216,width:260,height:60);
        double y=location||lockScreen?226:216,sx=location?306:297;
        Text(card,"Status:",sx,y); var status=Text(card,location?"Active":"Not Active",sx+50,y,14,location?Active:Inactive);
        var size=Text(card,"",location?548:558,y); var date=Text(card,"",784,y);
        cards[role]=new(photo,path,name,size,date,browse,status); Place(wallpaperContent,card,17,top);
    }
    private void RefreshCards()
    {
        foreach(var (role,view) in cards)
        {
            var image=document.ForRole(role); SetPhoto(view.Preview,image); view.Path.Text=image.DisplayPath; ToolTipService.SetToolTip(view.Path,image.DisplayPath);
            view.Name.Text=role==WallpaperRole.Default?"File name: "+image.Name:"File name:\n"+image.Name; view.Name.TextWrapping=TextWrapping.Wrap;
            view.Size.Text="Size: "+image.SizeText; view.Date.Text="Date modified: "+image.ModifiedText;
            view.Browse.IsEnabled=role switch { WallpaperRole.LockScreen=>!document.MatchDefault,WallpaperRole.Location=>!document.CarouselEnabled,_=>true };
        }
        SetCheckVisual(matchCheckbox,document.MatchDefault);
        SetCheckVisual(matchHomeCheckbox,document.MatchHomeScreen);
    }
    private void BuildCarousel()
    {
        Surface(carouselCard,0,0,980,363,Card); Text(carouselCard,"Carousel List",17,17,18,semibold:true);
        Text(carouselCard,"Manage your carousel wallpapers, add or remove images, and customize carousel settings here.",17,44);
        rotationButton=ActionButton(carouselCard,"",787,10,ShowRotationOptions,Card,"#000000",186,29,14,false);
        AutomationProperties.SetName(rotationButton,"Rotation time");
        var rotationContent=new Canvas { Width=186,Height=29,IsHitTestVisible=false };
        rotationLabel=Text(rotationContent,"Rotation time: 15 min",10,4);
        IconAsset(rotationContent,"Arrow.png",152,1,24,24);rotationButton.Content=rotationContent;
        Surface(carouselCard,17,81,643,247,Paper); carouselScroll.Content=carouselRows; Place(carouselCard,carouselScroll,17,88);
        carouselCard.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,args)=>
        {
            var point=args.GetCurrentPoint(carouselCard);
            if(point.Properties.IsLeftButtonPressed && IsCarouselBlankClick(args.OriginalSource as DependencyObject,point.Position))
            { document.ClearSelection();RefreshCarousel(); }
        }),true);
        carouselPreview=Photo(carouselCard,document.LocationImage,680,81);
        carouselName=Text(carouselCard,"",680,280,width:283); carouselName.TextAlignment=TextAlignment.Center; carouselName.TextTrimming=TextTrimming.CharacterEllipsis;
        addImage=ActionButton(carouselCard,"Add Image",303,292,()=>_=AddImagesAsync(),Paper,Active,80,29,14,false);
        selectFolder=ActionButton(carouselCard,"Select Folder",393,292,()=>_=AddFolderAsync(),Paper,Active,99,29,14,false);
        removeImage=ActionButton(carouselCard,"Remove",503,292,()=> { document.RemoveSelected(); RefreshCarousel(); },Paper,Inactive,68,29,14,false);
        setActive=ActionButton(carouselCard,"Set Active",583,292,()=>_=SetSelectedCarouselActiveAsync(),Paper,Active,72,29,14,false);
        Place(wallpaperContent,carouselCard,17,857);
    }
    private void RefreshCarousel()
    {
        if(draggedImageId!=null)return;
        carouselRowViews.Clear();
        carouselRows.Children.Clear(); carouselRows.Height=Math.Max(194,document.CarouselImages.Count*97);
        for(int i=0;i<document.CarouselImages.Count;i++)
        {
            var item=document.CarouselImages[i]; double y=i*97;
            var row=new Canvas { Width=629,Height=87 }; Surface(row,0,0,629,87,item.Id==document.SelectedImageId?"#DFDFF1":Paper);
            Photo(row,item,5,5,122,77);
            var name=Text(row,"File name:\n"+item.Name,145,11,width:230,height:40); name.TextTrimming=TextTrimming.CharacterEllipsis;
            bool active=backendLoaded?string.Equals(item.DisplayPath,actualWallpaper,StringComparison.OrdinalIgnoreCase):item.Id==document.LocationImage.Id;
            Text(row,"Status:",145,57); Text(row,active?"Active":"Not Active",195,57,14,active?Active:Inactive);
            Text(row,"Date modified: "+item.ModifiedText,387,11); Text(row,"Size: "+item.SizeText,387,57);
            var hit=new Button { Width=629,Height=87,Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=new SolidColorBrush(Colors.Transparent),Template=ButtonTemplate(),Content=row,UseSystemFocusVisuals=true };
            AutomationProperties.SetName(hit,"Select "+item.Name); hit.Click+=(_,_)=>SelectCarouselImage(item.Id);
            var container=new Canvas { Width=629,Height=87,Background=new SolidColorBrush(Colors.Transparent) };
            Place(container,hit,0,0);Place(carouselRows,container,7,y);
            AttachCarouselGrip(container,item.Id);
            IconAsset(carouselRows,"Divider.png",19,y+92,606,0.25);
        }
        if(document.CarouselImages.Count==0) Text(carouselRows,"Add an image to start your carousel.",17,20);
        var detail=document.SelectedImage??document.LocationImage; SetPhoto(carouselPreview,detail); carouselName.Text="File name: "+detail.Name;
        bool selected=document.SelectedImage!=null;
        removeImage.IsEnabled=selected;setActive.IsEnabled=selected&&!settingActive;
        ((TextBlock)removeImage.Content).Foreground=Brush(selected?Inactive:"#49454F");
        ((TextBlock)setActive.Content).Foreground=Brush(selected?Active:"#49454F");
        removeImage.Opacity=1;setActive.Opacity=1;
    }
    private bool IsCarouselBlankClick(DependencyObject? source,Windows.Foundation.Point point)
    {
        if(point.X<17 || point.X>660 || point.Y<81 || point.Y>328)return false;
        for(var current=source;current!=null && current!=carouselCard;current=VisualTreeHelper.GetParent(current))
            if(current is ButtonBase or ScrollBar or CarouselGrip)return false;
        return true;
    }
    private void SelectCarouselImage(string id) { document.SelectImage(id); RefreshCarousel(); }
    private void RefreshLayout()
    {
        if(!document.CarouselEnabled)FinishCarouselDrag(true);
        cards[WallpaperRole.Location].Browse.IsEnabled=!document.CarouselEnabled;
        SetCheckVisual(carouselCheckbox,document.CarouselEnabled); carouselCard.Visibility=document.CarouselEnabled?Visibility.Visible:Visibility.Collapsed;
        double top=document.CarouselEnabled?1236:857; Canvas.SetTop(locationsCard,top); wallpaperContent.Height=top+271;
    }
    private FileOpenPicker WallpaperPicker(WallpaperRole role)=>ImagePicker(role!=WallpaperRole.Location || !document.CarouselEnabled);
    private FileOpenPicker ImagePicker(bool allowVideo=false)
    {
        var picker=new FileOpenPicker { SuggestedStartLocation=PickerLocationId.PicturesLibrary,ViewMode=PickerViewMode.Thumbnail };
        foreach(var extension in new[]{".jpg",".jpeg",".png",".bmp",".gif",".tif",".tiff",".webp"}) picker.FileTypeFilter.Add(extension);
        if(allowVideo)foreach(var extension in WallpaperMedia.VideoExtensions)picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this)); return picker;
    }
    private async Task<WallpaperImage> ReadImageAsync(StorageFile file)
    {
        if(WallpaperMedia.IsVideo(file.Path))
        {
            string poster=await WallpaperMedia.EnsurePosterAsync(file.Path);
            var videoFile=new FileInfo(file.Path);
            return new(file.Path.ToUpperInvariant(),file.Name,file.Path,new Uri(poster).AbsoluteUri,$"{videoFile.Length/1048576.0:0.##}MB",videoFile.LastWriteTime.ToString("dd-MM-yyyy"));
        }
        using var stream=await file.OpenReadAsync(); var decoder=await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        if(decoder.PixelWidth==0 || decoder.PixelHeight==0) throw new InvalidDataException("This file has no image pixels.");
        stream.Seek(0); var source=new BitmapImage { DecodePixelWidth=640 }; await source.SetSourceAsync(stream);
        var properties=await file.GetBasicPropertiesAsync(); string id=file.Path.ToUpperInvariant(); imageCache[id]=source;
        return new(id,file.Name,file.Path,new Uri(file.Path).AbsoluteUri,
            properties.Size>=1048576?$"{properties.Size/1048576.0:0.##}MB":$"{properties.Size/1024.0:0.##}KB",properties.DateModified.ToLocalTime().ToString("dd-MM-yyyy"));
    }
    private async Task BrowseAsync(WallpaperRole role)
    {
        if(pickerOpen || role==WallpaperRole.LockScreen && document.MatchDefault || role==WallpaperRole.Location && document.CarouselEnabled) return; pickerOpen=true;
        try { var file=await WallpaperPicker(role).PickSingleFileAsync(); if(file==null) return; document.ChooseImage(role,await ReadImageAsync(file)); RefreshCards(); RefreshCarousel(); }
        catch(Exception ex) { await ShowErrorAsync("Unable to open image",ex.Message); } finally { pickerOpen=false; }
    }
    private async Task AddImagesAsync()
    {
        if(pickerOpen) return; pickerOpen=true;
        try { foreach(var file in await ImagePicker().PickMultipleFilesAsync()) document.AddImage(await ReadImageAsync(file)); RefreshCarousel(); }
        catch(Exception ex) { await ShowErrorAsync("Unable to add image",ex.Message); } finally { pickerOpen=false; }
    }
    private async Task AddFolderAsync()
    {
        if(pickerOpen) return; pickerOpen=true;
        try
        {
            var picker=new FolderPicker { SuggestedStartLocation=PickerLocationId.PicturesLibrary }; picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var folder=await picker.PickSingleFolderAsync(); if(folder==null) return; int failed=0;
            foreach(var file in await folder.GetFilesAsync())
            {
                if(!new[]{".jpg",".jpeg",".png",".bmp",".gif",".tif",".tiff",".webp"}.Contains(file.FileType.ToLowerInvariant())) continue;
                try { document.AddImage(await ReadImageAsync(file)); } catch { failed++; }
            }
            RefreshCarousel(); if(failed>0) await ShowErrorAsync("Some images could not be opened",$"Added the readable images. Skipped {failed} unreadable file(s).");
        }
        catch(Exception ex) { await ShowErrorAsync("Unable to open folder",ex.Message); } finally { pickerOpen=false; }
    }
    private async Task ShowErrorAsync(string title,string message)
    {
        if(closed) return;
        await new ContentDialog { XamlRoot=artboard.XamlRoot,Title=title,Content=message,CloseButtonText="OK",RequestedTheme=ElementTheme.Light }.ShowAsync();
    }
}
