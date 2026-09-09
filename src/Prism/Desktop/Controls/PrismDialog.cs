using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;

namespace Prism.Controls;

/// <summary>A shared, DPI-aware dialog frame with a scrolling body and an optional fixed footer.</summary>
public class PrismDialog : Window
{
    public static readonly DependencyProperty FooterProperty=DependencyProperty.Register(nameof(Footer),typeof(object),typeof(PrismDialog));
    public static readonly DependencyProperty IsBusyProperty=DependencyProperty.Register(nameof(IsBusy),typeof(bool),typeof(PrismDialog),new PropertyMetadata(false));
    public object? Footer { get=>(object?)GetValue(FooterProperty); set=>SetValue(FooterProperty,value); }
    public bool IsBusy { get=>(bool)GetValue(IsBusyProperty); set=>SetValue(IsBusyProperty,value); }

    public PrismDialog()
    {
        SetResourceReference(StyleProperty,typeof(PrismDialog));
        WindowStyle=WindowStyle.None;
        ResizeMode=ResizeMode.NoResize;
        SizeToContent=SizeToContent.Height;
        WindowStartupLocation=WindowStartupLocation.CenterOwner;
        ShowInTaskbar=false;
        UseLayoutRounding=true;
        SnapsToDevicePixels=true;
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,(_,_)=>Close()));
        TextOptions.SetTextFormattingMode(this,System.Windows.Media.TextFormattingMode.Display);
        MaxHeight=Math.Max(320,SystemParameters.WorkArea.Height-40);
        MaxWidth=Math.Max(320,SystemParameters.WorkArea.Width-40);
        WindowChrome.SetWindowChrome(this,new WindowChrome { CaptionHeight=46,ResizeBorderThickness=new Thickness(0),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(10) });
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle=new WindowInteropHelper(this).Handle;
        int dark=1,rounded=2;
        _=DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));
        _=DwmSetWindowAttribute(handle,33,ref rounded,sizeof(int));
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if(IsBusy)e.Cancel=true;
        base.OnClosing(e);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
}
