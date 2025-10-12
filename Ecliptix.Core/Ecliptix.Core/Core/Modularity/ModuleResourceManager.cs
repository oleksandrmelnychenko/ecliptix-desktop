using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Ecliptix.Core.Core.Abstractions;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;

public sealed class ModuleServiceContext
{
    private readonly IServiceProvider _parentProvider;

    public ModuleServiceContext(IServiceProvider parentProvider)
    {
        _parentProvider = parentProvider ?? throw new ArgumentNullException(nameof(parentProvider));
    }

    public T GetParentService<T>() where T : notnull => _parentProvider.GetRequiredService<T>();
}

public static class ModuleServiceExtensions
{
    public static IServiceCollection ForwardParentService<T>(this IServiceCollection services) where T : class
    {
        services.AddSingleton(sp =>
        {
            ModuleServiceContext context = sp.GetRequiredService<ModuleServiceContext>();
            return context.GetParentService<T>();
        });
        return services;
    }

    public static IServiceCollection ForwardParentService<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddSingleton<TService>(sp =>
        {
            ModuleServiceContext context = sp.GetRequiredService<ModuleServiceContext>();
            return context.GetParentService<TImplementation>();
        });
        return services;
    }
}

public class ModuleResourceManager : IDisposable
{
    private readonly ConcurrentDictionary<string, IModuleScope> _moduleScopes = new();
    private readonly IServiceProvider _rootServiceProvider;
    private bool _disposed;

    public ModuleResourceManager(IServiceProvider serviceProvider)
    {
        _rootServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IModuleScope CreateModuleScope(string moduleName, IModuleResourceConstraints constraints, Action<IServiceCollection>? configureServices = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name cannot be null or empty", nameof(moduleName));

        IServiceScope serviceScope;

        if (configureServices != null)
        {
            IServiceScope parentScope = _rootServiceProvider.CreateScope();
            ServiceCollection moduleServices = new();

            ModuleServiceContext context = new(parentScope.ServiceProvider);
            moduleServices.AddSingleton(context);

            AutoForwardCoreServices(moduleServices, context);

            configureServices(moduleServices);

            ServiceProvider moduleServiceProvider = moduleServices.BuildServiceProvider();
            serviceScope = new CompositeServiceScope(moduleServiceProvider, parentScope);
        }
        else
        {
            serviceScope = _rootServiceProvider.CreateScope();
        }

        ModuleScope moduleScope = new(moduleName, serviceScope);

        if (!_moduleScopes.TryAdd(moduleName, moduleScope))
        {
            moduleScope.Dispose();
            throw new InvalidOperationException($"Module scope for '{moduleName}' already exists");
        }

        Log.Information("Created module scope for {ModuleName}", moduleName);

        return moduleScope;
    }

    private void AutoForwardCoreServices(IServiceCollection moduleServices, ModuleServiceContext context)
    {
        moduleServices.AddSingleton(context.GetParentService<Core.Messaging.Services.ISystemEventService>());
        moduleServices.AddSingleton(context.GetParentService<Core.Messaging.Services.INetworkEventService>());
        moduleServices.AddSingleton(context.GetParentService<Core.Messaging.Services.IBottomSheetService>());
        moduleServices.AddSingleton(context.GetParentService<Core.Messaging.Services.ILanguageDetectionService>());
        moduleServices.AddSingleton(context.GetParentService<Infrastructure.Network.Core.Providers.NetworkProvider>());
        moduleServices.AddSingleton(context.GetParentService<Infrastructure.Network.Abstractions.Core.IInternetConnectivityObserver>());
        moduleServices.AddSingleton(context.GetParentService<Infrastructure.Network.Abstractions.Transport.IRpcMetaDataProvider>());
        moduleServices.AddSingleton(context.GetParentService<Infrastructure.Data.Abstractions.IApplicationSecureStorageProvider>());
        moduleServices.AddSingleton(context.GetParentService<Services.Abstractions.Core.ILocalizationService>());
        moduleServices.AddSingleton(context.GetParentService<Services.Abstractions.Core.IApplicationRouter>());
        moduleServices.AddSingleton(context.GetParentService<Services.Abstractions.Network.IUiDispatcher>());
        moduleServices.AddSingleton(context.GetParentService<Services.Abstractions.Membership.ILogoutService>());
        moduleServices.AddSingleton(context.GetParentService<Services.Abstractions.Authentication.IAuthenticationService>());
        moduleServices.AddSingleton(context.GetParentService<Services.Abstractions.Authentication.IOpaqueRegistrationService>());
        moduleServices.AddSingleton(context.GetParentService<Services.Abstractions.Authentication.IPasswordRecoveryService>());
        moduleServices.AddSingleton(context.GetParentService<Ecliptix.Core.Controls.Core.NetworkStatusNotificationViewModel>());

        Log.Debug("Auto-forwarded core services to module service collection");
    }

    public bool RemoveModuleScope(string moduleName)
    {
        if (_moduleScopes.TryRemove(moduleName, out IModuleScope? scope))
        {
            scope.Dispose();
            Log.Information("Removed module scope for {ModuleName}", moduleName);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        foreach (KeyValuePair<string, IModuleScope> kvp in _moduleScopes)
        {
            try
            {
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error disposing module scope for {ModuleName}", kvp.Key);
            }
        }

        _moduleScopes.Clear();
    }
}
