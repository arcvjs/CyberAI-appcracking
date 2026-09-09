using Prism.Licensing.Client;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Prism;

public partial class MainWindow
{
    private LicenseClient _licensing=null!;
    private DispatcherTimer? _licenseTimer;
    private bool _showingLicense;
    private void InitializeLicensing()
    {
        _licensing=new LicenseClient();
        Loaded+=async(_,_)=>{await RefreshLicense(true);};
        _licenseTimer=new DispatcherTimer{Interval=TimeSpan.FromMinutes(1)};
        _licenseTimer.Tick+=async(_,_)=>await RefreshLicense();
        _licenseTimer.Start();
        Closed+=(_,_)=>_licenseTimer.Stop();
    }
    private async Task RefreshLicense(bool force=false)
    {
        try{await _licensing.CheckAsync(force);UpdateLicenseBadge();}
        catch(Exception ex)when(ex is System.IO.IOException or HttpRequestException or TaskCanceledException or System.ComponentModel.Win32Exception){LicenseBadge.Content="License needs attention";LicenseBadge.ToolTip=ex.Message;}
    }
    private void UpdateLicenseBadge()
    {
        LicenseBadge.Content=_licensing.Status.CanEdit?(_licensing.Status.IsTrial?"●  Free trial":_licensing.Status.Title=="Working offline"?"●  Offline license":"●  Licensed"):"Activate Prism";
        LicenseBadge.Foreground=B(_licensing.Status.CanEdit?"#B2D3BA":"#D7C5FF");
        LicenseBadge.ToolTip=_licensing.Status.Message;
        if(_ready)UpdateProperties();
    }
    private bool RequireLicense()
    {
        if(_licensing.CanEditNow())return true;
        UpdateLicenseBadge();
        if(!_showingLicense)ShowLicenseDialog();
        return _licensing.CanEditNow();
    }
    private void License_Click(object sender,RoutedEventArgs e)=>ShowLicenseDialog();
    private void ShowLicenseDialog()
    {
        if(_showingLicense)return;
        _showingLicense=true;
        try { new Prism.Views.LicenseWindow(_licensing) { Owner=this }.ShowDialog(); }
        finally { _showingLicense=false;UpdateLicenseBadge(); }
    }
}
