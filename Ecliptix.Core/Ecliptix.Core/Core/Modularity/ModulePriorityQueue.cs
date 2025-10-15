using System;
using System.Collections.Generic;
using System.Linq;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Modularity;

internal class ModulePriorityQueue
{
    private const int MaxRetryAttempts = 100;

    private readonly SortedList<int, Queue<IModule>> _priorityQueues = new();
    private readonly HashSet<string> _processedModules = new();
    private readonly Dictionary<string, IModule> _moduleMap = new();
    private readonly Dictionary<string, int> _retryAttempts = new();

    public void EnqueueModules(IEnumerable<IModule> modules)
    {
        IModule[] moduleArray = modules.ToArray();

        foreach (IModule module in moduleArray)
        {
            _moduleMap[module.Id.ToName()] = module;
            _retryAttempts[module.Id.ToName()] = 0;
        }

        List<IModule> sortedModules = TopologicalSort(moduleArray);

        foreach (IModule module in sortedModules)
        {
            int priority = module.Manifest.Priority;

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

            if (queue.Count == 0)
                continue;

            IModule module = queue.Dequeue();
            string moduleName = module.Id.ToName();

            if (AreDependenciesLoaded(module))
            {
                _processedModules.Add(moduleName);
                _retryAttempts.Remove(moduleName);
                return module;
            }

            _retryAttempts[moduleName]++;

            if (_retryAttempts[moduleName] >= MaxRetryAttempts)
            {
                string missingDeps = string.Join(", ",
                    module.Manifest.Dependencies
                        .Where(dep => !_processedModules.Contains(dep.ToName()))
                        .Select(dep => dep.ToName()));

                throw new InvalidOperationException(
                    $"Module '{moduleName}' dependencies not satisfied after {MaxRetryAttempts} attempts. Missing: {missingDeps}");
            }

            queue.Enqueue(module);
        }

        return null;
    }

    public int Count => _priorityQueues.Values.Sum(q => q.Count);

    private bool AreDependenciesLoaded(IModule module) =>
        module.Manifest.Dependencies.All(dep => _processedModules.Contains(dep.ToName()));

    private List<IModule> TopologicalSort(IModule[] modules)
    {
        List<IModule> result = new(modules.Length);
        HashSet<string> visited = new();
        HashSet<string> visiting = new();

        void Visit(IModule module)
        {
            string moduleName = module.Id.ToName();

            if (visiting.Contains(moduleName))
            {
                throw new InvalidOperationException($"Circular dependency detected involving module: {moduleName}");
            }

            if (visited.Contains(moduleName))
            {
                return;
            }

            visiting.Add(moduleName);

            foreach (ModuleIdentifier dependency in module.Manifest.Dependencies)
            {
                if (_moduleMap.TryGetValue(dependency.ToName(), out IModule? depModule))
                {
                    Visit(depModule);
                }
            }

            visiting.Remove(moduleName);
            visited.Add(moduleName);
            result.Add(module);
        }

        foreach (IModule module in modules)
        {
            Visit(module);
        }

        return result;
    }
}
