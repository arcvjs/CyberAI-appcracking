using System.Windows;
using System.Windows.Controls;
using Vapor.Licensing;

namespace Vapor.Client.Views;

public partial class StoreView : UserControl
{
    private readonly BackendClient _backend;
    private readonly MainWindow _window;
    public StoreView(BackendClient backend,MainWindow window){InitializeComponent();_backend=backend;_window=window;}
    private void OnBuy(object sender,RoutedEventArgs e)
    {
        var dialog=new TrialOverWindow(_backend,_window.RefreshAndGetSession(),_window){Title="Vapor Store"};
        dialog.Owner=_window;dialog.ShowDialog();
    }
}
