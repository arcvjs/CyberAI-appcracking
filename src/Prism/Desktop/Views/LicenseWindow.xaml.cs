using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Prism.Controls;
using Prism.Licensing;
using Prism.Licensing.Client;

namespace Prism.Views;

public partial class LicenseWindow : PrismDialog
{
    private readonly LicenseClient _client;
    private bool _initialized, _visible, _switching, _alternative;
    private string EnteredKey=>_visible?VisibleKey.Text:SecretKey.Password;
    private static Brush Brush(string color)=>(Brush)new BrushConverter().ConvertFromString(color)!;
    public LicenseWindow(LicenseClient client)
    {
        _client=client;
        InitializeComponent();
        _initialized=true;
        DevelopmentBadge.Visibility=client.Trust.ReferenceServer?Visibility.Visible:Visibility.Collapsed;
        DeviceNameLabel.Text=client.DeviceName;
        DeviceNameLabel.ToolTip=client.DeviceName;
        DeviceIdLabel.Text=client.DeviceId;
        ServerLabel.Text=client.Trust.ServerUrl;
        Loaded+=(_,_)=>{if(ActivationForm.Visibility==Visibility.Visible)SecretKey.Focus();};
        Render();
    }
    private void Render()
    {
        var status=_client.Status;
        bool active=status.CanEdit;
        PageHeading.Text=active?"Your workspace is ready":"Activate your workspace";
        PageDescription.Text=active?"Editing is enabled on this device.":"Activate Prism to unlock your editing tools.";
        StatusTitle.Text=active?status.Title:status.Title=="Activation required"?"Ready to activate":status.Title;
        StatusDescription.Text=status.Title=="Activation required"?"You can still view, save, and export your existing files.":status.Message;
        StatusCard.Background=Brush(active?"#26332F":"#2E2938");
        StatusCard.BorderBrush=Brush(active?"#3A5549":"#453A54");
        StatusIconBackground.Background=Brush(active?"#354E40":"#40354F");
        StatusIcon.Kind=active?"check":"lock";
        StatusIcon.Color=Brush(active?"#B9D7BD":"#C9B4F2");
        StatusTitle.Foreground=Brush(active?"#C4DFCA":"#DDD0F3");
        ActivationForm.Visibility=!active||status.IsTrial||_alternative?Visibility.Visible:Visibility.Collapsed;
        ActiveActions.Visibility=active&&!status.IsTrial?Visibility.Visible:Visibility.Collapsed;
        ActivateLabel.Text=IsBusy?"Connecting…":active?"Update activation":"Activate Prism";
        RefreshLabel.Text=active?"Check subscription":"Already activated? Check subscription";
        FooterStatus.Text=status.IsTrial?status.Title:active?"License verified":"Subscription & devices";
        FooterStatus.Visibility=FooterDot.Visibility=!active&&_client.Trust.ReferenceServer?Visibility.Collapsed:Visibility.Visible;
        FooterDot.Fill=Brush(active?"#A5C9AD":"#A697BC");
        OfflineValue.Text=status.Claims is {} claims?"Until "+claims.OfflineUntil.LocalDateTime.ToString("dd MMM"):"Up to 7 days";
        LeaseLabel.Text=status.Claims is {} lease?$"Subscription ends {lease.SubscriptionExpiresAt.LocalDateTime:d}.\nOffline access ends {lease.OfflineUntil.LocalDateTime:g}.":"An online check grants up to seven days of offline access.";
        if(status.IsTrial) {
            ActivateLabel.Text="Activate subscription";
            OfflineValue.Text="Available during trial";
            LeaseLabel.Text=status.Message;
        }
    }
    private void ShowFeedback(string message,bool error,bool inForm)
    {
        FeedbackCard.Visibility=inForm?Visibility.Visible:Visibility.Collapsed;
        GeneralFeedback.Visibility=inForm?Visibility.Collapsed:Visibility.Visible;
        if(inForm) {
            FeedbackText.Text=message;
            FeedbackCard.Background=Brush(error?"#372A2D":"#2D2A35");
            FeedbackCard.BorderBrush=Brush(error?"#694047":"#494054");
            FeedbackText.Foreground=Brush(error?"#F0B8B8":"#C9BCDC");
        } else { GeneralFeedbackText.Text=message;GeneralFeedbackText.Foreground=Brush(error?"#F0B8B8":"#CBC0DB"); }
    }
    private async Task Run(Func<Task> action,bool activation=false)
    {
        if(IsBusy)return;
        bool form=activation&&ActivationForm.Visibility==Visibility.Visible;
        IsBusy=true;
        ActivateButton.IsEnabled=RefreshButton.IsEnabled=DeactivateButton.IsEnabled=DoneButton.IsEnabled=SecretKey.IsEnabled=VisibleKey.IsEnabled=RevealButton.IsEnabled=false;
        ShowFeedback("Connecting to your licensing server…",false,form);
        ActivateLabel.Text=activation?"Activating…":"Activate Prism";
        try {
            await action();
            SecretKey.Clear();VisibleKey.Clear();
            _alternative=false;
            FeedbackCard.Visibility=GeneralFeedback.Visibility=Visibility.Collapsed;
            if(!_client.Status.CanEdit)ShowFeedback(_client.Status.Message,false,false);
        }
        catch(Exception error)when(error is LicenseException or HttpRequestException or TaskCanceledException or IOException or System.ComponentModel.Win32Exception) {
            ShowFeedback(error is HttpRequestException or TaskCanceledException?"Couldn't connect. Check your internet connection and that the licensing server is running, then try again.":error.Message,true,form);
        }
        finally {
            IsBusy=false;
            ActivateButton.IsEnabled=RefreshButton.IsEnabled=DeactivateButton.IsEnabled=DoneButton.IsEnabled=SecretKey.IsEnabled=VisibleKey.IsEnabled=RevealButton.IsEnabled=true;
            Render();
        }
    }
    private async void Activate_Click(object sender,RoutedEventArgs e)
    {
        var key=EnteredKey.Trim();
        if(key.Length==0){ShowFeedback("Enter your subscription key to activate Prism.",true,true);KeyBorder.BorderBrush=Brush("#D8909C");if(_visible)VisibleKey.Focus();else SecretKey.Focus();return;}
        await Run(()=>_client.ActivateAsync(key),true);
    }
    private async void Refresh_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{await _client.CheckAsync(true);});
    private async void Deactivate_Click(object sender,RoutedEventArgs e)=>await Run(()=>_client.DeactivateAsync());
    private void Done_Click(object sender,RoutedEventArgs e)=>Close();
    private void AnotherKey_Click(object sender,RoutedEventArgs e){_alternative=true;Render();SecretKey.Focus();}
    private void Reveal_Click(object sender,RoutedEventArgs e)
    {
        _switching=true;
        if(_visible){SecretKey.Password=VisibleKey.Text;VisibleKey.Clear();}
        else VisibleKey.Text=SecretKey.Password;
        _visible=!_visible;
        SecretKey.Visibility=_visible?Visibility.Collapsed:Visibility.Visible;
        VisibleKey.Visibility=_visible?Visibility.Visible:Visibility.Collapsed;
        RevealIcon.Kind=_visible?"eye-off":"eye";
        RevealButton.ToolTip=_visible?"Hide license key":"Show license key";
        System.Windows.Automation.AutomationProperties.SetName(RevealButton,_visible?"Hide license key":"Show license key");
        _switching=false;
        if(_visible){VisibleKey.Focus();VisibleKey.CaretIndex=VisibleKey.Text.Length;}else SecretKey.Focus();
        UpdatePlaceholder();
    }
    private void UpdatePlaceholder(){if(!_initialized||_switching)return;KeyPlaceholder.Visibility=EnteredKey.Length==0?Visibility.Visible:Visibility.Collapsed;}
    private void Key_Changed(object sender,RoutedEventArgs e)=>UpdatePlaceholder();
    private void VisibleKey_Changed(object sender,TextChangedEventArgs e)=>UpdatePlaceholder();
    private void Key_Focused(object sender,KeyboardFocusChangedEventArgs e){if(_initialized)KeyBorder.BorderBrush=Brush("#B5A0EC");}
    private void Key_Unfocused(object sender,KeyboardFocusChangedEventArgs e){if(_initialized)KeyBorder.BorderBrush=Brush("#595062");}
    private void Key_PreviewKeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;Activate_Click(ActivateButton,new RoutedEventArgs());}}
}
