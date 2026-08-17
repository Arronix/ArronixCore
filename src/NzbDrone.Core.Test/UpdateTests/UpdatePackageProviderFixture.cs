using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Test.UpdateTests
{
    /// <summary>
    /// These tests previously called the upstream project's live update service over real HTTP and asserted
    /// that upstream packages were offered as upgrades — down to asserting the returned filenames matched
    /// <c>Sonarr.{branch}.4.*</c>. Both the outbound call and the upstream binaries are deliberately gone
    /// (see <see cref="UpdatePackageProvider"/>), so this fixture pins the replacement contract instead:
    /// no updates are offered, and nothing reaches the network.
    /// </summary>
    public class UpdatePackageProviderFixture : CoreTest<UpdatePackageProvider>
    {
        [Test]
        public void should_report_no_latest_update_regardless_of_current_version()
        {
            Subject.GetLatestUpdate("main", new Version(3, 0)).Should().BeNull();
            Subject.GetLatestUpdate("main", new Version(10, 0)).Should().BeNull();
        }

        [Test]
        public void should_report_no_latest_update_for_an_unknown_branch()
        {
            Subject.GetLatestUpdate("invalid_branch", new Version(3, 0)).Should().BeNull();
        }

        [Test]
        public void should_report_no_recent_updates()
        {
            Subject.GetRecentUpdates("main", new Version(4, 0), null).Should().BeEmpty();
        }

        [Test]
        public void should_report_no_recent_updates_when_a_previous_version_is_supplied()
        {
            Subject.GetRecentUpdates("main", new Version(4, 0), new Version(3, 0)).Should().BeEmpty();
        }

        /// <summary>
        /// The point of the change: checking for updates must not contact anyone. Any outbound request would
        /// register as a call on the mocked HTTP client.
        /// </summary>
        [Test]
        public void should_not_make_any_http_request()
        {
            Subject.GetLatestUpdate("main", new Version(3, 0));
            Subject.GetRecentUpdates("main", new Version(4, 0), null);

            Mocker.GetMock<IHttpClient>().VerifyNoOtherCalls();
        }
    }
}
