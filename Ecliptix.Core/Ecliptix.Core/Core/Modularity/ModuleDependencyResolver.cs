using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;

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
                string depName = dependency.ToName();

                if (!visited.Contains(depName))
                {
                    if (HasCircularDependency(depName, moduleMap, visited, recursionStack))
                        return true;
                }
                else if (recursionStack.Contains(depName))
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
        Dictionary<string, HashSet<string>> dependents = new();

        foreach (IModule module in modules)
        {
            string moduleName = module.Id.ToName();
            dependents[moduleName] = new HashSet<string>();
        }

        foreach (IModule module in modules)
        {
            foreach (ModuleIdentifier dependency in module.Manifest.Dependencies)
            {
                string depName = dependency.ToName();
                if (dependents.ContainsKey(depName))
                {
                    dependents[depName].Add(module.Id.ToName());
                    inDegree[module.Id.ToName()]++;
                }
            }
        }

        Queue<string> queue = new(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        List<IModule> result = new(modules.Count);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            result.Add(moduleMap[current]);

            foreach (string dependent in dependents[current])
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                {
                    queue.Enqueue(dependent);
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
            string depName = dependency.ToName();
            if (!required.Contains(depName) && moduleMap.TryGetValue(depName, out IModule? depModule))
            {
                required.Add(depName);
                GetDependenciesRecursive(depModule, moduleMap, required);
            }
        }
    }
}
