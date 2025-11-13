using System.Reflection;
using System.Runtime.Loader;

namespace Axorith.Core.Services;

/// <summary>
///     A custom AssemblyLoadContext that resolves dependencies from the module's own directory first.
/// </summary>
internal class ModuleAssemblyLoadContext(string modulePath) : AssemblyLoadContext(true)
{
    private readonly AssemblyDependencyResolver _resolver = new(modulePath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath != null
            ? LoadFromAssemblyPath(assemblyPath)
            :
            // If not found in the module's directory, fall back to the default context.
            null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}