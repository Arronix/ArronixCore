namespace Arronix.Abstractions.Health;

/// <summary>
/// Represents a health check result from a component or plugin.
/// </summary>
public record HealthCheck(
    string CheckId,
    string Name,
    HealthStatus Status,
    HealthSeverity Severity,
    string? Message = null,
    string? RemediationHint = null,
    DateTime? LastChecked = null)
{
    /// <summary>
    /// Creates a healthy health check.
    /// </summary>
    public static HealthCheck Healthy(string checkId, string name, string? message = null)
        => new(checkId, name, HealthStatus.Healthy, HealthSeverity.Info, message, null, DateTime.UtcNow);

    /// <summary>
    /// Creates a degraded health check.
    /// </summary>
    public static HealthCheck Degraded(
        string checkId,
        string name,
        HealthSeverity severity,
        string message,
        string? remediation = null)
        => new(checkId, name, HealthStatus.Degraded, severity, message, remediation, DateTime.UtcNow);

    /// <summary>
    /// Creates an unhealthy health check.
    /// </summary>
    public static HealthCheck Unhealthy(
        string checkId,
        string name,
        HealthSeverity severity,
        string message,
        string? remediation = null)
        => new(checkId, name, HealthStatus.Unhealthy, severity, message, remediation, DateTime.UtcNow);
}

/// <summary>
/// The status of a health check.
/// </summary>
public enum HealthStatus
{
    /// <summary>Component is healthy and functioning normally.</summary>
    Healthy = 0,

    /// <summary>Component is degraded but still functional.</summary>
    Degraded = 1,

    /// <summary>Component is unhealthy and not functioning properly.</summary>
    Unhealthy = 2
}

/// <summary>
/// The severity level of a health issue.
/// </summary>
public enum HealthSeverity
{
    /// <summary>Informational message, no action required.</summary>
    Info = 0,

    /// <summary>Warning, attention may be needed.</summary>
    Warning = 1,

    /// <summary>Error, action should be taken.</summary>
    Error = 2,

    /// <summary>Critical error, immediate action required.</summary>
    Critical = 3
}
