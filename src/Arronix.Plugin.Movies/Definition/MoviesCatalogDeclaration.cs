#pragma warning disable ARX0013 // Shape contracts are experimental; a media extension is their intended declarer.
#pragma warning disable ARX0014 // Extension contracts are experimental; a media extension is their intended declarer.
#pragma warning disable ARX0015 // Provider contracts are experimental; a catalog declaration is their intended declarer.
#pragma warning disable ARX0019 // Definition contracts are experimental; a media extension is their intended declarer.

using Arronix.Abstractions.Providers;
using Arronix.Abstractions.Definition;
using Arronix.Abstractions.Shape;

namespace Arronix.Plugin.Movies.Definition;

/// <summary>
/// The movie catalog as data: request templates, response field maps, the derivations that select out of a
/// vendor's own response shape, and the identifier conventions a user may type.
/// </summary>
/// <remarks>
/// <para>
/// Executed entirely by the host over the host's outbound gateway, attributed and rate-limited under this
/// extension's identity.
/// </para>
/// <para>
/// <b>The one thing left in this extension that names a vendor, and it names one because it is one.</b> It
/// is a cataloger wearing a media kind's clothes and it leaves for a cataloger plugin of its own at the
/// milestone that makes catalogers plugins; the instruction for this iteration was to stop the <i>shape</i>
/// naming vendors, and what remains is confined to this one file, which is the whole point of confining it.
/// When it moves, its response map stops being paths into field identifiers and becomes ordinary code
/// against <see cref="Movie"/>, because a cataloger assembly may reference this one.
/// </para>
/// <para>
/// Three of the five derivations left when the typed model landed: availability stages, the release-date
/// reduction and the secondary-year conditional are methods on <see cref="Movies"/> now. The two that
/// remain both select out of a vendor's own response shape and have no typed home until the cataloger owns
/// the mapping.
/// </para>
/// </remarks>
public static class MoviesCatalogDeclaration
{
    /// <summary>The level the movie response map fills, as the typed model derives its identifier.</summary>
    private const string MovieLevelId = "movie";

    /// <summary>The grouping axis the collection response map fills.</summary>
    private const string CollectionAxisId = "collection";

    /// <summary>The catalog this declaration speaks to.</summary>
    private const string TmdbScheme = "tmdb";

    /// <summary>The catalog it cross-references movies to.</summary>
    private const string ImdbScheme = "imdb";

    /// <summary>The key space collection identifiers live in, which is not the movie key space.</summary>
    private const string TmdbCollectionScheme = "tmdb-collection";

    /// <summary>The setting naming the certification regulator whose rating is kept.</summary>
    public const string CertificationCountrySetting = "certificationCountry";

    /// <summary>The setting naming the catalog endpoint.</summary>
    public const string BaseUrlSetting = "baseUrl";

    /// <summary>The setting below which a discovery result is discarded before it is offered.</summary>
    public const string MinimumPopularitySetting = "minimumPopularity";

    /// <summary>Builds the catalog declaration.</summary>
    /// <returns>The declaration. Pure data.</returns>
    public static CatalogDeclaration Build() => new()
    {
        Requests =
        [
            new RequestTemplate { RequestId = "movie", Verb = "GET", Route = "movie/{tmdbId}" },
            new RequestTemplate { RequestId = "movie-by-imdb", Verb = "GET", Route = "movie/imdb/{imdbId}" },
            new RequestTemplate
            {
                RequestId = "movie-bulk",
                Verb = "POST",
                Route = "movie/bulk",
                BodyTemplate = "[{tmdbIds:join(,)}]"
            },
            new RequestTemplate
            {
                RequestId = "changed",
                Verb = "GET",
                Route = "movie/changed",
                Query = [new RequestParameter("since", "{since:iso8601}")]
            },
            new RequestTemplate
            {
                RequestId = "collection",
                Verb = "GET",
                Route = "movie/collection/{collectionTmdbId}"
            },
            new RequestTemplate
            {
                RequestId = "search",
                Verb = "GET",
                Route = "search",

                // The year rides unconditionally and is allowed to be empty, exactly as the surveyed
                // client sends it: the catalog treats an empty year as "any", and omitting the parameter
                // changes which index it uses.
                Query =
                [
                    new RequestParameter("q", "{text:query:plus-separated}"),
                    new RequestParameter("year", "{year?}")
                ]
            },
            new RequestTemplate { RequestId = "discover", Verb = "GET", Route = "list/tmdb/{listName}" },
            new RequestTemplate { RequestId = "popular", Verb = "GET", Route = "movie/popular" }
        ],

        Responses =
        [
            new ResponseMap
            {
                LevelId = MovieLevelId,
                ExternalIdPath = "$.tmdbId",
                ExternalIdScheme = TmdbScheme,
                Rows =
                [
                    Map("$.title", "title"),
                    Map("$.originalTitle", "originalTitle"),
                    Map("$.originalLanguage", "originalLanguage", "language"),
                    Map("$.overview", "overview"),
                    Map("$.year", "year", "int"),
                    Map("$.runtime", "runtime", "minutes"),
                    Map("$.studio", "studio"),
                    Map("$.homepage", "website", "absolute-uri"),
                    Map("$.popularity", "popularity", "decimal"),
                    Map("$.genres[*]", "genres"),
                    Map("$.keywords[*]", "keywords"),
                    Map("$.inCinema", "inCinemas", "date"),
                    Map("$.physicalRelease", "physicalRelease", "date"),
                    Map("$.digitalRelease", "digitalRelease", "date"),
                    Map("$.alternativeTitles[*].title", "alternateTitles", "distinct")

                    // CANNOT EXPRESS, and each of these is a row the string surface had:
                    //
                    //   * the secondary identifier ($.imdbId). It is an element of the item's identifier
                    //     SET now rather than a field of its own, and a response row targets a field.
                    //   * the trailer ($.youtubeTrailerId). The item carries a whole address; the catalog
                    //     states one video host's bare identifier, and composing the address out of it is
                    //     that host's URL grammar, which is exactly what left the media kind.
                    //   * the translations. A response row is one path to one field, so three paths cannot
                    //     build the elements of one composite list.
                    //   * the five scores, for the same reason: five paths, one composite list.
                    //   * the collection. It is a reference now, filled by the axis map below, and a
                    //     response row has no way to state a reference.
                    //
                    // All five resolve the same way and at the same moment: when this declaration becomes
                    // a cataloger plugin that may reference the item type, the mapping is ordinary code and
                    // a composite is a constructor call.
                ]
            },
            new ResponseMap
            {
                AxisId = CollectionAxisId,
                ExternalIdPath = "$.tmdbId",
                ExternalIdScheme = TmdbCollectionScheme,

                // The members re-enter the movie map above rather than being described twice.
                MemberPath = "$.parts[*]",
                Rows =
                [
                    Map("$.name", "title"),
                    Map("$.overview", "overview"),
                    Map("$.parts.length", "memberCount", "count")
                ]
            }
        ],

        Derivations =
        [
            // DELETED, and this is what the typed model bought. Three rules left this list because they
            // are code now and always were: the availability stages (five parameters, one of them an
            // entire boolean and temporal expression grammar carried in a string, plus an ordering
            // mini-syntax), the release-date reduction, and the secondary-year conditional — the row the
            // review singled out for needing an "if". They are Movies.StatusOf, Movies.ReleaseDateOf and
            // Movies.SecondaryYearOf, and their grammars, parsers and validation rules cease to exist.
            new DerivationRule
            {
                // The preferred region only, with NO fallback to any region: "PG-13" on a foreign
                // regulator's scale means something else, and showing it as if it were the local rating
                // is worse than showing nothing. The item carries the region on the value now, so the rule
                // is enforceable by a consumer rather than only here.
                RuleId = "certification-region",
                TargetFieldId = "certification",
                Kind = DerivationKind.RegionSelect,
                Parameters = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    ["source"] = FieldValue.OfText("$.certifications[*]"),
                    ["regionKey"] = FieldValue.OfText("country"),
                    ["valueKey"] = FieldValue.OfText("certification"),
                    ["regionSetting"] = FieldValue.OfText(CertificationCountrySetting),
                    ["defaultRegion"] = FieldValue.OfText("US"),
                    ["fallbackToAnyRegion"] = FieldValue.OfBoolean(false)
                }
            },
            new DerivationRule
            {
                RuleId = "image-roles",
                Kind = DerivationKind.ImageRoleSelect,
                Parameters = new Dictionary<string, FieldValue>(StringComparer.Ordinal)
                {
                    ["source"] = FieldValue.OfText("$.images[*]"),
                    ["roleKey"] = FieldValue.OfText("coverType"),
                    ["urlKey"] = FieldValue.OfText("url"),
                    ["roles"] = FieldValue.OfText(
                        "poster->poster, fanart->fanart, banner->banner, clearlogo->clearLogo"),
                    ["pick"] = FieldValue.OfText("first-of-role"),
                    ["requireAbsoluteUri"] = FieldValue.OfBoolean(true)
                }
            }
        ],

        IdRules =
        [
            new IdNormalization
            {
                Scheme = ImdbScheme,
                Kind = IdRuleKind.PrefixPad,
                Prefix = "tt",
                PadDigitsTo = 7
            },
            new IdNormalization
            {
                Scheme = ImdbScheme,
                Kind = IdRuleKind.UrlSegment,
                AddressPattern = "imdb.com/title/{id}"
            },
            new IdNormalization
            {
                Scheme = TmdbScheme,
                Kind = IdRuleKind.UrlSegment,
                AddressPattern = "themoviedb.org/movie/{id}",
                StripSlugAfterDigits = true
            },
            new IdNormalization
            {
                Scheme = TmdbCollectionScheme,
                Kind = IdRuleKind.UrlSegment,
                AddressPattern = "themoviedb.org/collection/{id}",
                StripSlugAfterDigits = true
            },
            new IdNormalization
            {
                Scheme = ImdbScheme,
                Kind = IdRuleKind.TypedPrefix,
                Prefixes = ["imdb:", "imdbid:"]
            },
            new IdNormalization
            {
                Scheme = TmdbScheme,
                Kind = IdRuleKind.TypedPrefix,
                Prefixes = ["tmdb:", "tmdbid:"]
            },
            new IdNormalization
            {
                // "Arrival 2016" splits into ("Arrival", 2016). Nothing was filmed before 1870, and one
                // year of slack past today admits a movie that has been announced but not made.
                Kind = IdRuleKind.TrailingYearSplit,
                YearLowerBound = 1870,
                YearUpperBoundYearsFromNow = 1
            }
        ],

        // Back off fifteen minutes and floor to the hour: the catalog caches on hour boundaries, and an
        // exact-instant request straddling one silently drops the updates inside it.
        Delta = new DeltaSyncPolicy { BackoffMinutes = 15, FloorTo = TimeFloor.Hour },

        // Truncation is reported as incompleteness rather than as failure, because a list longer than its
        // page budget is a different fact from a list that could not be fetched.
        Paging = PagingPolicy.Default,

        Settings =
        [
            new SettingsField
            {
                FieldId = BaseUrlSetting,
                Name = "URL",
                ValueKind = FieldValueKind.Link,
                Role = SettingRole.Endpoint,
                Advanced = true,
                HelpText = "Only change this to point at a mirror or a local cache of the catalog."
            },
            new SettingsField
            {
                FieldId = CertificationCountrySetting,
                Name = "Certification region",
                ValueKind = FieldValueKind.Enumerated,
                Role = SettingRole.Enumeration,
                DefaultValue = "US",
                Choices =
                [
                    new FacetValue("US", "United States"),
                    new FacetValue("GB", "United Kingdom"),
                    new FacetValue("DE", "Germany"),
                    new FacetValue("FR", "France"),
                    new FacetValue("AU", "Australia"),
                    new FacetValue("CA", "Canada"),
                    new FacetValue("NL", "Netherlands"),
                    new FacetValue("NZ", "New Zealand")
                ]
            },
            new SettingsField
            {
                FieldId = MinimumPopularitySetting,
                Name = "Minimum popularity",
                ValueKind = FieldValueKind.Decimal,
                Role = SettingRole.Value,
                Advanced = true,
                DefaultValue = "0",
                HelpText = "Discovery results below this score are discarded before they are offered."
            }
        ]
    };

    private static ResponseMapRow Map(string path, string fieldId, string? converter = null) =>
        new() { JsonPath = path, FieldId = fieldId, Converter = converter };
}
