using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.DTOs;

namespace Arronix.Abstractions.Quality;

/// <summary>One language a release states, and whether it states it as the only one.</summary>
/// <param name="Language">The language.</param>
/// <param name="IsDualLanguageMarker">
/// Whether the claim came from a dual or multi-language marker rather than from a language name. The
/// distinction is quality-bearing: a dual-language disc encode with no rip marker is a bitstream copy, and
/// reading that from a typed language claim is what keeps it out of a per-kind guard string.
/// </param>
[Experimental(ExperimentalContracts.Quality, UrlFormat = ExperimentalContracts.UrlFormat)]
public readonly record struct LanguageClaim(Language Language, bool IsDualLanguageMarker);
