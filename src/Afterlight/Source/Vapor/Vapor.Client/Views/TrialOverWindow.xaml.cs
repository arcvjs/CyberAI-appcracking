using System.Net.Http;
using System.Windows;
using Vapor.Licensing;

namespace Vapor.Client.Views;

public partial class TrialOverWindow : Window
{
    private readonly BackendClient _backend;
    private readonly CachedSession _session;
    private readonly MainWindow _window;
    public bool Purchased;
    public TrialOverWindow(BackendClient backend,CachedSession session,MainWindow window)
    {
        InitializeComponent();
        _backend=backend;_session=session;_window=window;
        if(_session.SessionToken.Length==0)DetailText.Text="You are not signed in. Sign in to Vapor to purchase or check your trial.";
    }
    private void OnCancel(object sender,RoutedEventArgs e)=>Close();
    private async void OnPurchase(object sender,RoutedEventArgs e)
    {
        PurchaseButton.IsEnabled=false;CancelButton.IsEnabled=false;
        StatusText.Text="Contacting the Vapor store…";
        try
        {
            var ownership=await _backend.PurchaseAsync(_session.SessionToken,VaporProtocol.AfterlightAppId);
            Purchased=true;
            StatusText.Text="Purchase complete! AFTERLIGHT is now yours.";
            await Task.Delay(900);
            Close();
            _window.ShowLibrary();
        }
        catch(VaporException error){StatusText.Text=error.Message;PurchaseButton.IsEnabled=true;CancelButton.IsEnabled=true;}
        catch(Exception error)when(error is HttpRequestException or TaskCanceledException){StatusText.Text="The Vapor store is unreachable. Try again.";PurchaseButton.IsEnabled=true;CancelButton.IsEnabled=true;}
    }
}
