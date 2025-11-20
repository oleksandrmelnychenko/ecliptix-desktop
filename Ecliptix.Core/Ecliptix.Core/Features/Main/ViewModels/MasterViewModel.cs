using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.ViewModels.Core;
using Ecliptix.Core.ViewModels.Navigation;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.Features.Main.ViewModels;

public sealed class MasterViewModel : ViewModelBase
{
    private readonly IModuleViewFactory _moduleViewFactory;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    private int _currentViewIndex = 0;

    private readonly Dictionary<ModuleIdentifier, int> _moduleOrder = new()
    {
        { ModuleIdentifier.FEED, 0 },
        { ModuleIdentifier.CHATS, 1 },
        { ModuleIdentifier.SETTINGS, 2 },
        { ModuleIdentifier.PROFILE, 99 }
    };

    [Reactive] public UserControl? CurrentView { get; set; }
    [Reactive] public bool IsLoadingView { get; set; }
    [Reactive] public bool IsTransitionReversed { get; set; }

    public ConnectivityNotificationViewModel ConnectivityNotification { get; }
    public NavigationSidebarViewModel NavigationSidebar { get; }

    public MasterViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IModuleViewFactory moduleViewFactory,
        ILogoutService logoutService,
        IProfileMenuService profileMenuService,
        IApplicationSecureStorageProvider storageProvider,
        MainWindowViewModel mainWindowViewModel)
        : base(networkProvider, localizationService)
    {
        _moduleViewFactory = moduleViewFactory;
        ConnectivityNotification = mainWindowViewModel.ConnectivityNotification;
        NavigationSidebar = new NavigationSidebarViewModel(networkProvider, localizationService, logoutService, profileMenuService, storageProvider);


        LoadInitialView();

        this.WhenAnyValue(x => x.NavigationSidebar.SelectedMenuItem)
            .WhereNotNull()
            .Subscribe(async menuItem =>
            {
                ModuleIdentifier? moduleId = menuItem.Id switch
                {
                    "home" => ModuleIdentifier.FEED,
                    "chats" => ModuleIdentifier.CHATS,
                    "settings" => ModuleIdentifier.SETTINGS,
                    "profile" => ModuleIdentifier.PROFILE,
                    _ => null
                };

                if (moduleId.HasValue)
                {
                    CalculateTransitionDirection(moduleId.Value);
                    await LoadModuleViewAsync(moduleId.Value);
                }
            })
            .DisposeWith(_disposables);
    }

    private async void LoadInitialView()
    {
        _currentViewIndex = _moduleOrder[ModuleIdentifier.FEED];
        await LoadModuleViewAsync(ModuleIdentifier.FEED);
    }

    private async Task LoadModuleViewAsync(ModuleIdentifier moduleId)
    {
        IsLoadingView = true;

        try
        {
            Option<UserControl> viewOption = await _moduleViewFactory.CreateViewForModuleAsync(moduleId);

            if (viewOption.IsSome)
            {
                CurrentView = viewOption.Value;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error loading view for module: {ModuleName}", moduleId.ToName());
        }
        finally
        {
            IsLoadingView = false;
        }
    }

    private void CalculateTransitionDirection(ModuleIdentifier nextModuleId)
    {
        if (_moduleOrder.TryGetValue(nextModuleId, out int nextIndex))
        {
            IsTransitionReversed = nextIndex < _currentViewIndex;

            _currentViewIndex = nextIndex;
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            NavigationSidebar.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
