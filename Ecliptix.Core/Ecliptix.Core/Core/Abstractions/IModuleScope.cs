namespace Ecliptix.Core.Modularity.Abstractions;

public interface IModuleScope : IDisposable
{
    IServiceProvider ServiceProvider { get; }

    string ModuleName { get; }
}
