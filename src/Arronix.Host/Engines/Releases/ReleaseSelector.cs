using System.Linq;
using Arronix.Abstractions.DTOs;
using Arronix.Abstractions.Media;
using Arronix.Abstractions.Releases;


namespace Arronix.Host.Engines.Releases;

/// <summary>Deterministically chooses the best admitted option regardless of provider result order.</summary>
public static class ReleaseSelector
{
    /// <summary>Selects one option, or none when every option is rejected.</summary>
    public static ReleaseOption<TTarget, TRelease>? Select<TTarget, TRelease>(
        IEnumerable<ReleaseOption<TTarget, TRelease>> options,
        ReleasePolicy<TRelease> policy,
        Comparison<ReleaseListing>? acquisitionPreference = null)
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(policy);

        var admitted = options
            .Where(option => option.Match.Disposition != TargetDisposition.Rejected)
            .Where(option => policy.Admit(option.Release).IsAdmitted)
            .ToArray();

        if (admitted.Length == 0)
        {
            return null;
        }

        Array.Sort(admitted, (left, right) => Compare(left, right, policy, acquisitionPreference));
        return admitted[^1];
    }

    private static int Compare<TTarget, TRelease>(
        ReleaseOption<TTarget, TRelease> left,
        ReleaseOption<TTarget, TRelease> right,
        ReleasePolicy<TRelease> policy,
        Comparison<ReleaseListing>? acquisitionPreference)
        where TTarget : class, IReleaseTarget
        where TRelease : class, IRelease
    {
        var release = policy.Compare(left.Release, right.Release);
        if (release != 0)
        {
            return release;
        }

        var acquisition = acquisitionPreference?.Invoke(left.Listing, right.Listing) ?? 0;
        if (acquisition != 0)
        {
            return acquisition;
        }

        return string.Compare(
            left.Listing.ReleaseId.ToString(),
            right.Listing.ReleaseId.ToString(),
            StringComparison.Ordinal);
    }
}
