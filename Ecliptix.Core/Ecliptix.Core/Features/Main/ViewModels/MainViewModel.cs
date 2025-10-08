using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;

namespace Ecliptix.Core.Features.Main.ViewModels;

public class MainViewModel : Core.MVVM.ViewModelBase
{
    private readonly ILogoutService _logoutService;

    public MainViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService)
        : base(systemEventService, networkProvider, localizationService)
    {
        _logoutService = logoutService;

        LogoutCommand = new AsyncRelayCommand(ExecuteLogoutAsync);
    }

    public ICommand LogoutCommand { get; }

    private async Task ExecuteLogoutAsync()
    {
        try
        {
            
            Result<Unit, LogoutFailure> result = await _logoutService.LogoutAsync(
                LogoutReason.UserInitiated);

            if (result.IsErr)
            {
                LogoutFailure failure = result.UnwrapErr();

               
                return;
            }

          
        }
        catch (Exception)
        {
            
        }
    }
}