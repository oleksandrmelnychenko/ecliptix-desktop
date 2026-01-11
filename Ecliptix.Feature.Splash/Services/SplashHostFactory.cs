using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Modularity.Splash;
using Ecliptix.Feature.Splash.ViewModels;
using Ecliptix.Feature.Splash.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Splash.Services;

public sealed class SplashHostFactory : ISplashHostFactory
{
    private readonly IServiceProvider _serviceProvider;

    public SplashHostFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ISplashHost Create() => ActivatorUtilities.CreateInstance<SplashHost>(_serviceProvider);
}

internal sealed class SplashHost : ISplashHost
{
    private readonly SplashWindowViewModel _viewModel;

    public SplashHost(SplashWindowViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public Task<bool> IsSubscribedAsync => _viewModel.IsSubscribed.Task;

    public Task PrepareForShutdownAsync() => _viewModel.PrepareForShutdownAsync();

    public Window CreateWindow() => new SplashWindow { DataContext = _viewModel };

    public void Dispose() => _viewModel.Dispose();
}
