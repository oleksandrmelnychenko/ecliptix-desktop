using Avalonia.Controls;

namespace Ecliptix.Core.Modularity.Abstractions.Splash;

public interface ISplashHost : IDisposable
{
    Task<bool> IsSubscribedAsync { get; }
    Task PrepareForShutdownAsync();
    Window CreateWindow();
}
