using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck
{
    public class ServerSideNotificationService : HealthCheckBase
    {
        public ServerSideNotificationService(ILocalizationService localizationService)
            : base(localizationService)
        {
        }

        public override HealthCheck Check()
        {
            // DISABLED: this health check used to call the upstream project's services endpoint on every run,
            // sending this installation's version, operating system, CPU architecture and release branch. That
            // is an install beacon — it let a third party count and profile deployments of this fork — and it
            // fetched announcements written for a different product, which do not apply here.
            //
            // Arronix operates no equivalent service. Rather than point this at someone else's, the check is
            // inert and always reports healthy. If Arronix ever runs its own announcement service, restore the
            // request here against an Arronix-controlled endpoint and gate it on an explicit operator opt-in.
            return new HealthCheck(GetType());
        }
    }
}
