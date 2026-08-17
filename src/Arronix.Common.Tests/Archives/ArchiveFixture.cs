using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using Arronix.Common.Archives;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arronix.Common.Tests.Archives;

/// <summary>
/// Shared scaffolding for the archive tests: a scratch folder that is removed afterwards, and helpers that
/// write archives holding whatever entry names a test needs — including the ones no honest tool produces.
/// </summary>
public abstract class ArchiveFixture
{
    /// <summary>Gets the scratch folder for the running test.</summary>
    protected string Scratch { get; private set; } = string.Empty;

    /// <summary>Gets the folder archives are extracted into.</summary>
    protected string Destination => Path.Combine(Scratch, "destination");

    /// <summary>Gets the service under test.</summary>
    protected ArchiveService Service { get; private set; } = null!;

    [SetUp]
    public void CreateScratch()
    {
        Scratch = Path.Combine(Path.GetTempPath(), "arronix-archives", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Scratch);
        Directory.CreateDirectory(Destination);
        Service = new ArchiveService(NullLogger<ArchiveService>.Instance);
    }

    [TearDown]
    public void RemoveScratch()
    {
        if (Directory.Exists(Scratch))
        {
            Directory.Delete(Scratch, recursive: true);
        }
    }

    /// <summary>
    /// Writes a zip holding exactly the entry names given, each with a little content.
    /// </summary>
    /// <param name="fileName">Name of the archive inside the scratch folder.</param>
    /// <param name="entryNames">Entry names to write verbatim, however malformed.</param>
    /// <returns>The full path of the archive.</returns>
    protected string WriteZip(string fileName, params string[] entryNames)
    {
        var path = Path.Combine(Scratch, fileName);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var entryName in entryNames)
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);

            if (entryName.EndsWith('/'))
            {
                continue;
            }

            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write($"content of {entryName}");
        }

        return path;
    }

    /// <summary>
    /// Writes a gzipped tar holding the entries given.
    /// </summary>
    /// <param name="fileName">Name of the archive inside the scratch folder.</param>
    /// <param name="entries">Entry type and name pairs, written verbatim.</param>
    /// <returns>The full path of the archive.</returns>
    protected string WriteGzippedTar(string fileName, params (TarEntryType Type, string Name)[] entries)
    {
        var path = Path.Combine(Scratch, fileName);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var compressor = new GZipStream(stream, CompressionLevel.Fastest);
        using var writer = new TarWriter(compressor, TarEntryFormat.Pax);

        foreach (var (type, name) in entries)
        {
            var entry = new PaxTarEntry(type, name);

            if (type == TarEntryType.RegularFile)
            {
                entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes($"content of {name}"));
            }

            if (type == TarEntryType.SymbolicLink)
            {
                entry.LinkName = "/etc/hosts";
            }

            writer.WriteEntry(entry);
        }

        return path;
    }

    /// <summary>
    /// Writes a gzipped tar holding one regular file with no content at all, which the format records with
    /// no data stream.
    /// </summary>
    /// <param name="fileName">Name of the archive inside the scratch folder.</param>
    /// <param name="entryName">Name of the empty entry.</param>
    /// <returns>The full path of the archive.</returns>
    protected string WriteGzippedTarWithEmptyFile(string fileName, string entryName)
    {
        var path = Path.Combine(Scratch, fileName);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var compressor = new GZipStream(stream, CompressionLevel.Fastest);
        using var writer = new TarWriter(compressor, TarEntryFormat.Pax);

        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName));

        return path;
    }
}
