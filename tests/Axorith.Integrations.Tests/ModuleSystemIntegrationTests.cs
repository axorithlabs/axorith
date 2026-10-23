using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using Autofac;
using Autofac.Core.Lifetime;
using Autofac.Extensions.DependencyInjection;
using Axorith.Contracts;
using Axorith.Core.Services;
using Axorith.Core.Services.Abstractions;
using Axorith.Host.Services;
using Axorith.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ModuleDefinition = Axorith.Sdk.ModuleDefinition;

namespace Axorith.Integrations.Tests;

public partial class HostGrpcEndToEndTests
{
    [Fact]
    public async Task ConcurrentBeginEditOwnsOneModuleScopeAndEndEditReleasesItsSettings()
    {
        var (_, _, _, modules, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        {
            var root = factory.Services.GetAutofacRoot();
            var scopes = new ConcurrentBag<ILifetimeScope>();
            void OnScope(object? sender, LifetimeScopeBeginningEventArgs args)
            {
                if (!args.LifetimeScope.IsRegistered<ModuleDefinition>()) return;
                scopes.Add(args.LifetimeScope);
                Thread.Sleep(30);
            }

            root.ChildLifetimeScopeBeginning += OnScope;
            var instanceId = Guid.NewGuid();
            try
            {
                var replies = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
                    modules.BeginEditAsync(new BeginEditRequest
                    {
                        ModuleId = factory.SiteBlockerModuleId.ToString(),
                        ModuleInstanceId = instanceId.ToString()
                    }).ResponseAsync));

                Assert.All(replies, reply => Assert.True(reply.Result.Success, reply.Result.Message));
                var scope = Assert.Single(scopes);
                var setting = scope.Resolve<IModule>().GetSettings().First();
                Assert.True((await modules.EndEditAsync(new EndEditRequest
                {
                    ModuleInstanceId = instanceId.ToString()
                })).Success);
                Assert.Throws<ObjectDisposedException>(() => { setting.GetCurrentValueAsObject(); });
            }
            finally
            {
                root.ChildLifetimeScopeBeginning -= OnScope;
                await modules.EndEditAsync(new EndEditRequest { ModuleInstanceId = instanceId.ToString() });
            }
        }
    }

    [Fact]
    public async Task SettingsRequestsDoNotReuseAnotherEditorsValues()
    {
        var (_, _, _, modules, channel) = await CreateAuthenticatedClientsAsync();
        using (channel)
        {
            var moduleId = factory.SiteBlockerModuleId.ToString();
            var original = await modules.GetModuleSettingsAsync(new GetModuleSettingsRequest { ModuleId = moduleId });
            var instanceId = Guid.NewGuid().ToString();
            try
            {
                var edit = new BeginEditRequest { ModuleId = moduleId, ModuleInstanceId = instanceId };
                edit.InitialValues.Add(new SettingValue { Key = "Mode", StringValue = "AllowList" });
                Assert.True((await modules.BeginEditAsync(edit)).Result.Success);

                var current = await modules.GetModuleSettingsAsync(new GetModuleSettingsRequest { ModuleId = moduleId });
                Assert.Equal(original.Settings.Single(setting => setting.Key == "Mode").StringValue,
                    current.Settings.Single(setting => setting.Key == "Mode").StringValue);
            }
            finally
            {
                await modules.EndEditAsync(new EndEditRequest { ModuleInstanceId = instanceId });
            }
        }
    }
}

public sealed class ModuleSystemIntegrationTests
{
    [Fact]
    public async Task SandboxCapacityEvictsTheOldestInstanceAndRejectsModuleIdChanges()
    {
        await using var host = new HostTestFactory();
        using var client = host.CreateDefaultClient();
        var manager = host.Services.GetRequiredService<DesignTimeSandboxManager>();
        await manager.EnsureAsync(Guid.Empty, host.SiteBlockerModuleId, new Dictionary<string, string?>(), CancellationToken.None);
        var first = manager.GetModule(Guid.Empty)!;
        var setting = first.GetSettings().First();
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.EnsureAsync(
            Guid.Empty, host.AppBlockerModuleId, new Dictionary<string, string?>(), CancellationToken.None));
        Assert.Same(first, manager.GetModule(Guid.Empty));

        for (var index = 0; index < 5; index++)
            await manager.EnsureAsync(Guid.NewGuid(), host.SiteBlockerModuleId, new Dictionary<string, string?>(), CancellationToken.None);

        Assert.Null(manager.GetModule(Guid.Empty));
        Assert.Throws<ObjectDisposedException>(() => { setting.GetCurrentValueAsObject(); });
    }

    [Fact]
    public async Task FailedActivationDisposesTheActualAutofacScope()
    {
        await using var host = new HostTestFactory();
        InstallModule(host.TestDataPath, "HomeAssistant", Guid.NewGuid());
        using var root = new ContainerBuilder().Build();
        var closedScopes = 0;
        root.ChildLifetimeScopeBeginning += (_, args) =>
            args.LifetimeScope.CurrentScopeEnding += (_, _) => Interlocked.Increment(ref closedScopes);
        using var registry = new ModuleRegistry(root, new ModuleLoader(NullLogger<ModuleLoader>.Instance),
            [Path.Combine(host.TestDataPath, "modules")], [], NullLogger<ModuleRegistry>.Instance);
        await registry.InitializeAsync(CancellationToken.None);
        var definition = registry.GetAllDefinitions().Single(module => module.Name == "HomeAssistant");

        Assert.Equal((null, null), registry.CreateInstance(definition.Id));
        Assert.Equal(1, closedScopes);
    }

    [Fact]
    public async Task UnsupportedSdkManifestIsRejectedWhileLegacyManifestStillLoads()
    {
        await using var host = new HostTestFactory();
        var modulePath = InstallModule(host.TestDataPath, "HomeAssistant", Guid.NewGuid(), sdkApiVersion: 2);
        var loader = new ModuleLoader(NullLogger<ModuleLoader>.Instance);
        var definitions = await loader.LoadModuleDefinitionsAsync([Path.GetDirectoryName(modulePath)!], CancellationToken.None);
        try
        {
            Assert.Equal(2, definitions.Count);
            Assert.DoesNotContain(definitions, module => module.Name == "HomeAssistant");
            Assert.All(definitions, module => Assert.NotNull(module.ModuleType));
        }
        finally
        {
            foreach (var definition in definitions)
            {
                definition.ModuleType = null;
                definition.LoadContext?.Unload();
                definition.LoadContext = null;
            }
        }
    }

    [Fact]
    public void RealModulesWithPrivateDependenciesUnloadAfterHostShutdown()
    {
        var (references, directory) = LoadAndReleaseModules();
        for (var attempt = 0; attempt < 30 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(100);
        }

        Assert.All(references, reference => Assert.False(reference.IsAlive, "A module context still has GC roots."));
        Directory.Delete(directory, recursive: true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference[] Contexts, string Directory) LoadAndReleaseModules()
    {
        using var host = new HostTestFactory();
        InstallModule(host.TestDataPath, "HomeAssistant", Guid.NewGuid());
        InstallModule(host.TestDataPath, "ApplicationLauncher", Guid.NewGuid());
        using var client = host.CreateDefaultClient();
        var registry = host.Services.GetRequiredService<IModuleRegistry>();
        var definitions = registry.GetAllDefinitions();
        Assert.Equal(4, definitions.Count);
        var references = definitions.Select(definition => new WeakReference(definition.LoadContext!)).ToArray();
        foreach (var definition in definitions)
        {
            var (module, scope) = registry.CreateInstance(definition.Id);
            Assert.NotNull(module);
            Axorith.Sdk.Settings.ISetting[] settings;
            using (scope)
            {
                module.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
                settings = module.GetSettings().ToArray();
                Assert.NotEmpty(settings);
                Assert.Same(typeof(IModule).Assembly,
                    definition.LoadContext!.LoadFromAssemblyName(typeof(IModule).Assembly.GetName()));
                if (definition.Name == "ApplicationLauncher")
                {
                    var dependency = definition.LoadContext.LoadFromAssemblyName(new AssemblyName("SpotifyAPI.Web"));
                    Assert.Same(definition.LoadContext, AssemblyLoadContext.GetLoadContext(dependency));
                    Assert.NotNull(dependency.GetType("SpotifyAPI.Web.SpotifyClientConfig")!
                        .GetMethod("CreateDefault", Type.EmptyTypes)!.Invoke(null, null));
                }
            }

            Assert.All(settings, setting =>
                Assert.Throws<ObjectDisposedException>(() => { setting.GetCurrentValueAsObject(); }));
        }

        return (references, host.TestDataPath);
    }

    private static string InstallModule(string dataPath, string name, Guid id, int sdkApiVersion = 1)
    {
        var buildDirectory = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var source = Path.Combine(buildDirectory, "Modules", name);
        var target = Path.Combine(dataPath, "modules", name);
        Directory.CreateDirectory(target);
        foreach (var sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetRelativePath(source, sourceFile).StartsWith("obj" + Path.DirectorySeparatorChar)) continue;
            var targetFile = Path.Combine(target, Path.GetRelativePath(source, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(sourceFile, targetFile, overwrite: true);
        }

        File.WriteAllText(Path.Combine(target, "module.json"), JsonSerializer.Serialize(new
        {
            id, name, sdkApiVersion,
            platforms = new[] { OperatingSystem.IsWindows() ? "Windows" : "Linux" },
            assembly = $"Axorith.Module.{name}.dll"
        }));
        return target;
    }
}
