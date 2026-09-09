using System.IO;
using System.Net.Http;
using System.Windows;
using Vapor.Client.Views;

namespace Vapor.Client.Views;

public partial class MainWindow : Window
{
    private readonly BackendClient _backend=new();
    private readonly VaporStore _store;
    private readonly PipeServer _pipe;
    private CachedSession _session=new();
    private readonly LibraryView _library;
    private readonly LoginView _login;
    private readonly StoreView _storePage;
    private readonly CommunityView _community;
    private bool pendingLaunch;
    private bool initialized;

    public MainWindow():this(true){}
    public MainWindow(bool initializeSession)
    {
        InitializeComponent();
        string issuer=Vapor.Licensing.VaporProtocol.Hash(_backend.Trust.PublicKey)[..16];
        _store=new VaporStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Vapor",issuer,"session.bin"));
        _pipe=new PipeServer(_backend,()=>_session,SaveSession);
        _login=new LoginView(_backend);
        _library=new LibraryView(_backend,this);
        _storePage=new StoreView(_backend,this);
        _community=new CommunityView();
        _login.SignedIn+=session=>OnSignedIn(session);
        Shell.Content=_login;
        if(initializeSession)Loaded+=async(_,_)=>await InitializeAsync();
        try{var cached=_store.Load();if(cached!=null&&!string.IsNullOrEmpty(cached.Account))_session=cached;}catch(Exception error)when(error is IOException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception){StatusLeft.Text="Saved sign-in could not be read. Sign in again.";}
    }
    private async Task InitializeAsync()
    {
        try
        {
            await LocalService.EnsureRunningAsync(_backend);
            if(!string.IsNullOrEmpty(_session.Account))await ResumeSessionAsync(_session);
            else if(LocalService.Exhibition)
            {
                var login=await _backend.SignInAsync("festivalgoer","vapor");
                OnSignedIn(new CachedSession{Account=login.Account,Password="vapor",SteamId=login.SteamId,Nickname=login.Nickname,SessionToken=login.SessionToken});
            }
            initialized=true;UpdateStatus();
            if(pendingLaunch&&!string.IsNullOrEmpty(_session.SessionToken)){pendingLaunch=false;_library.LaunchRequested();}
        }
        catch(Exception e)when(e is Vapor.Licensing.VaporException or IOException or HttpRequestException or TaskCanceledException){StatusLeft.Text="Unable to connect to Vapor. Restart the client and try again.";}
    }
    public void RequestLaunch(){if(initialized&&!string.IsNullOrEmpty(_session.SessionToken))_library.LaunchRequested();else pendingLaunch=true;}
    private async System.Threading.Tasks.Task ResumeSessionAsync(CachedSession cached)
    {
        try
        {
            var fresh=await _backend.SignInAsync(cached.Account,cached.Password);
            _session=new CachedSession{Account=cached.Account,Password=cached.Password,SteamId=fresh.SteamId,Nickname=fresh.Nickname,SessionToken=fresh.SessionToken,LastTicket=cached.LastTicket,LastProof=cached.LastProof,LastSeen=cached.LastSeen};
            OnSignedIn(_session);
        }
        catch(Vapor.Licensing.VaporException)
        {
            Shell.Content=_login;AccountLabel.Text="Not signed in";StatusLeft.Text="Saved sign-in expired. Sign in again.";
        }
        catch(Exception error)when(error is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            // Backend unreachable: Steam-style offline start with the cached identity.
            _session=cached;OnSignedIn(cached,offline:true);
        }
        UpdateStatus();
    }
    public void OnSignedIn(CachedSession session,bool offline=false,bool serve=true)
    {
        _session=session;
        AccountLabel.Text=(session.Account=="festivalgoer"?"Player":session.Nickname)+(offline?"  (offline)":"");
        SignOutButton.Visibility=Visibility.Visible;
        Shell.Content=_library;
        SelectNavigation(0);
        if(serve)try{_store.Save(session);}catch(IOException){StatusLeft.Text="Could not save the sign-in on this PC.";}
        if(serve)_pipe.Start();
        StatusLeft.Text=offline?"Offline — reconnect to verify your licenses":"Connected to the Vapor service";
        StatusRight.Text="AFTERLIGHT  /  SINGLE PLAYER";
        _=RefreshLibraryAsync();
    }
    public System.Threading.Tasks.Task RefreshLibraryAsync()=>_library.RefreshAsync(_session);
    private void OnSignOut(object sender,RoutedEventArgs e)
    {
        _session=new CachedSession();
        try{_store.Save(_session);}catch(IOException){}
        AccountLabel.Text="Not signed in";SignOutButton.Visibility=Visibility.Collapsed;
        Shell.Content=_login;StatusLeft.Text="Signed out.";
    }
    public void ShowStore()=>ShowTab(1);
    public void ShowLibrary()=>ShowTab(0);
    public void ShowCommunity()=>ShowTab(2);
    private void OnLibrary(object sender,RoutedEventArgs e)=>ShowLibrary();
    private void OnStore(object sender,RoutedEventArgs e)=>ShowStore();
    private void OnCommunity(object sender,RoutedEventArgs e)=>ShowCommunity();
    private void ShowTab(int index)
    {
        Shell.Content=index==0?_library:index==1?_storePage:_community;
        SelectNavigation(index);
        if(index==0)_=RefreshLibraryAsync();
    }
    private void SelectNavigation(int index)
    {
        LibraryTab.Tag=index==0?"selected":null;
        StoreTab.Tag=index==1?"selected":null;
        CommunityTab.Tag=index==2?"selected":null;
        _library.SetNav(index);
    }
    public void UpdateStatus(){StatusLeft.Text=_backend.Online?"Vapor is up to date":"Offline — using cached licenses";}
    public CachedSession RefreshAndGetSession()=>_session;
    public System.Diagnostics.Process LaunchGame(string path)=>_pipe.Launch(path);
    private void SaveSession(){try{_store.Save(_session);}catch(Exception e)when(e is IOException or System.ComponentModel.Win32Exception){} }
    private void OnClosing(object? sender,System.ComponentModel.CancelEventArgs e)=>_pipe.Dispose();
}


