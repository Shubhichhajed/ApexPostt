using System.Text.RegularExpressions;
using ApexPost.Courier.Api;
using ApexPost.Courier.Auth;
using ApexPost.Courier.Offline;
using ApexPost.Courier.Resilience;
using ApexPost.Courier.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace ApexPost.Courier.Screens;

/// <summary>
/// The four courier screens from the brief, as console menus: Login, New Scan, Pending Scans and Sync Status.
/// Screens only collect input and display results; storage, upload and resilience live in services.
/// </summary>
public sealed partial class CourierConsole(
    IServiceProvider services,
    ITokenStore tokens,
    SessionEvents sessionEvents,
    IScanRepository scans,
    ChangeListenerService changeListener,
    SyncCoordinator sync,
    ConnectionState state)
{
    private volatile bool _sessionExpired;
    private string? _courierId;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        sessionEvents.SessionExpired += (_, _) => _sessionExpired = true;
        changeListener.DocumentChanged += (_, args) =>
            WriteLine(ConsoleColor.DarkCyan, $"[listener] changed: {string.Join(", ", args.ChangedDocumentIds)} | pending scans: {args.PendingScanCount}");
        sync.SyncCompleted += (_, message) => WriteLine(ConsoleColor.DarkGreen, $"[sync] {message}");

        await changeListener.RefreshPendingCountAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await EnsureLoggedInAsync(cancellationToken))
                return;

            if (!await MainMenuAsync(cancellationToken))
                return;
        }
    }

    // ---------- Login ----------

    private async Task<bool> EnsureLoggedInAsync(CancellationToken cancellationToken)
    {
        var token = await tokens.GetAsync(cancellationToken);
        if (_sessionExpired || string.IsNullOrWhiteSpace(token) || !TryStartSession(token))
        {
            if (_sessionExpired)
                WriteLine(ConsoleColor.Yellow, "Your session ended (401). Please log in again.");
            _sessionExpired = false;
            return await LoginScreenAsync(cancellationToken);
        }

        return true;
    }

    private async Task<bool> LoginScreenAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Header("Login");
            Console.WriteLine("1. Get a development token from the API (Development environment only)");
            Console.WriteLine("2. Paste an access token");
            Console.WriteLine("0. Exit");
            switch (Prompt("Select"))
            {
                case "1":
                    var courierId = Prompt("Courier ID (e.g. courier-demo-01)");
                    if (string.IsNullOrWhiteSpace(courierId))
                        continue;
                    try
                    {
                        var api = services.GetRequiredService<ApexApiClient>();
                        var response = await api.RequestDevelopmentTokenAsync(courierId.Trim(), cancellationToken);
                        if (await AcceptTokenAsync(response.AccessToken, cancellationToken))
                            return true;
                    }
                    catch (Exception ex) when (IsExpectedFailure(ex))
                    {
                        WriteLine(ConsoleColor.Red, $"Could not get a token: {ex.Message}");
                    }
                    break;
                case "2":
                    if (await AcceptTokenAsync(Prompt("Access token"), cancellationToken))
                        return true;
                    break;
                case "0":
                    return false;
            }
        }
    }

    private async Task<bool> AcceptTokenAsync(string? token, CancellationToken cancellationToken)
    {
        token = token?.Trim();
        if (string.IsNullOrWhiteSpace(token) || !TryStartSession(token))
        {
            WriteLine(ConsoleColor.Red, "That is not a usable courier token (it needs a courierId claim and must not be expired).");
            return false;
        }

        await tokens.SaveAsync(token, cancellationToken);
        WriteLine(ConsoleColor.Green, $"Logged in as {_courierId}. The token is stored encrypted and is never shown or logged.");
        sync.TriggerInBackground("login");
        return true;
    }

    private bool TryStartSession(string token)
    {
        if (!JwtPayloadReader.TryRead(token, out var courierId, out var expiresAt) || string.IsNullOrWhiteSpace(courierId))
            return false;
        if (expiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow)
            return false;

        _courierId = courierId;
        return true;
    }

    // ---------- Main menu ----------

    private async Task<bool> MainMenuAsync(CancellationToken cancellationToken)
    {
        while (!_sessionExpired)
        {
            Header($"ApexPost Courier - {_courierId}");
            Console.WriteLine("1. New Scan");
            Console.WriteLine("2. Pending Scans");
            Console.WriteLine("3. Sync Status");
            Console.WriteLine("4. Sync Now");
            Console.WriteLine("5. Log out");
            Console.WriteLine("0. Exit");
            var choice = Prompt("Select");
            if (_sessionExpired)
                break;

            switch (choice)
            {
                case "1": await NewScanScreenAsync(); break;
                case "2": await PendingScansScreenAsync(); break;
                case "3": SyncStatusScreen(); break;
                case "4": Console.WriteLine(await sync.SyncNowAsync("manual", cancellationToken)); break;
                case "5":
                    await tokens.ClearAsync(cancellationToken);
                    _courierId = null;
                    return true;
                case "0": return false;
            }
        }

        return true;
    }

    // ---------- New Scan ----------

    private async Task NewScanScreenAsync()
    {
        Header("New Scan");
        var trackingNumber = (Prompt("Tracking number (APX-12345678)") ?? string.Empty).Trim().ToUpperInvariant();
        if (!TrackingNumberPattern().IsMatch(trackingNumber))
        {
            WriteLine(ConsoleColor.Red, "Tracking number must look like APX-12345678.");
            return;
        }

        if (!Enum.TryParse<ScanType>(Prompt("Scan type (Pickup/InTransit/Delivered/Failed)"), ignoreCase: true, out var scanType)
            || !Enum.IsDefined(scanType))
        {
            WriteLine(ConsoleColor.Red, "Unknown scan type.");
            return;
        }

        var location = Prompt("Location")?.Trim();
        if (string.IsNullOrWhiteSpace(location))
        {
            WriteLine(ConsoleColor.Red, "Location is required.");
            return;
        }

        var notes = Prompt(scanType == ScanType.Delivered ? "Recipient signature name (required)" : "Notes (optional)")?.Trim();
        if (scanType == ScanType.Delivered && string.IsNullOrWhiteSpace(notes))
        {
            WriteLine(ConsoleColor.Red, "A Delivered scan needs the recipient's signature name.");
            return;
        }

        // Saved locally first, as Pending. This works with no network at all.
        var scan = await scans.CreateAsync(new ShipmentScan
        {
            TrackingNumber = trackingNumber,
            ScanType = scanType,
            CourierId = _courierId!,
            Location = location,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
            ScannedAtUtc = DateTime.UtcNow
        });
        WriteLine(ConsoleColor.Green, $"Saved locally as Pending: {scan.Id}");

        if (state.IsOnline)
            sync.TriggerInBackground("new scan");
        else
            WriteLine(ConsoleColor.Yellow, "Offline: the scan will upload automatically when the connection returns.");
    }

    // ---------- Pending Scans ----------

    private async Task PendingScansScreenAsync()
    {
        var pending = (await scans.GetPendingScansAsync()).ToList();
        Header($"Pending Scans ({pending.Count})");
        if (pending.Count == 0)
        {
            Console.WriteLine("Nothing waiting to upload.");
            return;
        }

        foreach (var scan in pending)
            Console.WriteLine($"{scan.ScannedAtUtc:yyyy-MM-dd HH:mm:ss}Z  {scan.TrackingNumber}  {scan.ScanType,-10} {scan.Location}  ({scan.Id})");
    }

    // ---------- Sync Status ----------

    private void SyncStatusScreen()
    {
        Header("Sync Status");
        Console.WriteLine($"Connection        : {(state.IsOnline ? "online" : "offline")}");
        Console.WriteLine($"Circuit breaker   : {state.Circuit}");
        Console.WriteLine($"Pending scans     : {changeListener.LastPendingCount}");
        Console.WriteLine($"Last sync attempt : {Format(state.LastSyncAttemptUtc)}");
        Console.WriteLine($"Last good sync    : {Format(state.LastSuccessfulSyncUtc)}");
        Console.WriteLine($"Last result       : {state.LastSyncSummary ?? "-"}");
        Console.WriteLine($"Failures in a row : {state.ConsecutiveFailures}");
        Console.WriteLine($"Last error        : {state.LastError ?? "-"}");
        Console.WriteLine($"Retries so far    : {state.RetryCount}");
    }

    // ---------- helpers ----------

    private void Header(string title)
    {
        Console.WriteLine();
        if (state.ShowOfflineBanner)
            WriteLine(ConsoleColor.Black, $" {ConnectionState.OfflineBanner} ", ConsoleColor.Yellow);
        Console.WriteLine($"=== {title} ===   pending: {changeListener.LastPendingCount}");
    }

    private static string? Prompt(string label)
    {
        Console.Write($"{label}: ");
        return Console.ReadLine();
    }

    private static string Format(DateTimeOffset? value) => value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "never";

    private static readonly Lock ConsoleGate = new();

    private static void WriteLine(ConsoleColor foreground, string text, ConsoleColor? background = null)
    {
        lock (ConsoleGate)
        {
            var (oldForeground, oldBackground) = (Console.ForegroundColor, Console.BackgroundColor);
            Console.ForegroundColor = foreground;
            if (background is { } bg)
                Console.BackgroundColor = bg;
            Console.WriteLine(text);
            Console.ForegroundColor = oldForeground;
            Console.BackgroundColor = oldBackground;
        }
    }

    private static bool IsExpectedFailure(Exception ex) => ex is HttpRequestException or ApiProblemException
        or MalformedResponseException or SessionExpiredException or Polly.CircuitBreaker.BrokenCircuitException
        or Polly.Timeout.TimeoutRejectedException;

    [GeneratedRegex(@"^APX-\d{8}$")]
    private static partial Regex TrackingNumberPattern();
}
