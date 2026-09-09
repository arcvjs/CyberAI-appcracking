using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Vapor.Licensing;

namespace Vapor.Client.Views;

public partial class LoginView : UserControl
{
    public event Action<CachedSession>? SignedIn;
    private readonly BackendClient _backend;
    public LoginView(BackendClient backend){InitializeComponent();_backend=backend;PasswordBox.Password="vapor";}
    private async void OnSignIn(object sender,RoutedEventArgs e)
    {
        ErrorText.Text="";SignInButton.IsEnabled=false;
        try
        {
            var response=await _backend.SignInAsync(AccountBox.Text.Trim(),PasswordBox.Password);
            var session=new CachedSession{Account=AccountBox.Text.Trim(),Password=PasswordBox.Password,SteamId=response.SteamId,Nickname=response.Nickname,SessionToken=response.SessionToken,LastSeen=DateTimeOffset.UtcNow};
            if(RememberBox.IsChecked!=true)session.Password="";
            SignedIn?.Invoke(session);
        }
        catch(VaporException error){ErrorText.Text=error.Message;}
        catch(Exception error)when(error is HttpRequestException or TaskCanceledException){ErrorText.Text="Could not reach the Vapor service. Is the backend running?";}
        finally{SignInButton.IsEnabled=true;}
    }
}
