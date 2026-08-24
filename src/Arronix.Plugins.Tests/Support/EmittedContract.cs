using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Arronix.Plugins.Tests.Support;

/// <summary>
/// Writes real contract-shaped assemblies onto disk, with exact control over the facts the shared-contract
/// rules are decided from.
/// </summary>
/// <remarks>
/// Emitted rather than compiled because the interesting inputs are precisely the ones a project file cannot
/// vary within one build: the same assembly name at two different versions, an assembly that carries a module
/// initializer, and a dependent whose reference table names a version the installation did not admit. Each of
/// those is a real file the metadata reader sees exactly as it would see a shipped one.
/// </remarks>
internal static class EmittedContract
{
    /// <summary>The type every emitted contract exposes.</summary>
    public const string ItemTypeName = "Emitted.Contract.Item";

    /// <summary>The method an emitted dependent uses to bind to a contract type.</summary>
    public const string BindMethodName = "Bind";

    /// <summary>Writes a contract-shaped assembly.</summary>
    /// <param name="folder">Where to write it.</param>
    /// <param name="assemblyName">The assembly name, which is also its file name.</param>
    /// <param name="version">The assembly version, which is the binding identity.</param>
    /// <param name="moduleInitializer">Whether to emit a module initializer.</param>
    /// <param name="reference">A type to bind to, whose assembly then appears in the reference table.</param>
    /// <returns>The full path of the written assembly.</returns>
    public static string Write(
        string folder,
        string assemblyName,
        Version version,
        bool moduleInitializer = false,
        Type? reference = null)
    {
        var builder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName) { Version = version },
            typeof(object).Assembly);
        var module = builder.DefineDynamicModule(assemblyName);

        var type = module.DefineType(
            ItemTypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object));

        if (reference is not null)
        {
            var bind = type.DefineMethod(
                BindMethodName,
                MethodAttributes.Public | MethodAttributes.Static,
                reference,
                Type.EmptyTypes);

            var bindIl = bind.GetILGenerator();
            bindIl.Emit(OpCodes.Ldnull);
            bindIl.Emit(OpCodes.Ret);
        }

        type.CreateType();

        if (moduleInitializer)
        {
            var initializer = module.DefineGlobalMethod(
                ".cctor",
                MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName
                    | MethodAttributes.RTSpecialName,
                typeof(void),
                Type.EmptyTypes);

            initializer.GetILGenerator().Emit(OpCodes.Ret);
            module.CreateGlobalFunctions();
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, assemblyName + ".dll");
        builder.Save(path);
        return path;
    }

    /// <summary>Writes an assembly that declares a managed entry point.</summary>
    /// <param name="folder">Where to write it.</param>
    /// <param name="assemblyName">The assembly name, which is also its file name.</param>
    /// <param name="version">The assembly version.</param>
    /// <returns>The full path of the written assembly.</returns>
    /// <remarks>
    /// Emitted rather than borrowed from the build output, because every assembly this repository produces
    /// is named under a prefix the loader blocks outright — so borrowing one would prove the deny list
    /// fires, not that an entry point is refused.
    /// </remarks>
    public static string WriteExecutable(string folder, string assemblyName, Version version)
    {
        var builder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName) { Version = version },
            typeof(object).Assembly);
        var module = builder.DefineDynamicModule(assemblyName);

        var type = module.DefineType(
            ItemTypeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(object));

        var main = type.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes);
        main.GetILGenerator().Emit(OpCodes.Ret);
        type.CreateType();

        var metadata = builder.GenerateMetadata(out var il, out _);

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(imageCharacteristics: Characteristics.ExecutableImage | Characteristics.Dll),
            new MetadataRootBuilder(metadata),
            il,
            entryPoint: MetadataTokens.MethodDefinitionHandle(main.MetadataToken & 0x00FFFFFF),
            flags: CorFlags.ILOnly);

        var image = new BlobBuilder();
        pe.Serialize(image);

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, assemblyName + ".dll");
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        image.WriteContentTo(file);

        return path;
    }

    /// <summary>
    /// Writes a contract assembly whose metadata is perfectly readable and which the runtime refuses to load.
    /// </summary>
    /// <param name="folder">Where to write it.</param>
    /// <param name="assemblyName">The assembly name, which is also its file name.</param>
    /// <param name="version">The assembly version.</param>
    /// <returns>The full path of the written assembly.</returns>
    /// <remarks>
    /// The CLI header's runtime flags are overwritten after the file is built. Metadata lives elsewhere, so
    /// identity, references and the pre-execution shape all still read exactly as they did; only the runtime
    /// loader objects, with "Bad IL format". That is the input the load transaction exists for: a candidate
    /// which survives every check that can be made from metadata and still cannot be loaded.
    /// </remarks>
    public static string WriteUnloadable(string folder, string assemblyName, Version version)
    {
        var path = Write(folder, assemblyName, version);
        var bytes = File.ReadAllBytes(path);
        var header = CliHeaderOffset(bytes);

        for (var index = 0; index < 4; index++)
        {
            bytes[header + 16 + index] = 0xFF;
        }

        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Writes a file which is not a readable managed assembly.</summary>
    /// <param name="folder">Where to write it.</param>
    /// <param name="fileName">The file name to write.</param>
    /// <param name="shape">Which way it is malformed.</param>
    /// <returns>The full path of the written file.</returns>
    public static string WriteMalformed(string folder, string fileName, MalformedShape shape)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);

        switch (shape)
        {
            case MalformedShape.Garbage:
                File.WriteAllText(path, "this is not a portable executable");
                break;

            case MalformedShape.Empty:
                File.WriteAllBytes(path, []);
                break;

            case MalformedShape.TruncatedHeader:
            case MalformedShape.TruncatedBody:
                var scratch = Path.Combine(folder, "scratch");
                var full = File.ReadAllBytes(Write(scratch, "Emitted.Truncation.Source", new Version(1, 0, 0, 0)));
                var keep = shape == MalformedShape.TruncatedHeader ? 64 : full.Length / 2;
                File.WriteAllBytes(path, full[..keep]);
                Directory.Delete(scratch, recursive: true);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        return path;
    }

    /// <summary>Corrupts one region of a valid assembly, leaving the rest intact.</summary>
    /// <param name="source">The valid assembly to copy from.</param>
    /// <param name="destination">Where to write the corrupted copy.</param>
    /// <param name="offset">Where to start overwriting.</param>
    /// <param name="length">How many bytes to overwrite.</param>
    /// <returns>The destination path.</returns>
    public static string WriteCorrupted(string source, string destination, int offset, int length)
    {
        var bytes = File.ReadAllBytes(source);

        for (var index = offset; index < Math.Min(offset + length, bytes.Length); index++)
        {
            bytes[index] = 0xFF;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, bytes);
        return destination;
    }

    private static int CliHeaderOffset(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var peReader = new PEReader(stream);

        var directory = peReader.PEHeaders.PEHeader!.CorHeaderTableDirectory;
        var section = peReader.PEHeaders.GetContainingSectionIndex(directory.RelativeVirtualAddress);
        var header = peReader.PEHeaders.SectionHeaders[section];

        return header.PointerToRawData + (directory.RelativeVirtualAddress - header.VirtualAddress);
    }
}

/// <summary>The ways a staged candidate can fail to be a readable managed assembly.</summary>
internal enum MalformedShape
{
    /// <summary>Not a portable executable at all.</summary>
    Garbage = 0,

    /// <summary>A zero-length file.</summary>
    Empty = 1,

    /// <summary>Too small to contain its own headers.</summary>
    TruncatedHeader = 2,

    /// <summary>Headers intact, metadata cut in half.</summary>
    TruncatedBody = 3
}
