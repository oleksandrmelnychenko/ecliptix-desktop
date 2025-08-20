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
    IModule? GetModule(string name);
    bool HasModule(string name);
    
    Task<IEnumerable<IModule>> GetLoadOrderAsync();
    Task<IEnumerable<IModule>> GetRequiredModulesAsync(string moduleName);
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
        if (_modules.Any(m => m.Name == module.Name))
        {
            Log.Warning("Module {ModuleName} is already registered", module.Name);
            return;
        }

        _modules.Add(module);
        Log.Debug("Module {ModuleName} added to catalog", module.Name);
    }

    public void AddModules(params IModule[] modules)
    {
        foreach (IModule module in modules)
        {
            AddModule(module);
        }
    }

    public IReadOnlyList<IModule> GetModules() => _modules.AsReadOnly();

    public IModule? GetModule(string name) => 
        _modules.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool HasModule(string name) => GetModule(name) != null;

    public async Task<IEnumerable<IModule>> GetLoadOrderAsync()
    {
        return await _dependencyResolver.ResolveLoadOrderAsync(_modules);
    }

    public async Task<IEnumerable<IModule>> GetRequiredModulesAsync(string moduleName)
    {
        return await _dependencyResolver.GetRequiredModulesAsync(moduleName, _modules);
    }
}