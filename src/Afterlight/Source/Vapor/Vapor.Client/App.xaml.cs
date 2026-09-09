using System.Windows;
using System.IO.Pipes;
using System.IO;
using Vapor.Licensing;

namespace Vapor.Client;

public partial class App : Application
{
    Mutex? single;
    readonly CancellationTokenSource stopping=new();
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string suffix=VaporProtocol.LocalDeviceId()[..16];
        single=new Mutex(true,"Local\\Vapor-"+suffix,out bool first);
        if(!first)
        {
            try { using var timeout=new CancellationTokenSource(2500);using var pipe=new NamedPipeClientStream(".","vapor-ui-"+suffix,PipeDirection.Out,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);await pipe.ConnectAsync(timeout.Token);await IpcWire.Write(pipe,e.Args.Contains("--launch"),timeout.Token); }catch{}
            Shutdown();return;
        }
        var window=new Views.MainWindow();MainWindow=window;
        if(e.Args.Contains("--launch"))window.RequestLaunch();
        window.Show();
        _=ListenUi(suffix,window);
    }
    async Task ListenUi(string suffix,Views.MainWindow window)
    {
        while(!stopping.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream("vapor-ui-"+suffix,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);timeout.CancelAfter(2000);
                bool launch=await IpcWire.Read<bool>(pipe,timeout.Token);
                if(window.WindowState==WindowState.Minimized)window.WindowState=WindowState.Normal;
                window.Activate();window.ShowLibrary();if(launch)window.RequestLaunch();
            }
            catch(Exception ex)when(ex is IOException or OperationCanceledException){if(stopping.IsCancellationRequested)break;}
        }
    }
    protected override void OnExit(ExitEventArgs e){stopping.Cancel();LocalService.StopOwned();single?.Dispose();base.OnExit(e);}
}
