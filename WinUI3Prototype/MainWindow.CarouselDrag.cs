using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;

namespace WallpaperStudio;

public sealed partial class MainWindow
{
    private sealed class CarouselGrip : ContentControl
    {
        public CarouselGrip() { ProtectedCursor=InputSystemCursor.Create(InputSystemCursorShape.SizeAll); }
    }
    private readonly Dictionary<string,(Canvas Row,CarouselGrip Grip)> carouselRowViews=new();
    private string? draggedImageId;
    private string[] dragOriginalOrder=[];
    private CarouselGrip? capturedGrip;
    private uint dragPointerId;
    private double dragGrabOffset,dragViewportY;
    private DispatcherTimer? dragScrollTimer;

    private void AttachCarouselGrip(Canvas row,string id)
    {
        var grip=new CarouselGrip { Width=32,Height=40,Opacity=0,IsTabStop=true,
            Background=Brush(Paper),Template=ButtonTemplate("ContentControl"),
            Content=new Image { Width=24,Height=24,Source=new SvgImageSource(new Uri("ms-appx:///Assets/DragHandle.svg")),IsHitTestVisible=false } };
        // Icon origin is (617, 121) in the Figma card, exactly 24 x 24.
        grip.Background=new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Place(row,grip,589,25);carouselRowViews[id]=(row,grip);
        AutomationProperties.SetName(grip,"Reorder "+document.CarouselImages.First(image=>image.Id==id).Name);
        ToolTipService.SetToolTip(grip,"Drag to reorder · Up/Down to move · Esc to cancel");
        bool hovered=false;
        row.PointerEntered+=(_,_)=>{ hovered=true;grip.Opacity=1; };
        row.PointerExited+=(_,_)=>{ hovered=false;if(draggedImageId!=id&&grip.FocusState==FocusState.Unfocused)grip.Opacity=0; };
        grip.GotFocus+=(_,_)=>grip.Opacity=1;
        grip.LostFocus+=(_,_)=>{ if(!hovered&&draggedImageId!=id)grip.Opacity=0; };
        grip.PointerPressed+=(_,args)=>
        {
            var point=args.GetCurrentPoint(carouselScroll);
            if(!point.Properties.IsLeftButtonPressed || draggedImageId!=null)return;
            if(!grip.CapturePointer(args.Pointer))return;
            capturedGrip=grip;dragPointerId=args.Pointer.PointerId;
            BeginCarouselDrag(id,point.Position.Y);
            grip.Focus(FocusState.Programmatic);args.Handled=true;
            dragScrollTimer??=CreateDragScrollTimer();dragScrollTimer.Start();
        };
        grip.PointerMoved+=(_,args)=>
        {
            if(draggedImageId==null||args.Pointer.PointerId!=dragPointerId)return;
            dragViewportY=args.GetCurrentPoint(carouselScroll).Position.Y;UpdateCarouselDrag();args.Handled=true;
        };
        grip.PointerReleased+=(_,args)=>
        {
            if(draggedImageId==null||args.Pointer.PointerId!=dragPointerId)return;
            dragViewportY=args.GetCurrentPoint(carouselScroll).Position.Y;UpdateCarouselDrag();FinishCarouselDrag(false);args.Handled=true;
        };
        grip.PointerCanceled+=(_,_)=>FinishCarouselDrag(true);
        grip.PointerCaptureLost+=(_,_)=>{ if(capturedGrip==grip)FinishCarouselDrag(true); };
        grip.KeyDown+=(_,args)=>
        {
            if(args.Key==Windows.System.VirtualKey.Escape&&draggedImageId!=null) { FinishCarouselDrag(true);args.Handled=true; }
            else if(draggedImageId==null && args.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)
            {
                int index=document.CarouselImages.FindIndex(image=>image.Id==id);
                document.MoveCarouselImage(id,index+(args.Key==Windows.System.VirtualKey.Up?-1:1));RefreshCarousel();
                carouselRowViews[id].Grip.Focus(FocusState.Keyboard);args.Handled=true;
            }
        };
    }
    private void BeginCarouselDrag(string id,double viewportY)
    {
        draggedImageId=id;dragOriginalOrder=document.CarouselImages.Select(image=>image.Id).ToArray();
        dragViewportY=viewportY;
        dragGrabOffset=viewportY+carouselScroll.VerticalOffset-Canvas.GetTop(carouselRowViews[id].Row);
        Canvas.SetZIndex(carouselRowViews[id].Row,10);carouselRowViews[id].Grip.Opacity=1;
    }
    private void UpdateCarouselDrag()
    {
        if(draggedImageId is not { } id)return;
        double top=Math.Clamp(dragViewportY+carouselScroll.VerticalOffset-dragGrabOffset,0,Math.Max(0,carouselRows.Height-87));
        int destination=Math.Clamp((int)Math.Floor((top+43.5)/97),0,document.CarouselImages.Count-1);
        document.MoveCarouselImage(id,destination);
        for(int index=0;index<document.CarouselImages.Count;index++)
        {
            string itemId=document.CarouselImages[index].Id;
            Canvas.SetTop(carouselRowViews[itemId].Row,itemId==id?top:index*97);
        }
    }
    private DispatcherTimer CreateDragScrollTimer()
    {
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(16) };
        timer.Tick+=(_,_)=>
        {
            if(draggedImageId==null)return;
            double height=carouselScroll.ActualHeight;
            double delta=dragViewportY<28?-Math.Min(12,(28-dragViewportY)/3):dragViewportY>height-28?Math.Min(12,(dragViewportY-height+28)/3):0;
            if(delta!=0) { carouselScroll.ChangeView(null,Math.Clamp(carouselScroll.VerticalOffset+delta,0,carouselScroll.ScrollableHeight),null,true);UpdateCarouselDrag(); }
        };
        return timer;
    }
    private void FinishCarouselDrag(bool cancel)
    {
        if(draggedImageId==null)return;
        dragScrollTimer?.Stop();
        if(cancel)for(int index=0;index<dragOriginalOrder.Length;index++)document.MoveCarouselImage(dragOriginalOrder[index],index);
        draggedImageId=null;
        var grip=capturedGrip;capturedGrip=null;grip?.ReleasePointerCaptures();
        if(!closed)RefreshCarousel();
    }
}
