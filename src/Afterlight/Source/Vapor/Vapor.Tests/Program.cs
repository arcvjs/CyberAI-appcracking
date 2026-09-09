using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Vapor.Client;
using Vapor.Licensing;
using Vaporworks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

if(args.FirstOrDefault()=="--probe")
{
    var decision=await VaporAPI.InitAsync();
    File.WriteAllText(args[1],JsonSerializer.Serialize(new{decision.Ok,decision.Code},VaporProtocol.Json));
    return 0;
}
string root=Path.GetFullPath(args.FirstOrDefault()??throw new ArgumentException("Pass the exhibition package path"));
string data=Path.Combine(Path.GetTempPath(),"afterlight-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
foreach(var source in Directory.GetFiles(Path.Combine(root,"Presenter","seed-data")))File.Copy(source,Path.Combine(data,Path.GetFileName(source)));
using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"exhibition.json")));
string url=manifest.RootElement.GetProperty("serverUrl").GetString()!;
using var http=new HttpClient{BaseAddress=new Uri(url),Timeout=TimeSpan.FromSeconds(3)};
http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",File.ReadAllText(Path.Combine(data,"admin-key.txt")).Trim());
string account=File.ReadAllText(Path.Combine(data,"demo-account.txt")).Trim();
Process? service=null;List<string> results=[];
async Task StartService()
{
    var info=new ProcessStartInfo(Path.Combine(root,"Service","Vapor.Backend.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=data,RedirectStandardOutput=true,RedirectStandardError=true};
    info.Environment["VAPOR_DATA"]=data;info.Environment["VAPOR_URL"]=url;
    service=Process.Start(info)!;service.BeginOutputReadLine();service.BeginErrorReadLine();
    for(int i=0;i<30;i++){await Task.Delay(100);try{if((await http.GetAsync("/health")).IsSuccessStatusCode)return;}catch(HttpRequestException){}}
    throw new Exception("Reference service did not start");
}
async Task SetLicense(string state)
{
    string op=state=="owned"?"grant":state=="revoke"?"revoke":"trial";
    object body=state=="owned"?new{license="owned"}:(object)new{minutes=state=="expired"?0:2};
    using var response=await http.PostAsJsonAsync($"/admin/licenses/{account}/2937/{op}",body);response.EnsureSuccessStatusCode();
}
async Task CheckProbe(PipeServer? pipe,string expected,string label)
{
    string output=Path.Combine(data,Guid.NewGuid()+".json");
    using var child=pipe!=null?pipe.Launch(Environment.ProcessPath!,"--probe",output):Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,ArgumentList={"--probe",output}})!;
    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    using var result=JsonDocument.Parse(File.ReadAllText(output));string actual=result.RootElement.GetProperty("code").GetString()!;
    if(actual!=expected)throw new Exception(label+": expected "+expected+", got "+actual);
    results.Add("PASS  "+label);
}
void RenderUi(BackendClient backend,CachedSession session)
{
    Exception? failure=null;
    var thread=new Thread(()=>
    {
        try
        {
            var app=new Vapor.Client.App();app.InitializeComponent();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window=new Vapor.Client.Views.MainWindow(false);window.Show();window.OnSignedIn(session,serve:false);
            string folder=Path.Combine(root,"previews");Directory.CreateDirectory(folder);
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(800)};
            int step=0;Vapor.Client.Views.TrialOverWindow? modal=null;
            void Capture(Window target,string name)
            {
                target.UpdateLayout();var view=(FrameworkElement)target.Content;
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth),(int)Math.Ceiling(view.ActualHeight),96,96,PixelFormats.Pbgra32);
                bitmap.Render(view);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using var file=File.Create(Path.Combine(folder,name+".png"));png.Save(file);
            }
            timer.Tick+=(_,_)=>
            {
                try
                {
                    if(step++==0){Capture(window,"vapor-library");modal=new(backend,session,window){Owner=window};modal.Show();}
                    else{Capture(modal!,"trial-ended");timer.Stop();modal!.Close();window.Close();Dispatcher.CurrentDispatcher.InvokeShutdown();}
                }
                catch(Exception e){failure=e;timer.Stop();window.Close();Dispatcher.CurrentDispatcher.InvokeShutdown();}
            };
            timer.Start();Dispatcher.Run();
        }
        catch(Exception e){failure=e;}
    });
    thread.SetApartmentState(ApartmentState.STA);thread.Start();
    if(!thread.Join(10000))throw new Exception("UI rendering timed out");
    if(failure!=null)throw failure;
    results.Add("PASS  Native WPF library and trial dialog rendered to images");
}
try
{
    await StartService();
    using var backend=new BackendClient();
    var login=await backend.SignInAsync("festivalgoer","vapor");
    var session=new CachedSession{Account=login.Account,SteamId=login.SteamId,Nickname=login.Nickname,SessionToken=login.SessionToken};
    using var pipe=new PipeServer(backend,()=>session,()=>{});pipe.Start();
    await CheckProbe(null,"no_client","SDK rejects launch without client context");
    await SetLicense("expired");await CheckProbe(pipe,"trial_ended","Issuer denies expired trial through real IPC");
    using(var deniedGame=pipe.Launch(Path.Combine(root,"Afterlight.exe"),"--verify","--gate-shot"))
    {
        await deniedGame.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if(deniedGame.ExitCode!=0||!File.Exists(Path.Combine(root,"trial-gate.png")))throw new Exception("Published executable failed to render its license gate");
        if(Directory.Exists(Path.Combine(root,"playtest")))throw new Exception("Published --verify bypassed the license gate");
        results.Add("PASS  Published --verify cannot bypass DRM; real game renders its trial gate");
    }
    RenderUi(backend,session);
    await SetLicense("owned");await CheckProbe(pipe,"ok","Owned license: child PID, challenge MAC and publisher signature accepted");
    using(var realGame=pipe.Launch(Path.Combine(root,"Afterlight.exe")))
    {
        try
        {
            for(int i=0;i<40;i++){await Task.Delay(100);realGame.Refresh();if(realGame.HasExited||realGame.MainWindowHandle!=IntPtr.Zero)break;}
            await Task.Delay(700);realGame.Refresh();
            if(realGame.HasExited||!realGame.MainWindowTitle.StartsWith("AFTERLIGHT |"))throw new Exception("Published native game did not reach its menu");
            results.Add("PASS  Self-contained game starts a native OpenGL window with a real license");
        }
        finally{if(!realGame.HasExited){realGame.Kill();await realGame.WaitForExitAsync();}}
    }
    if(session.LastProof==null)throw new Exception("Signed ownership was not cached");
    var savedProof=session.LastProof;
    await SetLicense("revoke");await CheckProbe(pipe,"not_owned","Revocation rejects old cached rights while issuer is available");
    if(session.LastProof!=null)throw new Exception("Revocation failed to invalidate cached proof");
    await SetLicense("owned");await CheckProbe(pipe,"ok","Ownership can be restored without restarting client");
    service!.Kill();await service.WaitForExitAsync();service.Dispose();service=null;
    await CheckProbe(pipe,"ok","Cached signed ownership works with reference service stopped");
    session.LastProof=savedProof with{Signature=Convert.ToBase64String(new byte[64])};
    await CheckProbe(pipe,"bad_ticket","Game independently rejects forged offline proof");
    session.LastProof=savedProof;session.SteamId="another-account";
    await CheckProbe(pipe,"wrong_account","Offline ownership remains bound to the signed-in account");
    session.SteamId=login.SteamId;session.LastProof=null;
    await CheckProbe(pipe,"server_unavailable","Offline mode without a valid cache fails closed");
    File.WriteAllLines(Path.Combine(root,"integration-verification.txt"),results);Console.WriteLine(string.Join("\n",results));return 0;
}
catch(Exception e){Console.Error.WriteLine(e);File.WriteAllLines(Path.Combine(root,"integration-verification.txt"),results.Append("FAIL "+e.Message));return 1;}
finally{if(service is {HasExited:false})service.Kill();service?.Dispose();}
