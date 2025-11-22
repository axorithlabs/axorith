using System.Reflection;
using System.Runtime.Loader;

namespace Axorith.Core.Services;

/// <summary>
///     A custom AssemblyLoadContext that resolves dependencies from the module's own directory first.
///     It explicitly delegates loading of Shared assemblies to the default context to ensure type identity.
/// </summary>
internal class ModuleAssemblyLoadContext(string modulePath) : AssemblyLoadContext(true)
{
    private readonly AssemblyDependencyResolver _resolver = new(modulePath);

    // List of assemblies that MUST be loaded from the Default Context (Host)
    private static readonly HashSet<string> SharedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Axorith.Sdk",
        "Axorith.Shared.Platform",
        "Axorith.Shared.Utils",
        "Axorith.Shared.Exceptions"
    };

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name != null && SharedAssemblies.Contains(assemblyName.Name))
        {
            return null;
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

        return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}