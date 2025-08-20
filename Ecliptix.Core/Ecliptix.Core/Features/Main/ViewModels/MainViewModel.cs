using System;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Features.Main.ViewModels;

public class MainViewModel : Core.MVVM.ViewModelBase
{
    public MainViewModel(ISystemEvents systemEvents, NetworkProvider networkProvider, 
        ILocalizationService localizationService)
        : base(systemEvents, networkProvider, localizationService)
    {
        
    }

    
}