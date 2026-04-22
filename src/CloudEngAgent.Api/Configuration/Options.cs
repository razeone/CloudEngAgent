using System.ComponentModel.DataAnnotations;

namespace CloudEngAgent.Api.Configuration;

/// <summary>Strongly-typed options bound from the <c>Cors</c> configuration section.</summary>
public sealed class CorsOptions
{
    public const string Section = "Cors";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

/// <summary>Strongly-typed options bound from the <c>Runs</c> configuration section.</summary>
public sealed class RunsOptions
{
    public const string Section = "Runs";

    [Range(1, 1024)]
    public int MaxConcurrent { get; set; } = 32;

    [Range(16, 65_536)]
    public int EventBufferSize { get; set; } = 1024;
}

/// <summary>Strongly-typed options bound from the <c>Sse</c> configuration section.</summary>
public sealed class SseOptions
{
    public const string Section = "Sse";

    [Range(30, 600)]
    public int TokenLifetimeSeconds { get; set; } = 120;
}

/// <summary>Strongly-typed options bound from the <c>Entra</c> configuration section.</summary>
public sealed class EntraOptions
{
    public const string Section = "Entra";

    public string TenantId { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;
}

/// <summary>Strongly-typed options bound from the <c>OpenTelemetry</c> configuration section.</summary>
public sealed class OpenTelemetryOptions
{
    public const string Section = "OpenTelemetry";

    public bool Enabled { get; set; }

    /// <summary>OTLP endpoint, e.g. <c>http://localhost:4317</c>. When null, the OTLP exporter falls back to the default.</summary>
    public string? OtlpEndpoint { get; set; }

    public string ServiceName { get; set; } = "cloud-eng-agent";
}

/// <summary>Strongly-typed options bound from the <c>RateLimiting</c> configuration section.</summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimiting";

    public bool Enabled { get; set; } = true;

    [Range(1, 10_000)]
    public int WritePermitsPerMinute { get; set; } = 60;

    [Range(1, 10_000)]
    public int ReadPermitsPerMinute { get; set; } = 600;
}
