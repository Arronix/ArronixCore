using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Arronix.Abstractions.Events;
using Arronix.Abstractions.Plugins;
using Arronix.Abstractions.Providers;


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
    Throw = 2,

    /// <summary>Subscribe twice to a platform event, so contributed order can be observed.</summary>
    SubscribeTwiceToAPlatformEvent = 3
}

/// <summary>
/// Where an emitted fixture extension fails, and with what.
/// </summary>
/// <remarks>
/// The loader's containment policy is about which failures belong to a package and which mean the process
/// is no longer sound, so a fixture has to be able to throw both kinds from both places package code runs
/// before registration: the module constructor, which reflection wraps, and the identifier getter, which the
/// loader calls directly.
/// </remarks>
internal enum EmittedFault
{
    /// <summary>Construct and report an identifier normally.</summary>
    None = 0,

    /// <summary>Throw an exception type declared by the fixture itself, from the constructor.</summary>
    ConstructorThrowsNovel = 1,

    /// <summary>Throw <see cref="OutOfMemoryException"/> from the constructor, which reflection wraps.</summary>
    ConstructorThrowsOutOfMemory = 2,

    /// <summary>Throw <see cref="OperationCanceledException"/> from the constructor, which reflection wraps.</summary>
    ConstructorThrowsCanceled = 3,

    /// <summary>Throw the fixture's own exception type from the identifier getter.</summary>
    IdGetterThrowsNovel = 4,

    /// <summary>Throw <see cref="OutOfMemoryException"/> directly from the identifier getter.</summary>
    IdGetterThrowsOutOfMemory = 5,

    /// <summary>Throw <see cref="OperationCanceledException"/> directly from the identifier getter.</summary>
    IdGetterThrowsCanceled = 6
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
    /// <param name="fault">Where the module fails before registration, and with what.</param>
    /// <param name="disposalMarker">
    /// A file the module writes when it is disposed, so a test can observe that a module constructed before
    /// a later failure was still released.
    /// </param>
    /// <returns>The full path of the written assembly.</returns>
    public static string Write(
        string folder,
        string pluginId,
        EmittedBehavior behavior = EmittedBehavior.DoNothing,
        string assemblyName = "Emitted.Plugin",
        int moduleCount = 1,
        EmittedFault fault = EmittedFault.None,
        string? disposalMarker = null)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName(assemblyName), typeof(object).Assembly);
        var module = builder.DefineDynamicModule(assemblyName);
        var novel = DefineNovelException(module);
        var handler = behavior == EmittedBehavior.SubscribeTwiceToAPlatformEvent ? DefineEventHandler(module) : null;

        for (var index = 0; index < moduleCount; index++)
        {
            DefineModuleType(module, $"Emitted.PluginModule{index}", pluginId, behavior, fault, novel, handler, disposalMarker);
        }

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, assemblyName + ".dll");
        builder.Save(path);
        return path;
    }

    /// <summary>
    /// Declares an exception type in the fixture's own assembly, which the platform has never seen.
    /// </summary>
    /// <remarks>
    /// A package may throw anything, so the containment rule for package code cannot be an allowlist. This
    /// type is what makes that testable: no policy anywhere names it.
    /// </remarks>
    private static ConstructorBuilder DefineNovelException(ModuleBuilder module)
    {
        var type = module.DefineType(
            "Emitted.NovelPackageFailure",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(Exception));

        var constructor = type.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes);

        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, "this package failed in a way nothing has seen before");
        il.Emit(OpCodes.Call, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Ret);

        type.CreateType();
        return constructor;
    }

    /// <summary>
    /// Declares a handler of a platform event in the fixture's own assembly.
    /// </summary>
    /// <remarks>
    /// A platform event rather than one of the fixture's own, because two extensions can only be observed in
    /// order on an event they can both subscribe to.
    /// </remarks>
    private static ConstructorInfo DefineEventHandler(ModuleBuilder module)
    {
        var contract = typeof(IEventHandler<ProviderDefinitionChanged>);
        var type = module.DefineType(
            "Emitted.PlatformEventHandler",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object));

        type.AddInterfaceImplementation(contract);

        var handle = type.DefineMethod(
            nameof(IEventHandler<ProviderDefinitionChanged>.HandleAsync),
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot | MethodAttributes.Final,
            typeof(Task),
            [typeof(ProviderDefinitionChanged), typeof(CancellationToken)]);

        var il = handle.GetILGenerator();
        il.Emit(OpCodes.Call, typeof(Task).GetProperty(nameof(Task.CompletedTask))!.GetMethod!);
        il.Emit(OpCodes.Ret);

        type.DefineMethodOverride(handle, contract.GetMethod(nameof(IEventHandler<ProviderDefinitionChanged>.HandleAsync))!);

        var constructor = type.DefineDefaultConstructor(MethodAttributes.Public);
        type.CreateType();
        return constructor;
    }

    private static void DefineModuleType(
        ModuleBuilder module,
        string typeName,
        string pluginId,
        EmittedBehavior behavior,
        EmittedFault fault,
        ConstructorBuilder novel,
        ConstructorInfo? handler,
        string? disposalMarker)
    {
        var type = module.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            typeof(object));

        type.AddInterfaceImplementation(typeof(IPluginModule));

        if (disposalMarker is not null)
        {
            type.AddInterfaceImplementation(typeof(IDisposable));
            EmitDispose(type, disposalMarker);
        }

        EmitConstructor(type, fault, novel);
        EmitIdProperty(type, pluginId, fault, novel);
        EmitConfigure(type, behavior, handler);

        type.CreateType();
    }

    /// <summary>Emits the parameterless constructor the loader activates through.</summary>
    private static void EmitConstructor(TypeBuilder type, EmittedFault fault, ConstructorBuilder novel)
    {
        var constructor = type.DefineConstructor(
            MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes);

        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

        switch (fault)
        {
            case EmittedFault.ConstructorThrowsNovel:
                il.Emit(OpCodes.Newobj, novel);
                il.Emit(OpCodes.Throw);
                break;

            case EmittedFault.ConstructorThrowsOutOfMemory:
                il.Emit(OpCodes.Newobj, typeof(OutOfMemoryException).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Throw);
                break;

            case EmittedFault.ConstructorThrowsCanceled:
                il.Emit(OpCodes.Newobj, typeof(OperationCanceledException).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Throw);
                break;

            default:
                il.Emit(OpCodes.Ret);
                break;
        }
    }

    /// <summary>Emits a disposer that records having run, so release is observable across a load context.</summary>
    private static void EmitDispose(TypeBuilder type, string marker)
    {
        var dispose = type.DefineMethod(
            nameof(IDisposable.Dispose),
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig
                | MethodAttributes.NewSlot | MethodAttributes.Final,
            typeof(void),
            Type.EmptyTypes);

        var il = dispose.GetILGenerator();
        il.Emit(OpCodes.Ldstr, marker);
        il.Emit(OpCodes.Ldstr, "disposed");
        il.Emit(OpCodes.Call, typeof(File).GetMethod(nameof(File.WriteAllText), [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ret);

        type.DefineMethodOverride(dispose, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!);
    }

    private static void EmitIdProperty(
        TypeBuilder type,
        string pluginId,
        EmittedFault fault,
        ConstructorBuilder novel)
    {
        var getter = type.DefineMethod(
            "get_Id",
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Final,
            typeof(PluginId),
            Type.EmptyTypes);

        var il = getter.GetILGenerator();

        switch (fault)
        {
            case EmittedFault.IdGetterThrowsNovel:
                il.Emit(OpCodes.Newobj, novel);
                il.Emit(OpCodes.Throw);
                break;

            case EmittedFault.IdGetterThrowsOutOfMemory:
                il.Emit(OpCodes.Newobj, typeof(OutOfMemoryException).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Throw);
                break;

            case EmittedFault.IdGetterThrowsCanceled:
                il.Emit(OpCodes.Newobj, typeof(OperationCanceledException).GetConstructor(Type.EmptyTypes)!);
                il.Emit(OpCodes.Throw);
                break;

            default:
                il.Emit(OpCodes.Ldstr, pluginId);
                il.Emit(OpCodes.Call, typeof(PluginId).GetMethod(nameof(PluginId.FromString), [typeof(string)])!);
                il.Emit(OpCodes.Ret);
                break;
        }

        var property = type.DefineProperty("Id", PropertyAttributes.None, typeof(PluginId), null);
        property.SetGetMethod(getter);

        type.DefineMethodOverride(getter, typeof(IPluginModule).GetProperty(nameof(IPluginModule.Id))!.GetMethod!);
    }

    private static void EmitConfigure(TypeBuilder type, EmittedBehavior behavior, ConstructorInfo? handler)
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

            case EmittedBehavior.SubscribeTwiceToAPlatformEvent:
                var subscribe = typeof(IPluginRegistry)
                    .GetMethod(nameof(IPluginRegistry.AddEventHandler))!
                    .MakeGenericMethod(typeof(ProviderDefinitionChanged));

                for (var registration = 0; registration < 2; registration++)
                {
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Callvirt, typeof(IPluginContext).GetProperty(nameof(IPluginContext.Registry))!.GetMethod!);
                    il.Emit(OpCodes.Newobj, handler!);
                    il.Emit(OpCodes.Callvirt, subscribe);
                    il.Emit(OpCodes.Pop);
                }

                il.Emit(OpCodes.Ret);
                break;

            default:
                il.Emit(OpCodes.Ret);
                break;
        }

        type.DefineMethodOverride(configure, typeof(IPluginModule).GetMethod(nameof(IPluginModule.Configure))!);
    }
}
