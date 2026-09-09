using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Vapor.Licensing;

namespace Vapor.Client.Views;

public partial class LibraryView : UserControl
{
    private readonly BackendClient _backend;
    private readonly MainWindow _window;
    private Ownership? _ownership;
    private Process? _game;
    private bool launching;
    public LibraryView(BackendClient backend,MainWindow window){InitializeComponent();_backend=backend;_window=window;SetNav(0);}
    public void SetNav(int index)
    {
        Highlight(NavStore,index==1);Highlight(NavLibrary,index==0);Highlight(NavCommunity,index==2);
        void Highlight(TextBlock tab,bool selected){tab.Foreground=selected?FindResource("SteamBlue") as Brush:FindResource("SteamTextDim") as Brush;tab.FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;}
    }
    private void OnNavStore(object sender,MouseButtonEventArgs e)=>_window.ShowStore();
    private void OnNavLibrary(object sender,MouseButtonEventArgs e)=>_window.ShowLibrary();
    private void OnNavCommunity(object sender,MouseButtonEventArgs e)=>_window.ShowCommunity();
    private void OnGameClick(object sender,MouseButtonEventArgs e)=>SetNav(0);
    public async Task RefreshAsync(CachedSession session)
    {
        if(string.IsNullOrEmpty(session.SessionToken))return;
        try { _ownership=await _backend.GetOwnershipAsync(session.SessionToken,VaporProtocol.AfterlightAppId); }
        catch(VaporException error){LicenseTitle.Text="License unavailable";LicenseDetail.Text=error.Message;PlayButton.IsEnabled=false;return;}
        if(_ownership.License=="unknown" && session.LastProof!=null)
        {
            try {
                var claims=VaporProtocol.Verify(session.LastProof,_backend.Trust.PublicKey,VaporProtocol.AfterlightAppId,_backend.DeviceId,DateTimeOffset.UtcNow);
                if(claims.SteamId==session.SteamId)_ownership=new(claims.AppId,claims.Product,claims.License,claims.TrialEndsAt,true);
            }catch(VaporException){}
        }
        bool ended=_ownership is { License:"trial" }&&_ownership.TrialEndsAt<=DateTimeOffset.UtcNow;
        bool running=_game!=null&&!_game.HasExited;
        PlayButton.Content=running?"▶  RUNNING":"▶  PLAY";
        PlayButton.IsEnabled=!running;
        if(running){LicenseTitle.Text="AFTERLIGHT is running";LicenseDetail.Text="Your game is running. Return to its window to continue.";GameItemStatus.Text="Running";ActivityText.Text="Playing AFTERLIGHT";return;}
        switch(_ownership.License,ended)
        {
            case ("trial",false):
                LicenseTitle.Text="Free trial";
                LicenseDetail.Text=$"Your free trial of AFTERLIGHT ends {_ownership.TrialEndsAt.LocalDateTime:ddd, d MMM HH:mm}.";
                GameItemStatus.Text="Free trial";ActivityText.Text="Free trial — "+(_ownership.TrialEndsAt-DateTimeOffset.UtcNow).ToString(@"hh\:mm")+" remaining.";
                break;
            case ("trial",true):
                LicenseTitle.Text="Your free trial has ended";
                LicenseDetail.Text=$"The trial period ended {_ownership.TrialEndsAt.LocalDateTime:ddd, d MMM HH:mm}. Purchase AFTERLIGHT to keep playing.";
                GameItemStatus.Text="Trial ended";ActivityText.Text="Free trial ended — purchase required to continue.";
                break;
            case ("owned",_):
                LicenseTitle.Text="Purchased";
                LicenseDetail.Text="Ready to play. Your full game includes all three waves and offline single player.";
                GameItemStatus.Text="Ready to play";ActivityText.Text="Ready for your next session.";
                break;
            case ("free",_):
                LicenseTitle.Text="Free to play";
                LicenseDetail.Text="AFTERLIGHT is free on this account.";
                GameItemStatus.Text="Ready to play";ActivityText.Text="Free to play.";
                break;
            default:
                LicenseTitle.Text="Not in your library";
                LicenseDetail.Text="Purchase AFTERLIGHT on the Vapor store to add it to your library.";
                GameItemStatus.Text="Not owned";ActivityText.Text="—";
                break;
        }
        StoreLink.Visibility=_ownership.License=="owned"?Visibility.Collapsed:Visibility.Visible;
    }
    private void OnStorePage(object sender,RoutedEventArgs e)=>_window.ShowStore();
    public void LaunchRequested()=>OnPlay(this,new RoutedEventArgs());
    private async void OnPlay(object sender,RoutedEventArgs e)
    {
        if(launching || _game is {HasExited:false})return;
        launching=true;
        try
        {
        var session=((MainWindow)Application.Current.MainWindow!).RefreshAndGetSession();
        await RefreshAsync(session);
        bool ended=_ownership is { License:"trial" }&&_ownership.TrialEndsAt<=DateTimeOffset.UtcNow;
        if(_ownership is null||!_ownership.Owned||ended){var dialog=new TrialOverWindow(_backend,session,_window);dialog.Owner=_window;dialog.ShowDialog();await RefreshAsync(session);return;}
        var exe=FindGameExecutable();
        if(exe==null){MessageBox.Show(_window,"Vapor could not find Afterlight.exe.\n\nKeep Afterlight.exe and Vapor.exe together in the App folder.","Vapor",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        try
        {
            _game=_window.LaunchGame(exe);
            PlayButton.Content="▶  RUNNING";PlayButton.IsEnabled=false;
            ActivityText.Text="Playing AFTERLIGHT";
            _game!.Exited+=(_,_)=>Dispatcher.Invoke(()=>{PlayButton.Content="▶  PLAY";PlayButton.IsEnabled=true;ActivityText.Text="Last session ended.";});
            _game.EnableRaisingEvents=true;
        }
        catch(Exception error)when(error is System.ComponentModel.Win32Exception or InvalidOperationException){MessageBox.Show(_window,"Vapor could not launch the game: "+error.Message,"Vapor",MessageBoxButton.OK,MessageBoxImage.Error);}
        }
        finally{launching=false;}
    }
    private static string? FindGameExecutable()
    {
        string[] probes=
        [
            Path.Combine(AppContext.BaseDirectory,"Afterlight.exe"),
            Path.Combine(AppContext.BaseDirectory,"Afterlight","Afterlight.exe"),
            Path.Combine(AppContext.BaseDirectory,"..","Afterlight","Afterlight.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","..","..","Afterlight","dist","Afterlight-Festival","Afterlight.exe")),
        ];
        return probes.FirstOrDefault(File.Exists);
    }
}

