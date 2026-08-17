using System.Diagnostics.CodeAnalysis;
using Arronix.Abstractions.Shape;

namespace Arronix.Abstractions.Intent;

/// <summary>
/// Declares a working surface in which the platform proposes a set of decisions, the user amends them,
/// and the whole set is committed together.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the rest of the vocabulary honestly could not express one of the platform's daily
/// workflows: assigning a folder full of loose files to the items they satisfy, starting from a proposal
/// the extension computes. Expressing that as "an action with a collection parameter of triples" is not a
/// user interface, and saying so is better than shipping a vocabulary that cannot do the job.
/// </para>
/// <para>
/// It ships no code and no markup: an extension declares the columns and supplies a proposal, and one
/// generic consumer implementation serves every kind. The same primitive turns out to cover interactive
/// release selection and bulk editing too, which is the evidence that it is a primitive rather than one
/// workflow in disguise.
/// </para>
/// <para>
/// It passes the same test as the rest of this area. A command line proposes, sets a row and commits; a
/// text interface lays the rows out; a hands-free assistant reads the rows back and confirms; a screen
/// reader gets a real table with headers. Nothing in it names a technology or an implement.
/// </para>
/// </remarks>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record WorkbenchDescriptor
{
    /// <summary>
    /// Gets the identifier a proposal and a commit reference this surface by.
    /// </summary>
    public required string WorkbenchId { get; init; }

    /// <summary>
    /// Gets the surface's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets a sentence explaining what the surface is for.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets what the surface operates on, which is what tells a consumer where to offer it.
    /// </summary>
    public required WorkbenchSubject Subject { get; init; }

    /// <summary>
    /// Gets the level the decisions are about, when they are about one.
    /// </summary>
    public MediaLevelId? TargetLevelId { get; init; }

    /// <summary>
    /// Gets the columns, in order.
    /// </summary>
    public required IReadOnlyList<WorkbenchColumn> Columns { get; init; }

    /// <summary>
    /// Gets the values a consumer must collect before asking for a proposal — a folder to scan, a query
    /// to run.
    /// </summary>
    public IReadOnlyList<ActionParameter> Inputs { get; init; } = [];

    /// <summary>
    /// Gets the name of the commit, phrased as an instruction.
    /// </summary>
    public required string CommitLabel { get; init; }

    /// <summary>
    /// Gets how much committing costs and how far it can be undone.
    /// </summary>
    public required Consequence CommitConsequence { get; init; }

    /// <summary>
    /// Gets how sure the user must be before committing.
    /// </summary>
    public required ConfirmationRequirement CommitConfirmation { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user may leave rows out of the commit.
    /// </summary>
    public bool AllowsRowExclusion { get; init; } = true;
}

/// <summary>
/// What a working surface operates on.
/// </summary>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public enum WorkbenchSubject
{
    /// <summary>Files that are not yet part of the library.</summary>
    LooseFiles = 0,

    /// <summary>Releases that could be acquired.</summary>
    ReleaseCandidates = 1,

    /// <summary>Items already in the library.</summary>
    LibraryItems = 2
}

/// <summary>
/// One column of a working surface.
/// </summary>
/// <remarks>
/// A read-only column is a projection of the row; an editable one is a decision the user makes, with the
/// field's own declared choices or an option source supplying the permitted values.
/// </remarks>
[Experimental(ExperimentalContracts.Intent, UrlFormat = ExperimentalContracts.UrlFormat)]
public sealed record WorkbenchColumn
{
    /// <summary>
    /// Gets the field this column holds, which supplies the column's name, value shape and choices.
    /// </summary>
    public required FieldDescriptor Field { get; init; }

    /// <summary>
    /// Gets a value indicating whether the user may change this column's value.
    /// </summary>
    public bool Editable { get; init; }

    /// <summary>
    /// Gets the identifier of a set of permitted values resolved through the platform per row. Row-scoped
    /// because the legal answers for one row are rarely the legal answers for another.
    /// </summary>
    public string? OptionSourceId { get; init; }
}
