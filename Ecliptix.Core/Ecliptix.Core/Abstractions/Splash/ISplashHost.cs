using Avalonia.Controls;

namespace Ecliptix.Core.Modularity.Splash;

public interface ISplashHost : IDisposable
{
    Task<bool> IsSubscribedAsync { get; }
    Task PrepareForShutdownAsync();
    Window CreateWindow();
}
