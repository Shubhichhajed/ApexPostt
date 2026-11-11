using System.Net.NetworkInformation;
using ApexPost.Courier.Resilience;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApexPost.Courier.Sync;

/// <summary>
/// Console stand-in for MAUI <c>Connectivity.ConnectivityChanged</c>. It listens to the operating system's
/// NetworkChange.NetworkAvailabilityChanged event (airplane mode, Wi-Fi off) and also probes the API's
/// /health endpoint, so "API stopped" counts as offline too. Raises <see cref="ConnectivityChanged"/> on every change.
/// </summary>
public sealed class ConnectivityMonitor(
    IHttpClientFactory httpClientFactory,
    ConnectionState state,
    IOptions<SyncSettings> options,
    ILogger<ConnectivityMonitor> logger) : BackgroundService
{
    public const string HealthClientName = "apexpost-health";

    private volatile bool _networkAvailable = NetworkInterface.GetIsNetworkAvailable();
    private readonly SemaphoreSlim _wakeUp = new(0, 1);
    private bool? _lastReported;

    public event EventHandler<bool>? ConnectivityChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        try
        {
            var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.HealthProbeSeconds));
            while (!stoppingToken.IsCancellationRequested)
            {
                var online = _networkAvailable && await ProbeAsync(stoppingToken);
                Report(online);

                // Wait for the next probe, or wake early when the OS reports a network change.
                try
                {
                    await _wakeUp.WaitAsync(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        }
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        _networkAvailable = e.IsAvailable;
        logger.LogInformation("Operating system reports network {State}", e.IsAvailable ? "available" : "unavailable");
        if (_wakeUp.CurrentCount == 0)
        {
            try { _wakeUp.Release(); }
            catch (SemaphoreFullException) { /* a wake-up is already pending */ }
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = httpClientFactory.CreateClient(HealthClientName);
            using var response = await client.GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void Report(bool online)
    {
        state.SetOnline(online);
        if (_lastReported == online)
            return;

        _lastReported = online;
        logger.LogInformation("Connectivity changed: {State}", online ? "ONLINE" : "OFFLINE");
        ConnectivityChanged?.Invoke(this, online);
    }

    public override void Dispose()
    {
        _wakeUp.Dispose();
        base.Dispose();
    }
}
