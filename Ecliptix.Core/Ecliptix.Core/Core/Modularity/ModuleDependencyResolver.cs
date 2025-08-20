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
        Dictionary<string, IModule> moduleMap = moduleList.ToDictionary(m => m.Name, m => m);
        
        ValidateDependencies(moduleList, moduleMap);
        
        return TopologicalSort(moduleList, moduleMap);
    }

    public async Task<IEnumerable<IModule>> GetRequiredModulesAsync(string moduleName, IEnumerable<IModule> availableModules)
    {
        await Task.CompletedTask;
        
        Dictionary<string, IModule> moduleMap = availableModules.ToDictionary(m => m.Name, m => m);
        
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
            foreach (string dependency in module.DependsOn)
            {
                if (!moduleMap.ContainsKey(dependency))
                {
                    throw new InvalidOperationException(
                        $"Module '{module.Name}' depends on '{dependency}', but '{dependency}' is not registered");
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
            if (!visited.Contains(module.Name))
            {
                if (HasCircularDependency(module.Name, moduleMap, visited, recursionStack))
                {
                    throw new InvalidOperationException($"Circular dependency detected starting from module '{module.Name}'");
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
            foreach (string dependency in module.DependsOn)
            {
                if (!visited.Contains(dependency))
                {
                    if (HasCircularDependency(dependency, moduleMap, visited, recursionStack))
                        return true;
                }
                else if (recursionStack.Contains(dependency))
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
        Dictionary<string, int> inDegree = modules.ToDictionary(m => m.Name, _ => 0);
        
        foreach (IModule module in modules)
        {
            foreach (string dependency in module.DependsOn)
            {
                if (inDegree.ContainsKey(dependency))
                {
                    inDegree[module.Name]++;
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
                if (module.DependsOn.Contains(current))
                {
                    inDegree[module.Name]--;
                    if (inDegree[module.Name] == 0)
                    {
                        queue.Enqueue(module.Name);
                    }
                }
            }
        }
        
        if (result.Count != modules.Count)
        {
            throw new InvalidOperationException("Unable to resolve module dependencies - circular dependency detected");
        }
        
        return result.OrderBy(m => m.Priority).ThenBy(m => m.Name);
    }

    private void GetDependenciesRecursive(IModule module, Dictionary<string, IModule> moduleMap, HashSet<string> required)
    {
        foreach (string dependency in module.DependsOn)
        {
            if (!required.Contains(dependency) && moduleMap.TryGetValue(dependency, out IModule? depModule))
            {
                required.Add(dependency);
                GetDependenciesRecursive(depModule, moduleMap, required);
            }
        }
    }
}