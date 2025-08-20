using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;

public interface IModuleCatalog
{
    void AddModule<TModule>() where TModule : class, IModule, new();
    void AddModule(IModule module);
    void AddModules(params IModule[] modules);

    IReadOnlyList<IModule> GetModules();
    IModule? GetModule(ModuleIdentifier id);
    IModule? GetModule(string name);
    bool HasModule(ModuleIdentifier id);
    bool HasModule(string name);

    Task<IEnumerable<IModule>> GetLoadOrderAsync();
    Task<IEnumerable<IModule>> GetRequiredModulesAsync(ModuleIdentifier moduleId);
}

public class ModuleCatalog : IModuleCatalog
{
    private readonly List<IModule> _modules = new();
    private readonly ModuleDependencyResolver _dependencyResolver = new();

    public void AddModule<TModule>() where TModule : class, IModule, new()
    {
        IModule module = new TModule();
        AddModule(module);
    }

    public void AddModule(IModule module)
    {
        if (_modules.Any(m => m.Id == module.Id))
        {
            Log.Warning("Module {ModuleName} is already registered", module.Id.ToName());
            return;
        }

        _modules.Add(module);
        Log.Debug("Module {ModuleName} added to catalog", module.Id.ToName());
    }

    public void AddModules(params IModule[] modules)
    {
        foreach (IModule module in modules)
        {
            AddModule(module);
        }
    }

    public IReadOnlyList<IModule> GetModules() => _modules.AsReadOnly();

    public IModule? GetModule(ModuleIdentifier id) =>
        _modules.FirstOrDefault(m => m.Id == id);

    public IModule? GetModule(string name) =>
        _modules.FirstOrDefault(m => string.Equals(m.Id.ToName(), name, StringComparison.OrdinalIgnoreCase));

    public bool HasModule(ModuleIdentifier id) => GetModule(id) != null;

    public bool HasModule(string name) => GetModule(name) != null;

    public async Task<IEnumerable<IModule>> GetLoadOrderAsync()
    {
        return await _dependencyResolver.ResolveLoadOrderAsync(_modules);
    }

    public async Task<IEnumerable<IModule>> GetRequiredModulesAsync(ModuleIdentifier moduleId)
    {
        return await _dependencyResolver.GetRequiredModulesAsync(moduleId.ToName(), _modules);
    }
}