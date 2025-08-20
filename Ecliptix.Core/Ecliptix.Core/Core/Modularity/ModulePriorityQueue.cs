using System;
using System.Collections.Generic;
using System.Linq;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Modularity;
public class ModulePriorityQueue
{
    private readonly SortedList<int, Queue<IModule>> _priorityQueues = new();
    private readonly HashSet<string> _processedModules = new();
    private readonly Dictionary<string, IModule> _moduleMap = new();

    
    
    
    public void EnqueueModules(IEnumerable<IModule> modules)
    {
        IModule[] moduleArray = modules.ToArray();
        
        
        foreach (IModule module in moduleArray)
        {
            _moduleMap[module.Name] = module;
        }

        
        List<IModule> sortedModules = TopologicalSort(moduleArray);
        
        foreach (IModule module in sortedModules)
        {
            int priority = module.Priority;
            
            if (!_priorityQueues.ContainsKey(priority))
            {
                _priorityQueues[priority] = new Queue<IModule>();
            }
            
            _priorityQueues[priority].Enqueue(module);
        }
    }

    
    
    
    public IModule? DequeueNext()
    {
        
        foreach (int priority in _priorityQueues.Keys.OrderByDescending(p => p))
        {
            Queue<IModule> queue = _priorityQueues[priority];
            
            while (queue.Count > 0)
            {
                IModule module = queue.Dequeue();
                
                
                if (AreDependenciesLoaded(module))
                {
                    _processedModules.Add(module.Name);
                    return module;
                }
                
                
                queue.Enqueue(module);
                break; 
            }
        }

        return null;
    }

    
    
    
    public int Count => _priorityQueues.Values.Sum(q => q.Count);

    
    
    
    private bool AreDependenciesLoaded(IModule module)
    {
        return module.DependsOn.All(dep => _processedModules.Contains(dep));
    }

    
    
    
    private List<IModule> TopologicalSort(IModule[] modules)
    {
        List<IModule> result = new();
        HashSet<string> visited = new();
        HashSet<string> visiting = new();

        void Visit(IModule module)
        {
            if (visiting.Contains(module.Name))
            {
                throw new InvalidOperationException($"Circular dependency detected involving module: {module.Name}");
            }

            if (visited.Contains(module.Name))
            {
                return;
            }

            visiting.Add(module.Name);

            
            foreach (string dependency in module.DependsOn)
            {
                if (_moduleMap.TryGetValue(dependency, out IModule? depModule))
                {
                    Visit(depModule);
                }
            }

            visiting.Remove(module.Name);
            visited.Add(module.Name);
            result.Add(module);
        }

        foreach (IModule module in modules)
        {
            Visit(module);
        }

        return result;
    }
}