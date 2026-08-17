using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Update
{
    public interface IUpdatePackageProvider
    {
        UpdatePackage GetLatestUpdate(string branch, Version currentVersion);
        List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null);
    }

    /// <summary>
    /// Reports that no updates are available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The upstream implementation queried the upstream project's update service on every check, sending this
    /// installation's version, operating system, CPU architecture, .NET runtime version, database engine and —
    /// when analytics were enabled — a liveness flag derived from whether the library had recent activity.
    /// Inherited by a fork, that both reports deployment telemetry to an unrelated third party and offers
    /// <em>that project's</em> binaries as upgrades for this one, which would replace an Arronix install with
    /// an upstream build.
    /// </para>
    /// <para>
    /// Arronix publishes no update feed, so "no update available" is the truthful answer and no request is
    /// made. When an Arronix update service exists, restore the requests here against an Arronix-controlled
    /// endpoint; send only what the endpoint needs to select a package (version, OS, architecture) and keep
    /// any usage signal behind an explicit operator opt-in.
    /// </para>
    /// </remarks>
    public class UpdatePackageProvider : IUpdatePackageProvider
    {
        public UpdatePackage GetLatestUpdate(string branch, Version currentVersion)
        {
            return null;
        }

        public List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null)
        {
            return new List<UpdatePackage>();
        }
    }
}
