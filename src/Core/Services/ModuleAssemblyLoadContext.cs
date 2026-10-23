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
        "Axorith.Shared.Exceptions",
        "Microsoft.Extensions.Http",
        "System.Reactive"
    };

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name != null && SharedAssemblies.Contains(assemblyName.Name))
            return LoadSharedAssembly(assemblyName);

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);

        return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
    }

    internal void ValidateSharedReferences(IEnumerable<AssemblyName> references)
    {
        foreach (var reference in references)
        {
            if (reference.Name != null && SharedAssemblies.Contains(reference.Name))
                LoadSharedAssembly(reference);
        }
    }

    private static Assembly LoadSharedAssembly(AssemblyName requested)
    {
        var assembly = Default.LoadFromAssemblyName(new AssemblyName(requested.Name!));
        var available = assembly.GetName();
        if (available.Version?.Major != requested.Version?.Major || available.Version < requested.Version ||
            !string.Equals(available.CultureName ?? "", requested.CultureName ?? "", StringComparison.OrdinalIgnoreCase) ||
            !(available.GetPublicKeyToken() ?? []).SequenceEqual(requested.GetPublicKeyToken() ?? []))
        {
            throw new FileLoadException(
                $"Incompatible shared module dependency '{requested.FullName}'. Host provides '{available.FullName}'. " +
                "Rebuild the module against the supported SDK and host contracts.");
        }

        return assembly;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : IntPtr.Zero;
    }
}
