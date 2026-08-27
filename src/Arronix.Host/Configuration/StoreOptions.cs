using System.ComponentModel.DataAnnotations;

namespace Arronix.Host.Configuration;

/// <summary>
/// Where the host keeps the state it must still hold after the process that wrote it has gone.
/// </summary>
/// <remarks>
/// <para>
/// One local file. This milestone proves one narrow durable vertical — catalog identity, the catalog
/// records that vertical materializes, and the user-owned library facet beside them — and a local embedded
/// database is the smallest mechanism that makes those seams honestly durable. It is deliberately not a
/// claim about the platform's eventual storage or provider contract: what a later store has to reproduce is
/// the transaction boundaries below it, not this file format.
/// </para>
/// <para>
/// A relative path is resolved against the process working directory, which is what an operator running
/// the server from its own folder expects; an absolute path is taken as given.
/// </para>
/// </remarks>
public sealed class StoreOptions
{
    /// <summary>The configuration section this options type binds from.</summary>
    public const string SectionName = "Arronix:Store";

    /// <summary>
    /// Gets or sets the local database file the durable seams read and write.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string DataSource { get; set; } = "arronix.db";
}
