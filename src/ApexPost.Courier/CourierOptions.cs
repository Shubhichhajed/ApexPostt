namespace ApexPost.Courier;

/// <summary>API endpoint settings. The base URL differs per platform (the Android emulator reaches the host as 10.0.2.2).</summary>
public sealed class ApexApiOptions
{
    public const string SectionName = "ApexApi";

    public string Platform { get; set; } = "Windows";
    public Dictionary<string, string> BaseUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Debug builds only: accept the untrusted local development certificate for localhost/10.0.2.2.</summary>
    public bool AllowUntrustedDevelopmentCertificate { get; set; }

    public Uri ResolveBaseUrl()
    {
        if (!BaseUrls.TryGetValue(Platform, out var url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"ApexApi:BaseUrls:{Platform} is missing or is not an absolute URL.");
        if (uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("The API base URL must use HTTPS: bearer tokens must never travel over plain HTTP.");
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}

/// <summary>Retry, circuit-breaker and timeout values. RESILIENCE.md explains each number.</summary>
public sealed class ResilienceSettings
{
    public const string SectionName = "Resilience";

    public int MaxRetryAttempts { get; set; } = 3;
    public double BaseDelaySeconds { get; set; } = 1;
    public double AttemptTimeoutSeconds { get; set; } = 5;
    public double TotalTimeoutSeconds { get; set; } = 45;
    public double FailureRatio { get; set; } = 0.5;
    public int MinimumThroughput { get; set; } = 4;
    public double SamplingDurationSeconds { get; set; } = 30;
    public double BreakDurationSeconds { get; set; } = 20;
}

/// <summary>Outbox synchronisation settings.</summary>
public sealed class SyncSettings
{
    public const string SectionName = "Sync";

    public int BatchSize { get; set; } = 20;
    public double HealthProbeSeconds { get; set; } = 5;
}

/// <summary>Where the courier's local data (Couchbase Lite files, protected token) is kept.</summary>
public sealed class CourierStorageOptions
{
    public const string SectionName = "Courier";

    public string? DataDirectory { get; set; }

    public string ResolveDataDirectory()
    {
        var directory = string.IsNullOrWhiteSpace(DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApexPost", "Courier")
            : DataDirectory;
        Directory.CreateDirectory(directory);
        return directory;
    }
}
