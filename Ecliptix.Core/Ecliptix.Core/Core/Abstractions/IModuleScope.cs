using System;

namespace Ecliptix.Core.Core.Abstractions;
public interface IModuleScope : IDisposable
{
    IServiceProvider ServiceProvider { get; }

    IModuleResourceConstraints Constraints { get; }

    string ModuleName { get; }
}