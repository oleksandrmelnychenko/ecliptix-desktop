namespace Ecliptix.Core.Modularity;

public interface IModuleScope : IDisposable
{
    IServiceProvider ServiceProvider { get; }

    string ModuleName { get; }
}
