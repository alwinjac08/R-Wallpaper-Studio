using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace WallpaperStudio;

public sealed partial class MainWindow
{
    private string UiPreferencesPath=>Path.Combine(DataDirectory,"ui-preferences.json");
    private DateTime appliedNoticeUntil;
    private ContentDialog? applyConfirmationDialog;
    private ToggleButton? suppressConfirmationCheckbox;
    private bool ShouldShowApplyConfirmation(string warning)=>!string.IsNullOrWhiteSpace(warning)||!UiPreferences.Load(UiPreferencesPath).SuppressApplyConfirmation;

    private async Task ShowApplyConfirmationAsync(string warning)
    {
        appliedNoticeUntil=DateTime.UtcNow.AddSeconds(6);
        RefreshBackendStatus();
        if(closed || !ShouldShowApplyConfirmation(warning))return;
        bool hasWarning=!string.IsNullOrWhiteSpace(warning);
        var body=new Canvas { Width=440,Height=hasWarning?300:226 };
        Text(body,"Settings applied",0,0,24,semibold:true);
        var message=Text(body,hasWarning?"Your settings have been saved."+warning:
            "Your wallpaper settings are up to date.\nYou can continue making changes or close Settings.",0,42,14,"#49454F",width:440,height:hasWarning?170:50);
        message.TextWrapping=TextWrapping.Wrap;
        var dialog=new ContentDialog { XamlRoot=artboard.XamlRoot,Content=body,Background=Brush(Paper),
            Foreground=Brush("#000000"),FontFamily=inter,CornerRadius=new CornerRadius(14),
            BorderBrush=Brush("#DFDFF1"),BorderThickness=new Thickness(1),RequestedTheme=ElementTheme.Light };
        dialog.Resources["ContentDialogBackground"]=Brush(Paper);
        dialog.Resources["ContentDialogTopOverlay"]=Brush(Paper);
        dialog.Resources["OverlayCornerRadius"]=new CornerRadius(14);
        dialog.Resources["ContentDialogPadding"]=new Thickness(24);
        dialog.Resources["ContentDialogMinWidth"]=488d;
        dialog.Resources["ContentDialogMaxWidth"]=488d;
        ToggleButton? dontShow=null;
        if(!hasWarning)
        {
            Surface(body,0,105,440,50,Card);
            dontShow=Check(body,"Don't show again",16,121,false,_=>{});
            var label=Text(body,"Don’t show again",48,120,14,"#49454F");
            label.Tapped+=(_,_)=>SetCheckVisual(dontShow,dontShow.IsChecked!=true);
        }
        ActionButton(body,"Done",297,hasWarning?245:178,dialog.Hide,width:143,height:48);
        applyConfirmationDialog=dialog;suppressConfirmationCheckbox=dontShow;
        try
        {
            await dialog.ShowAsync();
            if(dontShow?.IsChecked==true)
                await new UiPreferences { SuppressApplyConfirmation=true }.SaveAsync(UiPreferencesPath);
        }
        finally { applyConfirmationDialog=null;suppressConfirmationCheckbox=null; }
    }
}
