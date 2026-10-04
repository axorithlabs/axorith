using System.Reflection;
using System.Runtime.Loader;

namespace Axorith.Core.Services;

internal class ModuleAssemblyLoadContext(string modulePath) : AssemblyLoadContext(true)
{
    private readonly AssemblyDependencyResolver _resolver = new(modulePath);

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
