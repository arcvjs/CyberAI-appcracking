using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Prism.Controls;
using Prism.Models;
using Prism.Licensing;
using Prism.Licensing.Client;
using Prism.LicenseServer;

internal static class Program
{
    private static int _passed;
    private static void Check(bool condition,string name){if(!condition)throw new Exception("FAILED: "+name);_passed++;Console.WriteLine("PASS: "+name);}
    private static void Reject(Action action,string code){try{action();throw new Exception("Expected rejection: "+code);}catch(LicenseException e){Check(e.Code==code,"Reject "+code);}}
    [STAThread] private static int Main(string[] args)
    {
        try {
            if(args.Contains("--check-scroll")){CheckScrolling();return 0;}
            if(args.Contains("--render-ui")){RenderDialogs();return 0;}
            LicensingTests();TrialTests();EditorTests();
            if(args.Contains("--integration"))IntegrationTests().GetAwaiter().GetResult();
            Console.WriteLine($"All {_passed} checks passed.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static void TrialTests()
    {
        var directory=Path.Combine(Path.GetTempPath(),"PrismTrialChecks-"+Guid.NewGuid().ToString("N"));
        var path=Path.Combine(directory,"trial.json");
        try {
            var now=DateTimeOffset.UtcNow;
            var store=new TrialStore(path);
            Check(store.Evaluate(now).CanEdit,"First launch starts a free trial");
            Check(!store.Evaluate(now.AddDays(14)).CanEdit,"Trial expires at its deadline");
            var config=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            config["expiresAtUtc"]=now.AddYears(1).ToString("O");File.WriteAllText(path,config.ToJsonString());
            Check(store.Evaluate(now.AddDays(15)).CanEdit,"Editing local expiry extends the demo trial without restarting");
            config["expiresAtUtc"]=now.AddYears(2).ToString("O");File.WriteAllText(path,config.ToJsonString());
            Check(new TrialStore(path).Evaluate(now.AddYears(1)).CanEdit,"Trial expiry can be extended repeatedly");
            File.WriteAllText(path,"broken json");
            Check(!store.Evaluate(now).CanEdit,"Malformed trial config does not crash or unlock editing");
        } finally {if(File.Exists(path))File.Delete(path);if(Directory.Exists(directory))Directory.Delete(directory);}
    }
    private static void CheckScrolling()
    {
        var app=new Prism.App();app.InitializeComponent();
        var viewer=new System.Windows.Controls.ScrollViewer {
            HorizontalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto,
            Content=new System.Windows.Controls.Border {Width=2000,Height=1500}
        };
        viewer.Measure(new Size(800,600));viewer.Arrange(new Rect(0,0,800,600));viewer.UpdateLayout();
        var bar=(System.Windows.Controls.Primitives.ScrollBar)viewer.Template.FindName("PART_HorizontalScrollBar",viewer);
        bar.Style=(Style)app.FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar));
        viewer.Measure(new Size(800,600));viewer.Arrange(new Rect(0,0,800,600));viewer.UpdateLayout();bar.ApplyTemplate();
        var track=(System.Windows.Controls.Primitives.Track)bar.Template.FindName("PART_Track",bar);
        Console.WriteLine($"Horizontal bar: {bar.Visibility}, {bar.ActualWidth} x {bar.ActualHeight}");
        Check(bar.Visibility==Visibility.Visible&&bar.ActualWidth>700&&bar.ActualHeight>=9&&bar.ActualHeight<=20,"Horizontal scrollbar spans the viewport");
        Check(!track.IsDirectionReversed,"Horizontal thumb moves in the correct direction");
        Check(track.IncreaseRepeatButton.Command==System.Windows.Controls.Primitives.ScrollBar.PageRightCommand,"Horizontal track pages right");
        viewer.ScrollToHorizontalOffset(350);viewer.UpdateLayout();
        Check(viewer.HorizontalOffset==350,"Canvas can scroll horizontally");
        Console.WriteLine($"All {_passed} scrolling checks passed.");
    }
    private static void RenderDialogs()
    {
        // Render our own WPF visual tree; no desktop capture or input automation is involved.
        var application=new Prism.App();application.InitializeComponent();
        var output=Path.GetFullPath(Environment.GetEnvironmentVariable("PRISM_UI_OUTPUT")??Path.Combine(AppContext.BaseDirectory,"ui-previews"));Directory.CreateDirectory(output);
        using var client=new LicenseClient();
        foreach(var (name,width,maximumHeight) in new[]{("activation",554d,720d),("activation-compact",450d,530d)}) {
            var window=new Prism.Views.LicenseWindow(client);
            window.ApplyTemplate();
            var root=(FrameworkElement)VisualTreeHelper.GetChild(window,0);
            root.Measure(new Size(width,double.PositiveInfinity));
            double height=Math.Min(maximumHeight,root.DesiredSize.Height);
            root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(height),96,96,PixelFormats.Pbgra32);bitmap.Render(root);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(output,name+".png")))encoder.Save(file);
            var body=(FrameworkElement)window.Template.FindName("DialogBody",window);
            var footer=(FrameworkElement)window.Template.FindName("DialogFooter",window);
            var bodyOrigin=body.TranslatePoint(new Point(),root);var footerOrigin=footer.TranslatePoint(new Point(),root);
            Check(bodyOrigin.X>=27&&body.ActualWidth<=width-54,"Dialog content has real left and right insets at "+width+" px");
            Check(footerOrigin.Y+footer.ActualHeight<=height+1,"Footer remains within the viewport at "+maximumHeight+" px");
            Console.WriteLine($"Rendered {name}: {width} × {height}");
            window.Close();
        }
    }
    private static void LicensingTests()
    {
        var now=DateTimeOffset.UtcNow;using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);string publicKey=key.ExportSubjectPublicKeyInfoPem();string device=new('A',64);
        var claims=new LicenseClaims{SubscriptionId="test-subscription",DeviceId=device,Customer="Test",IssuedAt=now,RenewAfter=now.AddHours(12),OfflineUntil=now.AddDays(7),SubscriptionExpiresAt=now.AddDays(30)};
        var signed=LicenseProtocol.Sign(claims,key);
        Check(LicenseProtocol.Verify(signed,publicKey,device,now).Customer=="Test","Valid signed license");
        Check(LicenseProtocol.Verify(signed,publicKey,device,now.AddDays(7).AddSeconds(-1)).DeviceId==device,"Offline access until exact deadline");
        Reject(()=>LicenseProtocol.Verify(signed,publicKey,device,now.AddDays(7)),"offline_expired");
        Reject(()=>LicenseProtocol.Verify(signed,publicKey,new string('B',64),now),"wrong_device");
        var tampered=signed with{Payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(signed.Payload)).Replace("Test","Forged")))};
        Reject(()=>LicenseProtocol.Verify(tampered,publicKey,device,now),"invalid_signature");
        using var attacker=ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Reject(()=>LicenseProtocol.Verify(LicenseProtocol.Sign(claims,attacker),publicKey,device,now),"invalid_signature");
        Reject(()=>LicenseProtocol.Verify(signed,publicKey,device,now,now.AddHours(1)),"clock_changed");
        Reject(()=>LicenseProtocol.Verify(signed,publicKey,device,now.AddDays(-1)),"clock_changed");
        Reject(()=>LicenseProtocol.Verify(LicenseProtocol.Sign(claims with{Product="other"},key),publicKey,device,now),"wrong_device");
        Reject(()=>LicenseProtocol.Verify(LicenseProtocol.Sign(claims with{OfflineUntil=now.AddDays(8)},key),publicKey,device,now),"invalid_lease");
        Reject(()=>LicenseProtocol.Verify(LicenseProtocol.Sign(claims with{OfflineUntil=now.AddDays(1),SubscriptionExpiresAt=now.AddDays(1)},key),publicKey,device,now.AddDays(1)),"subscription_expired");
        var bytes=Encoding.UTF8.GetBytes("private-refresh-token-for-test");var protectedBytes=WindowsLicenseStore.Protect(bytes);
        Check(!bytes.SequenceEqual(protectedBytes)&&WindowsLicenseStore.Unprotect(protectedBytes).SequenceEqual(bytes),"Windows DPAPI protects activation secrets");
        var path=Path.Combine(Path.GetTempPath(),"prism-license-tests-"+Guid.NewGuid().ToString("N"));var trust=LicenseAuthority.Initialize(path,"http://127.0.0.1:47831");
        var clock=now;
        using(var authority=new LicenseAuthority(path,()=>clock)) {
            var created=authority.Create(new("Test customer",30,2));
            var first=authority.Activate(new(created.Key,device,"Workstation"));
            Check(LicenseProtocol.Verify(first.License,trust.PublicKey,device,now).OfflineUntil==now.AddDays(7),"Server issues seven-day lease");
            var again=authority.Activate(new(created.Key,device,"Workstation"));
            authority.Activate(new(created.Key,new string('B',64),"Laptop"));
            Reject(()=>authority.Activate(new(created.Key,new string('C',64),"Third PC")),"device_limit");
            Reject(()=>authority.Renew(new(first.RefreshToken,device)),"invalid_activation");
            Check(authority.Renew(new(again.RefreshToken,device)).License!=null,"Reactivate same device without consuming extra seat");
            Reject(()=>authority.Activate(new("invalid",device,"PC")),"invalid_key");
            authority.SetRevoked(created.Subscription.Id,true);
            Reject(()=>authority.Renew(new(again.RefreshToken,device)),"revoked");
            authority.SetRevoked(created.Subscription.Id,false);
            authority.Deactivate(new(again.RefreshToken,device));
            Reject(()=>authority.Renew(new(again.RefreshToken,device)),"invalid_activation");
            var replacement=authority.Activate(new(created.Key,new string('C',64),"Replacement PC"));
            Check(replacement.License!=null,"Deactivation frees a device slot");
            var shortLicense=authority.Create(new("One day",1,1));var shortActivation=authority.Activate(new(shortLicense.Key,new string('D',64),"Short PC"));
            Check(LicenseProtocol.Verify(shortActivation.License,trust.PublicKey,new string('D',64),now).OfflineUntil==now.AddDays(1),"Offline lease never exceeds subscription expiry");
            clock=now.AddDays(2);Reject(()=>authority.Renew(new(shortActivation.RefreshToken,new string('D',64))),"subscription_expired");
            authority.Extend(shortLicense.Subscription.Id,30);
            Check(authority.Renew(new(shortActivation.RefreshToken,new string('D',64))).License!=null,"Subscription renewal restores activation");
            Reject(()=>authority.Extend(shortLicense.Subscription.Id,0),"invalid_request");
        }
        using(var reopened=new LicenseAuthority(path,()=>clock))Check(JsonSerializer.Serialize(reopened.List()).Contains("Test customer"),"Subscriptions survive a server restart");
    }
    private static byte[] Pixel(BitmapSource image,int x,int y){var pixel=new byte[4];image.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel;}
    private static void EditorTests()
    {
        var text=new Layer{Kind="text",Text="Natural text",FontSize=50};text.SizeToText();Check(Math.Abs(text.Width-text.Format().WidthIncludingTrailingWhitespace)<.01,"Text uses natural glyph proportions");
        var rotated=new Layer{X=50,Y=50,Width=100,Height=20,Rotation=90};Check(rotated.Contains(new Point(100,100))&&!rotated.Contains(new Point(50,50)),"Rotated layer hit testing");
        var paint=new Layer{Kind="paint",Width=100,Height=100,ContentWidth=100,ContentHeight=100};
        paint.Strokes.Add(new(){Points=[new Point(10,50),new Point(90,50)],Size=20,Color="#FF0000"});
        paint.Strokes.Add(new(){Points=[new Point(50,10),new Point(50,90)],Size=20,IsEraser=true});
        var doc=new EditorDocument{Width=100,Height=100,Layers=[paint]};var surface=new DocumentSurface{Document=doc};surface.Refresh();
        var rendered=surface.RenderBitmap();Check(Pixel(rendered,25,50)[3]==255&&Pixel(rendered,50,50)[3]==0,"Eraser removes only intersecting pixels");
        paint.Strokes.Add(new(){Points=[new Point(50,50)],Size=8,Color="#00FF00"});
        Check(Pixel(surface.RenderBitmap(),50,50)[1]==255,"Can repaint a previously erased area");
        paint.Strokes.Clear();paint.Strokes.Add(new(){Points=[new Point(0,50),new Point(100,50)],Size=30,Color="#0000FF",Clip=new Rect(40,40,20,20)});
        var clipped=surface.RenderBitmap();Check(Pixel(clipped,30,50)[3]==0&&Pixel(clipped,50,50)[3]==255&&Pixel(clipped,50,65)[3]==0,"Brush stroke is clipped at selection boundaries");
        paint.Width=200;doc.Width=200;Check(Pixel(surface.RenderBitmap(),100,50)[3]==255,"Paint content scales with layer transforms");
        var serialized=doc.Serialize();var restored=EditorDocument.Deserialize(serialized);Check(restored.Layers[0].Strokes[0].Clip==paint.Strokes[0].Clip&&restored.Layers[0].Strokes[0].Points.SequenceEqual(paint.Strokes[0].Points),"Project roundtrip preserves brush selection and points");
        var history=new EditHistory();history.Reset(doc);Check(history.Matches(doc)&&!history.IsDirty,"Initial history is clean");
        paint.X=5;history.Push(doc,"Move");Check(history.IsDirty&&history.CanUndo,"Mutation creates an undo entry");
        var undo=history.Restore(0);Check(undo.Layers[0].X==0&&!history.IsDirty,"Undo restores content and saved status");
        var redo=history.Restore(1);history.MarkSaved();Check(redo.Layers[0].X==5&&!history.IsDirty,"Redo and saved revision");
        history.Restore(0);doc.Layers[0].X=20;history.Push(doc,"Branch");Check(!history.CanRedo&&history.IsDirty,"Branching after undo discards redo history");
        var imagePayload=new string('x',100);doc.Layers[0].ImageData=imagePayload;var copy=doc.Clone();Check(ReferenceEquals(doc.Layers[0].ImageData,copy.Layers[0].ImageData),"History shares immutable image data");
        copy.Layers[0].Strokes[0].Points.Add(new Point(1,1));Check(copy.Layers[0].Strokes[0].Points.Count!=doc.Layers[0].Strokes[0].Points.Count,"History isolates mutable brush geometry");
        var empty=new DocumentSurface{Document=new EditorDocument{Width=20,Height=20,Layers=[]}};
        Check(Pixel(empty.RenderBitmap(),0,0)[3]==0&&Pixel(empty.RenderBitmap(false),0,0)[3]==255,"PNG transparency and JPEG white backdrop");
        Check(empty.RenderBitmap(scale:.5).PixelWidth==10,"Export scaling uses requested resolution");
        try{EditorDocument.Deserialize("{\"Width\":9000,\"Height\":9000,\"Layers\":[]}");throw new Exception("Invalid document accepted");}catch(InvalidDataException){Check(true,"Oversized document rejected");}
    }
    private static async Task IntegrationTests()
    {
        var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..",".."));
        string data=Environment.GetEnvironmentVariable("PRISM_TEST_SERVER_DATA")??Path.Combine(root,"Prism.LicenseServer",".local-licensing");
        using var http=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:47831/"),Timeout=TimeSpan.FromSeconds(5)};
        Check((await http.GetAsync("health")).IsSuccessStatusCode,"Local licensing server health");
        Check((await http.GetAsync("admin/subscriptions")).StatusCode==HttpStatusCode.Unauthorized,"Admin endpoints require authentication");
        http.DefaultRequestHeaders.Authorization=new("Bearer",File.ReadAllText(Path.Combine(data,"admin-key.txt")).Trim());
        var response=await http.PostAsJsonAsync("admin/subscriptions",new CreateSubscriptionRequest("Automated integration check",1,1),LicenseProtocol.Json);response.EnsureSuccessStatusCode();
        var created=JsonDocument.Parse(await response.Content.ReadAsStringAsync());string key=created.RootElement.GetProperty("licenseKey").GetString()!,id=created.RootElement.GetProperty("id").GetString()!;
        Check(!string.IsNullOrEmpty(key),"Admin can issue subscription keys");
        http.DefaultRequestHeaders.Authorization=null;
        var activation=await http.PostAsJsonAsync("api/v1/activate",new ActivationRequest(key,new string('E',64),"Integration test"),LicenseProtocol.Json);activation.EnsureSuccessStatusCode();var lease=(await activation.Content.ReadFromJsonAsync<ActivationResponse>(LicenseProtocol.Json))!;
        var trust=JsonSerializer.Deserialize<LicenseTrust>(File.ReadAllText(Path.Combine(data,"client-trust.json")),LicenseProtocol.Json)!;
        Check(LicenseProtocol.Verify(lease.License,trust.PublicKey,new string('E',64),DateTimeOffset.UtcNow).Customer=="Automated integration check","HTTP activation returns verifiable signed license");
        var limit=await http.PostAsJsonAsync("api/v1/activate",new ActivationRequest(key,new string('F',64),"Extra PC"),LicenseProtocol.Json);Check(limit.StatusCode==HttpStatusCode.Conflict,"HTTP device limit enforced");
        http.DefaultRequestHeaders.Authorization=new("Bearer",File.ReadAllText(Path.Combine(data,"admin-key.txt")).Trim());(await http.PostAsync("admin/subscriptions/"+id+"/revoke",null)).EnsureSuccessStatusCode();http.DefaultRequestHeaders.Authorization=null;
        var renewal=await http.PostAsJsonAsync("api/v1/renew",new RenewalRequest(lease.RefreshToken,new string('E',64)),LicenseProtocol.Json);Check(renewal.StatusCode==HttpStatusCode.Forbidden,"Revocation rejects online renewal");
    }
}


