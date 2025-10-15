using System;

namespace Ecliptix.Core.Core.Abstractions;
public interface IModuleScope : IDisposable
{
    IServiceProvider ServiceProvider { get; }

    string ModuleName { get; }
}
