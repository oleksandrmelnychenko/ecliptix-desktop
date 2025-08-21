using System;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Features.Main.ViewModels;

public class MainViewModel : Core.MVVM.ViewModelBase
{
    public MainViewModel(ISystemEventService systemEventService, NetworkProvider networkProvider,
        ILocalizationService localizationService)
        : base(systemEventService, networkProvider, localizationService)
    {

    }
}