namespace Arronix.Plugins.Dependencies;

/// <summary>
/// Whether an installed package can be activated at all, before its dependencies are considered.
/// </summary>
/// <remarks>
/// A closed state with exactly the members the platform can produce. There is no caller-supplied reason
/// string: the graph branches on the state, and only the diagnostic boundary turns it into words.
/// </remarks>
internal enum PackageAvailability
{
    /// <summary>The package may be activated, so far as anything outside its dependencies is concerned.</summary>
    Available = 0,

    /// <summary>An operator switched the package off through host configuration.</summary>
    DisabledByConfiguration = 1
}

/// <summary>
/// Validates <see cref="PackageAvailability"/> values and renders the ones that refuse activation.
/// </summary>
internal static class PackageAvailabilityReason
{
    /// <summary>
    /// Proves a value is a defined member of the closed state.
    /// </summary>
    /// <param name="availability">The value.</param>
    /// <param name="parameterName">The parameter being checked.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined member.</exception>
    /// <remarks>
    /// An undefined value is refused rather than treated as "some other kind of unavailable". Silently
    /// widening the closed set is how a state nothing can produce starts to look supported.
    /// </remarks>
    public static PackageAvailability Required(PackageAvailability availability, string parameterName)
        => availability is PackageAvailability.Available or PackageAvailability.DisabledByConfiguration
            ? availability
            : throw new ArgumentOutOfRangeException(
                parameterName,
                availability,
                "Package availability is a closed state; this value is not one of its members.");

    /// <summary>
    /// Says why a package cannot be activated, as a phrase completing "cannot be activated: ...".
    /// </summary>
    /// <param name="availability">A state other than <see cref="PackageAvailability.Available"/>.</param>
    /// <returns>The phrase.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is <see cref="PackageAvailability.Available"/>, which has no reason to give, or is undefined.
    /// </exception>
    public static string Describe(PackageAvailability availability) => availability switch
    {
        PackageAvailability.DisabledByConfiguration => "it is disabled by configuration",
        _ => throw new ArgumentOutOfRangeException(
            nameof(availability),
            availability,
            "Only a package which cannot be activated has a reason to describe.")
    };
}
