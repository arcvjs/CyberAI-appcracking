using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace AfterlightDemo;
public static class Program
{
    [STAThread] public static int Main(string[] args)
    {
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","App"));
        if(args.Contains("--self-test"))return Checks.Run(root,args.Last());
        var app=new Application();var window=new DemoWindow(new Controller(root));
        if(args.Contains("--preview"))window.Loaded+=(_,_)=>{ var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};timer.Tick+=(_,_)=>{timer.Stop();
            window.UpdateLayout();var view=(FrameworkElement)window.Content;
            var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(args.Last()))png.Save(f);window.Close();
        };timer.Start();};
        return app.Run(window);
    }
}
public sealed class DemoWindow:Window
{
    readonly Controller controller;
    readonly TextBlock state=new(),detail=new();
    readonly TextBox log=new();
    readonly Button apply,restore;
    static SolidColorBrush Brush(string color)=>(SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
    static TextBlock Text(string value,double size,string color)=>new(){Text=value,FontSize=size,Foreground=Brush(color),TextWrapping=TextWrapping.Wrap};
    public DemoWindow(Controller control)
    {
        controller=control;Title="Afterlight | SDK demonstration";Width=1000;Height=720;MinWidth=850;MinHeight=650;
        WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=Brush("#171D25");FontFamily=new FontFamily("Segoe UI");
        var panel=new Grid();Content=new Border{Background=Brush("#171D25"),Padding=new Thickness(36),Child=panel};
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})panel.RowDefinitions.Add(new(){Height=height});
        void Add(UIElement element,int row){Grid.SetRow(element,row);panel.Children.Add(element);}
        var header=new StackPanel();header.Children.Add(Text("AFTERLIGHT   /   SDK LAB",12,"#66C0F4"));
        header.Children.Add(new TextBlock{Text="One component. Different behavior.",FontSize=32,Foreground=Brush("#FFFFFF"),Margin=new Thickness(0,12,0,8)});
        header.Children.Add(Text("Show the original restriction, replace the SDK, then restore it.",15,"#A9B4C0"));Add(header,0);
        var card=new Border{Background=Brush("#252D39"),Padding=new Thickness(24),Margin=new Thickness(0,26,0,24)};
        var status=new StackPanel();status.Children.Add(Text("CURRENT STATE",11,"#8F98A0"));state.FontSize=28;state.Margin=new Thickness(0,6,0,8);status.Children.Add(state);detail.FontSize=14;detail.Foreground=Brush("#C6D4DF");detail.TextWrapping=TextWrapping.Wrap;status.Children.Add(detail);card.Child=status;Add(card,1);
        var actions=new Grid();for(int i=0;i<3;i++)actions.ColumnDefinitions.Add(new());
        Button Button(string title,string color,int column,Action action){var b=new Button{Content=title,FontSize=15,FontWeight=FontWeights.SemiBold,Background=Brush(color),Foreground=Brush("#FFFFFF"),BorderThickness=new Thickness(0),Padding=new Thickness(12),Height=58,Margin=new Thickness(column==0?0:10,0,0,0),Cursor=System.Windows.Input.Cursors.Hand};b.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='Surface' Background='{TemplateBinding Background}' CornerRadius='3'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Surface' Property='Opacity' Value='0.4'/><Setter Property='Foreground' Value='#A9B4C0'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Surface' Property='Opacity' Value='0.85'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Surface' Property='BorderBrush' Value='White'/><Setter TargetName='Surface' Property='BorderThickness' Value='2'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");b.Click+=(_,_)=>Execute(action);Grid.SetColumn(b,column);actions.Children.Add(b);return b;}
        apply=Button("1  Apply replacement","#237BBB",0,()=>{controller.Apply();Append("Original SDK backed up and verified. Replacement installed.");});
        Button("2  Launch game","#709B15",1,()=>{using var game=controller.Launch();Append("Game launched. Protected mode opens Vapor; replacement mode opens Afterlight directly.");});
        restore=Button("3  Restore protection","#45546B",2,()=>{controller.Restore();Append("Original SDK restored from its verified backup.");});Add(actions,2);
        var instruction=Text("Close the game and Vapor before Apply or Restore. Launch also works in the original state for your before/after comparison.",12,"#A9B4C0");instruction.Margin=new Thickness(0,14,0,20);Add(instruction,3);
        log.Background=Brush("#11161D");log.Foreground=Brush("#AFC9DC");log.FontFamily=new FontFamily("Consolas");log.FontSize=12;log.Padding=new Thickness(16);log.BorderThickness=new Thickness(0);log.IsReadOnly=true;log.TextWrapping=TextWrapping.Wrap;log.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;Add(log,4);
        var footer=Text("Only the Vapor SDK changes. Game code and account ownership stay the same.\nThe replacement supplies local API responses; it does not create a signed license.",12,"#8F98A0");footer.Margin=new Thickness(0,18,0,0);Add(footer,5);
        Append("Ready. Select Launch game to show the protected version first.");Refresh();
        Activated+=(_,_)=>Refresh();
    }
    void Append(string message){log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");log.ScrollToEnd();}
    void Execute(Action action){try{action();}catch(Exception error){Append(error.Message);}Refresh();}
    void Refresh()
    {
        try{string current=controller.State();bool active=current=="Replacement active";state.Text=current;state.Foreground=Brush(active?"#A4D65E":"#66C0F4");detail.Text=active?"Afterlight can start directly. The replacement answers the game's SDK calls locally.":"Original SDK installed. Afterlight opens Vapor and checks your play license.";apply.IsEnabled=!active;restore.IsEnabled=active;}
        catch(Exception error){state.Text="Check installation";state.Foreground=Brush("#FFB980");detail.Text=error.Message;apply.IsEnabled=false;restore.IsEnabled=false;}
    }
}



