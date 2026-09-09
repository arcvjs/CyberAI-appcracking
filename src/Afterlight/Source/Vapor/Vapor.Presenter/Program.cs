using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Vapor.Licensing;

string state=args.FirstOrDefault()??"help";
if(state is not ("expired" or "owned" or "trial" or "revoke" or "status")) {Console.WriteLine("Afterlight exhibition controls\nDemoControl.exe expired | owned | trial [minutes] | revoke | status\nOnly changes the local mock game's demo account.");return 0;}
try
{
    string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,".."));
    using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"exhibition.json")));
    string url=manifest.RootElement.GetProperty("serverUrl").GetString()!;
    if(!new Uri(url).IsLoopback)throw new InvalidOperationException("Presenter controls require the local demo service.");
    string issuer=VaporProtocol.Hash((root+Path.DirectorySeparatorChar).ToUpperInvariant())[..16];
    string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Vapor","Exhibition",issuer,"service-data");
    using var http=new HttpClient{BaseAddress=new Uri(url),Timeout=TimeSpan.FromSeconds(3)};
    bool ready=false;try{ready=(await http.GetAsync("/health")).IsSuccessStatusCode;}catch(HttpRequestException){}
    if(!ready)
    {
        Process.Start(new ProcessStartInfo(Path.Combine(root,"Vapor.exe")){UseShellExecute=false,WorkingDirectory=root});
        for(int i=0;i<40;i++){await Task.Delay(250);try{ready=(await http.GetAsync("/health")).IsSuccessStatusCode;}catch(HttpRequestException){}if(ready)break;}
    }
    if(!ready)throw new IOException("The local exhibition service did not start.");
    string account=File.ReadAllText(Path.Combine(data,"demo-account.txt")).Trim();
    http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",File.ReadAllText(Path.Combine(data,"admin-key.txt")).Trim());
    if(state=="status"){Console.WriteLine(await http.GetStringAsync("/admin/licenses"));return 0;}
    string operation=state=="owned"?"grant":state=="revoke"?"revoke":"trial";
    int minutes=state=="trial"&&args.Length>1?int.Parse(args[1]):2;
    object body=state=="owned"?new{license="owned"}:(object)new{minutes=state=="expired"?0:minutes};
    using var response=await http.PostAsJsonAsync($"/admin/licenses/{account}/{VaporProtocol.AfterlightAppId}/{operation}",body);
    if(!response.IsSuccessStatusCode)throw new IOException(await response.Content.ReadAsStringAsync());
    Console.WriteLine($"Exhibition state set to {state}. Click LIBRARY to refresh. Running games recheck within 20 seconds.\nThis is a local simulation; no payment or Steam account is involved.");return 0;
}
catch(Exception e){Console.Error.WriteLine(e.Message);return 1;}
