using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;

public class ModuleDependencyResolver
{
    public async Task<IEnumerable<IModule>> ResolveLoadOrderAsync(IEnumerable<IModule> modules)
    {
        await Task.CompletedTask;

        List<IModule> moduleList = modules.ToList();
        Dictionary<string, IModule> moduleMap = moduleList.ToDictionary(m => m.Id.ToName(), m => m);

        ValidateDependencies(moduleList, moduleMap);

        return TopologicalSort(moduleList, moduleMap);
    }

    public async Task<IEnumerable<IModule>> GetRequiredModulesAsync(string moduleName, IEnumerable<IModule> availableModules)
    {
        await Task.CompletedTask;

        Dictionary<string, IModule> moduleMap = availableModules.ToDictionary(m => m.Id.ToName(), m => m);

        if (!moduleMap.TryGetValue(moduleName, out IModule? targetModule))
            throw new InvalidOperationException($"Module {moduleName} not found");

        HashSet<string> required = new();
        GetDependenciesRecursive(targetModule, moduleMap, required);

        return required.Select(name => moduleMap[name]);
    }

    private void ValidateDependencies(List<IModule> modules, Dictionary<string, IModule> moduleMap)
    {
        foreach (IModule module in modules)
        {
            foreach (ModuleIdentifier dependency in module.Manifest.Dependencies)
            {
                if (!moduleMap.ContainsKey(dependency.ToName()))
                {
                    throw new InvalidOperationException(
                        $"Module '{module.Id.ToName()}' depends on '{dependency.ToName()}', but '{dependency.ToName()}' is not registered");
                }
            }
        }

        DetectCircularDependencies(modules, moduleMap);
    }

    private void DetectCircularDependencies(List<IModule> modules, Dictionary<string, IModule> moduleMap)
    {
        HashSet<string> visited = new();
        HashSet<string> recursionStack = new();

        foreach (IModule module in modules)
        {
            if (!visited.Contains(module.Id.ToName()))
            {
                if (HasCircularDependency(module.Id.ToName(), moduleMap, visited, recursionStack))
                {
                    throw new InvalidOperationException($"Circular dependency detected starting from module '{module.Id.ToName()}'");
                }
            }
        }
    }

    private bool HasCircularDependency(string moduleName, Dictionary<string, IModule> moduleMap,
        HashSet<string> visited, HashSet<string> recursionStack)
    {
        visited.Add(moduleName);
        recursionStack.Add(moduleName);

        if (moduleMap.TryGetValue(moduleName, out IModule? module))
        {
            foreach (ModuleIdentifier dependency in module.Manifest.Dependencies)
            {
                if (!visited.Contains(dependency.ToName()))
                {
                    if (HasCircularDependency(dependency.ToName(), moduleMap, visited, recursionStack))
                        return true;
                }
                else if (recursionStack.Contains(dependency.ToName()))
                {
                    return true;
                }
            }
        }

        recursionStack.Remove(moduleName);
        return false;
    }

    private IEnumerable<IModule> TopologicalSort(List<IModule> modules, Dictionary<string, IModule> moduleMap)
    {
        Dictionary<string, int> inDegree = modules.ToDictionary(m => m.Id.ToName(), _ => 0);

        foreach (IModule module in modules)
        {
            foreach (ModuleIdentifier dependency in module.Manifest.Dependencies)
            {
                if (inDegree.ContainsKey(dependency.ToName()))
                {
                    inDegree[module.Id.ToName()]++;
                }
            }
        }

        Queue<string> queue = new(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        List<IModule> result = new();

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            result.Add(moduleMap[current]);

            IModule currentModule = moduleMap[current];

            foreach (IModule module in modules)
            {
                if (module.Manifest.Dependencies.Select(d => d.ToName()).Contains(current))
                {
                    inDegree[module.Id.ToName()]--;
                    if (inDegree[module.Id.ToName()] == 0)
                    {
                        queue.Enqueue(module.Id.ToName());
                    }
                }
            }
        }

        if (result.Count != modules.Count)
        {
            throw new InvalidOperationException("Unable to resolve module dependencies - circular dependency detected");
        }

        return result.OrderBy(m => m.Manifest.Priority).ThenBy(m => m.Id.ToName());
    }

    private void GetDependenciesRecursive(IModule module, Dictionary<string, IModule> moduleMap, HashSet<string> required)
    {
        foreach (ModuleIdentifier dependency in module.Manifest.Dependencies)
        {
            if (!required.Contains(dependency.ToName()) && moduleMap.TryGetValue(dependency.ToName(), out IModule? depModule))
            {
                required.Add(dependency.ToName());
                GetDependenciesRecursive(depModule, moduleMap, required);
            }
        }
    }
}