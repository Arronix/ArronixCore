
namespace Arronix.Abstractions.Media;

/// <summary>A content classification issued under one regional classification system.</summary>
/// <remarks>
/// Region alone does not identify a vocabulary. Carrying the authority prevents equal-looking codes from
/// different systems being treated as interchangeable while leaving both vocabularies open to catalogers.
/// </remarks>
public sealed record ContentCertification
{
    /// <summary>Creates a validated classification value.</summary>
    /// <param name="region">The ISO 3166 region whose rules apply.</param>
    /// <param name="authority">The regulator or classification system that issued the code.</param>
    /// <param name="code">The code exactly as that authority spells it.</param>
    /// <param name="minimumAge">The minimum age represented by the code, when the system defines one.</param>
    public ContentCertification(string region, string authority, string code, int? minimumAge = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        ArgumentException.ThrowIfNullOrWhiteSpace(authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (minimumAge < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumAge), minimumAge, "A minimum age cannot be negative.");
        }

        Region = region;
        Authority = authority;
        Code = code;
        MinimumAge = minimumAge;
    }

    /// <summary>Gets the ISO 3166 region whose rules apply.</summary>
    public string Region { get; }

    /// <summary>Gets the regulator or classification system that issued the code.</summary>
    public string Authority { get; }

    /// <summary>Gets the code exactly as that authority spells it.</summary>
    public string Code { get; }

    /// <summary>Gets the minimum age represented by the code, when the system defines one.</summary>
    public int? MinimumAge { get; }
}
