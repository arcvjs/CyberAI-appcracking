using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Vapor.Licensing;
using Vapor.Backend;

string directory=Path.GetFullPath(Environment.GetEnvironmentVariable("VAPOR_DATA")??Path.Combine(Directory.GetCurrentDirectory(),".local-steam"));
string url=Environment.GetEnvironmentVariable("VAPOR_URL")??"http://127.0.0.1:47832";
if(!Uri.TryCreate(url,UriKind.Absolute,out var serverUri)||(serverUri.Scheme!="https"&&!(serverUri.Scheme=="http"&&serverUri.IsLoopback)))throw new InvalidOperationException("Use HTTPS, or HTTP on loopback for local development.");
if(args.Contains("--initialize")) {
    SteamBackend.Initialize(directory,url);
    using var backend=new SteamBackend(directory);
    if(!File.Exists(Path.Combine(directory,"demo-account.txt"))) {
        var created=backend.CreateAccount(new CreateAccountRequest("festivalgoer","vapor","Festival Goer"));
        backend.SetTrial(created.SteamId,VaporProtocol.AfterlightAppId,0);   // seed an already-ended trial: the exhibition "crack" state
        File.WriteAllText(Path.Combine(directory,"demo-account.txt"),created.SteamId);
        File.WriteAllText(Path.Combine(directory,"demo-login.txt"),"festivalgoer / vapor");
    }
    Console.WriteLine("Vapor backend initialized. Client trust, SDK trust, admin key, and the demo account are stored in the private data directory.");return;
}
if(!File.Exists(Path.Combine(directory,"signing-key.pem")))throw new InvalidOperationException("Initialize the backend with --initialize first.");
string adminKey=Environment.GetEnvironmentVariable("VAPOR_ADMIN_KEY")??File.ReadAllText(Path.Combine(directory,"admin-key.txt")).Trim();
if(adminKey.Length<32)throw new InvalidOperationException("A strong administrator key is required.");
var builder=WebApplication.CreateBuilder(args);builder.WebHost.UseUrls(url);builder.WebHost.ConfigureKestrel(k=>k.Limits.MaxRequestBodySize=16384);
builder.Services.AddSingleton(new SteamBackend(directory));
builder.Services.AddRateLimiter(options=>{
    options.RejectionStatusCode=429;
    options.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=90,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
    options.AddPolicy("sign-in",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=12,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
var app=builder.Build();app.UseRateLimiter();
app.Use(async(context,next)=>{
    context.Response.Headers.CacheControl="no-store";
    if(context.Request.Path.StartsWithSegments("/admin")) {
        string supplied=context.Request.Headers.Authorization.ToString();
        if(!supplied.StartsWith("Bearer ",StringComparison.Ordinal)||!VaporProtocol.SecretEquals(adminKey,supplied[7..])){context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new ApiError("unauthorized","Administrator authorization is required."));return;}
    }
    try{await next();}
    catch(VaporException error){context.Response.StatusCode=error.Code switch{"not_found"=>404,"invalid_request"=>400,"not_owned" or "trial_ended"=>403,_=>401};await context.Response.WriteAsJsonAsync(new ApiError(error.Code,error.Message));}
});
app.MapGet("/health",()=>Results.Ok(new{status="ok",product="Vapor Backend",version="1.0"}));
app.MapPost("/api/v1/login",(LoginRequest request,SteamBackend backend)=>backend.Login(request)).RequireRateLimiting("sign-in");
app.MapPost("/api/v1/ownership",(OwnershipQuery request,SteamBackend backend)=>Results.Ok(backend.GetOwnership(request)));
app.MapPost("/api/v1/purchase",(PurchaseRequest request,SteamBackend backend)=>Results.Ok(backend.Purchase(request)));
app.MapPost("/api/v1/appticket",(AppTicketRequest request,SteamBackend backend)=>Results.Ok(backend.IssueAppTicket(request)));
app.MapGet("/admin/accounts",(SteamBackend backend)=>Results.Ok(backend.ListAccounts()));
app.MapPost("/admin/accounts",(CreateAccountRequest request,SteamBackend backend)=>{var created=backend.CreateAccount(request);return Results.Ok(new{created.SteamId,created.Account.Account,created.Account.Nickname});});
app.MapGet("/admin/licenses",(SteamBackend backend)=>Results.Ok(backend.ListLicenses()));
app.MapPost("/admin/licenses/{steamId}/{appId}/trial",(string steamId,int appId,SetTrialRequest request,SteamBackend backend)=>{backend.SetTrial(steamId,appId,request.Minutes);return Results.NoContent();});
app.MapPost("/admin/licenses/{steamId}/{appId}/grant",(string steamId,int appId,GrantRequest request,SteamBackend backend)=>{backend.SetLicense(steamId,appId,request);return Results.NoContent();});
app.MapPost("/admin/licenses/{steamId}/{appId}/revoke",(string steamId,int appId,SteamBackend backend)=>{backend.Revoke(steamId,appId);return Results.NoContent();});
app.Run();
