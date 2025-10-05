using Arronix.Abstractions.Health;

namespace Arronix.Abstractions.Tests.Health;

[TestFixture]
public class HealthCheckTests
{
    [Test]
    public void HealthCheck_HealthyFactoryMethodCreatesHealthyCheck()
    {
        var check = HealthCheck.Healthy("test-check", "Test Check", "All systems operational");

        Assert.That(check.CheckId, Is.EqualTo("test-check"));
        Assert.That(check.Name, Is.EqualTo("Test Check"));
        Assert.That(check.Status, Is.EqualTo(HealthStatus.Healthy));
        Assert.That(check.Severity, Is.EqualTo(HealthSeverity.Info));
        Assert.That(check.Message, Is.EqualTo("All systems operational"));
    }

    [Test]
    public void HealthCheck_DegradedFactoryMethodCreatesDegradedCheck()
    {
        var check = HealthCheck.Degraded(
            "test-check",
            "Test Check",
            HealthSeverity.Warning,
            "Performance degraded",
            "Check system resources");

        Assert.That(check.Status, Is.EqualTo(HealthStatus.Degraded));
        Assert.That(check.Severity, Is.EqualTo(HealthSeverity.Warning));
        Assert.That(check.RemediationHint, Is.EqualTo("Check system resources"));
    }

    [Test]
    public void HealthCheck_UnhealthyFactoryMethodCreatesUnhealthyCheck()
    {
        var check = HealthCheck.Unhealthy(
            "test-check",
            "Test Check",
            HealthSeverity.Error,
            "System failure",
            "Restart the service");

        Assert.That(check.Status, Is.EqualTo(HealthStatus.Unhealthy));
        Assert.That(check.Severity, Is.EqualTo(HealthSeverity.Error));
        Assert.That(check.RemediationHint, Is.EqualTo("Restart the service"));
    }
}
