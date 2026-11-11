using System.Net.Security;
using System.Text;
using ApexPost.Courier;
using ApexPost.Courier.Api;
using ApexPost.Courier.Auth;
using ApexPost.Courier.Offline;
using ApexPost.Courier.Resilience;
using ApexPost.Courier.Screens;
using ApexPost.Courier.Sync;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Composition root for the courier client (Week 4 Part D-II, console edition of the mobile app).
if (OperatingSystem.IsWindows())
    Console.OutputEncoding = Encoding.UTF8; // so the offline banner's dash renders in the classic console
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true);

builder.Services.Configure<ApexApiOptions>(builder.Configuration.GetSection(ApexApiOptions.SectionName));
builder.Services.Configure<ResilienceSettings>(builder.Configuration.GetSection(ResilienceSettings.SectionName));
builder.Services.Configure<SyncSettings>(builder.Configuration.GetSection(SyncSettings.SectionName));
builder.Services.Configure<CourierStorageOptions>(builder.Configuration.GetSection(CourierStorageOptions.SectionName));

var apiOptions = builder.Configuration.GetSection(ApexApiOptions.SectionName).Get<ApexApiOptions>() ?? new ApexApiOptions();
var baseUrl = apiOptions.ResolveBaseUrl();
var dataDirectory = (builder.Configuration.GetSection(CourierStorageOptions.SectionName).Get<CourierStorageOptions>()
                     ?? new CourierStorageOptions()).ResolveDataDirectory();

// ---- Offline store from Part A (one process owns the embedded database) ----
builder.Services.AddSingleton(sp => new CourierDatabase(
    sp.GetRequiredService<ILogger<CourierDatabase>>(), Path.Combine(dataDirectory, "db")));
builder.Services.AddSingleton<IScanRepository, CouchbaseScanRepository>();
builder.Services.AddSingleton<ChangeListenerService>();

// ---- Token storage: encrypted at rest (DPAPI-protected keys on Windows) ----
var keyRing = builder.Services.AddDataProtection()
    .SetApplicationName("ApexPost.Courier")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
if (OperatingSystem.IsWindows())
    keyRing.ProtectKeysWithDpapi();
builder.Services.AddSingleton<ITokenStore>(sp => new ProtectedTokenStore(
    sp.GetRequiredService<IDataProtectionProvider>(), Path.Combine(dataDirectory, "token.bin"),
    sp.GetRequiredService<ILogger<ProtectedTokenStore>>()));
builder.Services.AddSingleton<SessionEvents>();
builder.Services.AddTransient<AuthHeaderHandler>();

// ---- Typed API client through IHttpClientFactory: auth header + resilience pipeline ----
builder.Services.AddSingleton<ConnectionState>();
var apiClient = builder.Services.AddHttpClient<ApexApiClient>(client =>
    {
        client.BaseAddress = baseUrl;
        client.Timeout = Timeout.InfiniteTimeSpan; // the resilience pipeline owns every timeout
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .AddHttpMessageHandler<AuthHeaderHandler>();
apiClient.AddResilienceHandler(ApexApiResilience.PipelineName, ApexApiResilience.Configure);

// Plain client for the /health probe: no retries and no circuit breaker, so it sees recovery immediately.
var healthClient = builder.Services.AddHttpClient(ConnectivityMonitor.HealthClientName, client =>
{
    client.BaseAddress = baseUrl;
    client.Timeout = TimeSpan.FromSeconds(3);
});

#if DEBUG
// Development certificate escape hatch. Compiled into Debug builds only, off by default, and limited to
// the local machine / Android emulator host. Release builds always validate certificates (see SECURITY.md).
if (apiOptions.AllowUntrustedDevelopmentCertificate)
{
    static HttpMessageHandler DevelopmentHandler() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (request, _, _, errors) =>
            errors == SslPolicyErrors.None
            || request.RequestUri is { IsLoopback: true }
            || request.RequestUri?.Host == "10.0.2.2"
    };
    apiClient.ConfigurePrimaryHttpMessageHandler(DevelopmentHandler);
    healthClient.ConfigurePrimaryHttpMessageHandler(DevelopmentHandler);
}
#endif

// ---- Sync: real uploader replaces the Part A fake; connectivity triggers uploads ----
builder.Services.AddTransient<IShipmentUploader, ApiShipmentUploader>();
builder.Services.AddTransient<OutboxService>();
builder.Services.AddSingleton<ConnectivityMonitor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ConnectivityMonitor>());
builder.Services.AddSingleton<SyncCoordinator>();
builder.Services.AddSingleton<CourierConsole>();

using var host = builder.Build();

var database = host.Services.GetRequiredService<CourierDatabase>();
await database.InitializeAsync();
var listener = host.Services.GetRequiredService<ChangeListenerService>();
await listener.StartListeningAsync();
_ = host.Services.GetRequiredService<SyncCoordinator>(); // subscribe to connectivity changes before monitoring starts

await host.StartAsync();
var settings = host.Services.GetRequiredService<IOptions<ApexApiOptions>>().Value;
Console.WriteLine($"ApexPost Courier - API {baseUrl} ({settings.Platform}) - local data in {dataDirectory}");

try
{
    await host.Services.GetRequiredService<CourierConsole>().RunAsync(CancellationToken.None);
}
finally
{
    await host.StopAsync();
    listener.Dispose();     // removes the change-listener token
    database.Dispose();     // then closes the database
}
