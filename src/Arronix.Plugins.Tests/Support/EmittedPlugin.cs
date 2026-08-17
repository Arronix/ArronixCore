using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Arronix.Abstractions.Plugins;

#pragma warning disable ARX0014 // The extension model is experimental; these fixtures implement it.

namespace Arronix.Plugins.Tests.Support;

/// <summary>
/// What an emitted fixture extension does when it is configured.
/// </summary>
internal enum EmittedBehavior
{
    /// <summary>Register nothing and return.</summary>
    DoNothing = 0,

    /// <summary>Reach for the outbound-call gateway, which is gated on a privilege.</summary>
    ReachForTheNetwork = 1,

    /// <summary>Throw, to prove a failing extension quarantines itself rather than the host.</summary>
    Throw = 2
}

/// <summary>
/// Compiles a real extension assembly onto disk, so the loader can be proved against a genuine one.
/// </summary>
/// <remarks>
/// <para>
/// Worth the machinery. Every other test in this suite exercises one component in isolation; only a real
/// assembly, loaded through a real load context and cast to the host's own interface, proves the property
/// that matters most — that the contract assembly unified. A fake that the test constructed in the host's
/// own context could not fail that way, so it could not prove it either.
/// </para>
/// <para>
/// The emitted assembly references the contract assembly and nothing else, which is exactly what an
/// extension is permitted to reference, so it also passes through the static reference check for the right
/// reason rather than by accident.
/// </para>
/// </remarks>
internal static class EmittedPlugin
{
    /// <summary>
    /// Writes an extension assembly whose single entry module behaves as asked.
    /// </summary>
    /// <param name="folder">Where to write it.</param>
    /// <param name="pluginId">The identifier its module reports.</param>
    /// <param name="behavior">What its configure method does.</param>
    /// <param name="assemblyName">The assembly name, which is also its file name.</param>
    /// <param name="moduleCount">How many entry modules to expose, to prove ambiguity is a defect.</param>
    /// <returns>The full path of the written assembly.</returns>
    public static string Write(
        string folder,
        string pluginId,
        EmittedBehavior behavior = EmittedBehavior.DoNothing,
        string assemblyName = "Emitted.Plugin",
        int moduleCount = 1)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly);
        var module = builder.DefineDynamicModule(assemblyName);

        for (var index = 0; index < moduleCount; index++)
        {
            DefineModuleType(module, $"Emitted.PluginModule{index}", pluginId, behavior);
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, assemblyName + ".dll");
        builder.Save(path);
        return path;
    }

    private static void DefineModuleType(
        ModuleBuilder module,
        string typeName,
        string pluginId,
        EmittedBehavior behavior)
    {
        var type = module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object));

        type.AddInterfaceImplementation(typeof(IPluginModule));

        EmitIdProperty(type, pluginId);
        EmitConfigure(type, behavior);

        type.CreateType();
    }

    private static void EmitIdProperty(TypeBuilder type, string pluginId)
    {
        var getter = type.DefineMethod(
            "get_Id",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            typeof(PluginId),
            Type.EmptyTypes);

        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldstr, pluginId);
        il.Emit(OpCodes.Call, typeof(PluginId).GetMethod(nameof(PluginId.FromString), [typeof(string)])!);
        il.Emit(OpCodes.Ret);

        var property = type.DefineProperty("Id", PropertyAttributes.None, typeof(PluginId), null);
        property.SetGetMethod(getter);

        type.DefineMethodOverride(getter, typeof(IPluginModule).GetProperty(nameof(IPluginModule.Id))!.GetMethod!);
    }

    private static void EmitConfigure(TypeBuilder type, EmittedBehavior behavior)
    {
        var configure = type.DefineMethod(
            nameof(IPluginModule.Configure),
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot | MethodAttributes.Final,
            typeof(void),
            [typeof(IPluginContext)]);

        var il = configure.GetILGenerator();

        switch (behavior)
        {
            case EmittedBehavior.ReachForTheNetwork:
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Callvirt, typeof(IPluginContext).GetMethod(nameof(IPluginContext.RequireHttp))!);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ret);
                break;

            case EmittedBehavior.Throw:
                il.Emit(OpCodes.Ldstr, "this extension is broken");
                il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
                il.Emit(OpCodes.Throw);
                break;

            default:
                il.Emit(OpCodes.Ret);
                break;
        }

        type.DefineMethodOverride(configure, typeof(IPluginModule).GetMethod(nameof(IPluginModule.Configure))!);
    }
}
