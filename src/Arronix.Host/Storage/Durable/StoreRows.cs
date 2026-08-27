namespace Arronix.Host.Storage.Durable;

/// <summary>The tables this vertical owns.</summary>
internal static class StoreTables
{
    internal const string CatalogIdentity = "catalog_identity";
    internal const string CatalogRedirect = "catalog_redirect";
    internal const string CatalogAllocation = "catalog_allocation";
    internal const string CatalogRecord = "catalog_record";
    internal const string LibraryEntry = "library_entry";
    internal const string LibraryMonitor = "library_entry_monitor";
    internal const string ProviderDefinition = "provider_definition";
    internal const string ProviderDefinitionSetting = "provider_definition_setting";
    internal const string ProviderDefinitionKind = "provider_definition_kind";
    internal const string ProviderDefinitionTag = "provider_definition_tag";
}

/// <summary>One catalog identifier bound to the local identity the platform holds it under.</summary>
/// <remarks>
/// Kind and level are the scope G04 already states: a local identity is unique within its media kind, and
/// an item's and a group's identifiers are different key spaces.
/// </remarks>
internal sealed class CatalogIdentityRow
{
    public string Kind { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public string Scheme { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public long Identity { get; set; }
}

/// <summary>One local identity superseded by another after a merge, kept so a held reference resolves.</summary>
internal sealed class CatalogRedirectRow
{
    public string Kind { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public long Superseded { get; set; }

    public long Surviving { get; set; }
}

/// <summary>The high-water mark of the local identities issued for one media kind.</summary>
/// <remarks>Without it a restart reissues numbers the library is already keyed by.</remarks>
internal sealed class CatalogAllocationRow
{
    public string Kind { get; set; } = string.Empty;

    public long Issued { get; set; }
}

/// <summary>
/// The catalog half of one item: an opaque typed payload under the reference the host holds it by.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Payload"/> is the item itself, serialized through the entry point its own contract declared,
/// so the complete typed value round-trips — release timeline included — with no media fact in any column.
/// <see cref="ContractMetadataHash"/> is that contract's declared hash over the member graph its reader
/// accepts, so a payload written by an incompatible build is refused rather than read as something else.
/// The exact item type is not stored: the installed registration supplies it, and durable state must not
/// depend on assembly naming or versioning.
/// </para>
/// <para>
/// Kind and level are the whole of <c>MediaItemRef</c>, so a database reopened by a different installation
/// still answers for the entity the reference names rather than for whichever kind asked.
/// </para>
/// <para>
/// <see cref="Title"/> and <see cref="CatalogState"/> are indexes over the payload's own values, written in
/// the same transaction and never read as truth. Both are members of the common item contract.
/// </para>
/// </remarks>
internal sealed class CatalogRecordRow
{
    public string Kind { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public long Identity { get; set; }

    /// <summary>The scheme of the catalog that is the authority for this record.</summary>
    public string CatalogScheme { get; set; } = string.Empty;

    /// <summary>The identifier that catalog answered with, which is its identity for the item.</summary>
    public string CatalogValue { get; set; } = string.Empty;

    /// <summary>An index over the payload's own title, so a page is ordered and taken in the store.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>An index over the payload's own catalog state, so a withdrawn record stays addressable.</summary>
    public int CatalogState { get; set; }

    /// <summary>The writing contract's declared hash over the member graph its reader accepts.</summary>
    public string ContractMetadataHash { get; set; } = string.Empty;

    /// <summary>The item, serialized through its own contract's generated metadata.</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>When the catalog-owned half was last written.</summary>
    public DateTimeOffset RefreshedAt { get; set; }

    /// <summary>Incremented on every catalog-owned write, so a concurrent one is detected rather than lost.</summary>
    public long Revision { get; set; }
}

/// <summary>
/// That the user has this item in their library, and when they added it.
/// </summary>
/// <remarks>
/// Presence and monitoring only. Chosen variants, paths, root folders and tags are not persisted in this
/// milestone; a facet carrying one is refused rather than written with it silently dropped.
/// </remarks>
internal sealed class LibraryEntryRow
{
    public long Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public long Identity { get; set; }

    public DateTimeOffset? AddedAt { get; set; }
}

/// <summary>One monitoring answer on one axis the item's level declares.</summary>
internal sealed class LibraryMonitorRow
{
    public long Id { get; set; }

    public long EntryId { get; set; }

    public string Dimension { get; set; } = string.Empty;

    public string Choice { get; set; } = string.Empty;
}

/// <summary>
/// One provider instance the operator configured.
/// </summary>
/// <remarks>
/// The identifier is host-assigned rather than database-generated, because it is the identifier the
/// operator and the API already use. State and message are not stored: whether a definition's implementation
/// is present is recomputed against the loaded registry, and a stored answer would be a stale one.
/// </remarks>
internal sealed class ProviderDefinitionRow
{
    public int Id { get; set; }

    public string Provider { get; set; } = string.Empty;

    public int Family { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public int Priority { get; set; }
}

/// <summary>One value the operator entered for a settings field the provider declared.</summary>
internal sealed class ProviderDefinitionSettingRow
{
    public long Id { get; set; }

    public int DefinitionId { get; set; }

    public string FieldId { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>One media kind the operator narrowed a definition to. None means every kind it can serve.</summary>
internal sealed class ProviderDefinitionKindRow
{
    public long Id { get; set; }

    public int DefinitionId { get; set; }

    public int Ordinal { get; set; }

    public string Kind { get; set; } = string.Empty;
}

/// <summary>One platform tag the operator applied to a definition.</summary>
internal sealed class ProviderDefinitionTagRow
{
    public long Id { get; set; }

    public int DefinitionId { get; set; }

    public int Ordinal { get; set; }

    public string Value { get; set; } = string.Empty;
}
