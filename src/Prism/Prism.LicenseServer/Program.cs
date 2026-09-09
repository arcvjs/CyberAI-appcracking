using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Prism.Licensing;
using Prism.LicenseServer;

string directory=Path.GetFullPath(Environment.GetEnvironmentVariable("PRISM_LICENSE_DATA")??Path.Combine(Directory.GetCurrentDirectory(),".local-licensing"));
string url=Environment.GetEnvironmentVariable("PRISM_LICENSE_URL")??"http://127.0.0.1:47831";
if(!Uri.TryCreate(url,UriKind.Absolute,out var serverUri)||(serverUri.Scheme!="https"&&!(serverUri.Scheme=="http"&&serverUri.IsLoopback)))throw new InvalidOperationException("Use HTTPS, or HTTP on loopback for local development.");
if(args.Contains("--initialize")) {
    LicenseAuthority.Initialize(directory,url);
    using var authority=new LicenseAuthority(directory);
    if(!File.Exists(Path.Combine(directory,"development-license.txt"))){var created=authority.Create(new CreateSubscriptionRequest("Prism local development",30,2));File.WriteAllText(Path.Combine(directory,"development-license.txt"),created.Key);File.WriteAllText(Path.Combine(directory,"development-subscription.txt"),created.Subscription.Id);}
    Console.WriteLine("Local licensing service initialized. Public client trust, admin credential, and development subscription are stored in the private data directory.");return;
}
if(!File.Exists(Path.Combine(directory,"signing-key.pem")))throw new InvalidOperationException("Initialize the licensing service with --initialize first.");
string adminKey=Environment.GetEnvironmentVariable("PRISM_LICENSE_ADMIN_KEY")??File.ReadAllText(Path.Combine(directory,"admin-key.txt")).Trim();
if(adminKey.Length<32)throw new InvalidOperationException("A strong administrator key is required.");
var builder=WebApplication.CreateBuilder(args);builder.WebHost.UseUrls(url);builder.WebHost.ConfigureKestrel(k=>k.Limits.MaxRequestBodySize=16384);
builder.Services.AddSingleton(new LicenseAuthority(directory));
builder.Services.AddRateLimiter(options=>{
    options.RejectionStatusCode=429;
    options.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=90,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
    options.AddPolicy("activation",context=>RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=12,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
});
var app=builder.Build();app.UseRateLimiter();
app.Use(async(context,next)=>{
    context.Response.Headers.CacheControl="no-store";
    if(context.Request.Path.StartsWithSegments("/admin")) {
        string supplied=context.Request.Headers.Authorization.ToString();
        if(!supplied.StartsWith("Bearer ",StringComparison.Ordinal)||!LicenseProtocol.SecretEquals(adminKey,supplied[7..])){context.Response.StatusCode=401;await context.Response.WriteAsJsonAsync(new ApiError("unauthorized","Administrator authorization is required."));return;}
    }
    try{await next();}
    catch(LicenseException error){context.Response.StatusCode=error.Code switch{"not_found"=>404,"invalid_request"=>400,"device_limit"=>409,_=>403};await context.Response.WriteAsJsonAsync(new ApiError(error.Code,error.Message));}
});
app.MapGet("/health",()=>Results.Ok(new{status="ok",product="Prism Licensing",version="1.0"}));
app.MapPost("/api/v1/activate",(ActivationRequest request,LicenseAuthority authority)=>authority.Activate(request)).RequireRateLimiting("activation");
app.MapPost("/api/v1/renew",(RenewalRequest request,LicenseAuthority authority)=>authority.Renew(request));
app.MapPost("/api/v1/deactivate",(RenewalRequest request,LicenseAuthority authority)=>{authority.Deactivate(request);return Results.NoContent();});
app.MapGet("/admin/subscriptions",(LicenseAuthority authority)=>authority.List());
app.MapPost("/admin/subscriptions",(CreateSubscriptionRequest request,LicenseAuthority authority)=>{var created=authority.Create(request);return Results.Ok(new{licenseKey=created.Key,created.Subscription.Id,created.Subscription.Customer,created.Subscription.ExpiresAt,created.Subscription.MaxDevices});});
app.MapPost("/admin/subscriptions/{id}/extend",(string id,ExtendSubscriptionRequest request,LicenseAuthority authority)=>{authority.Extend(id,request.Days);return Results.NoContent();});
app.MapPost("/admin/subscriptions/{id}/revoke",(string id,LicenseAuthority authority)=>{authority.SetRevoked(id,true);return Results.NoContent();});
app.MapPost("/admin/subscriptions/{id}/restore",(string id,LicenseAuthority authority)=>{authority.SetRevoked(id,false);return Results.NoContent();});
app.MapDelete("/admin/subscriptions/{id}/devices/{device}",(string id,string device,LicenseAuthority authority)=>{authority.RemoveDevice(id,device);return Results.NoContent();});
app.Run();
