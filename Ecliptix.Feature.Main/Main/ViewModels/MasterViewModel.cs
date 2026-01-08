using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.ViewModels.Core;
using Ecliptix.Core.ViewModels.Navigation;
using Ecliptix.Feature.Chats.Chats.Views;
using Ecliptix.Feature.NewContent.NewContent.ViewModels;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Splat;
using Ecliptix.Core.Modularity.Abstractions.Main;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Feature.Main.Main.ViewModels;

public sealed class MasterViewModel : ViewModelBase, IMainHost
{
    private readonly IModuleViewFactory _moduleViewFactory;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    private int _currentViewIndex;

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
    [Reactive] public bool IsOverlayVisible { get; set; }

    [Reactive] public bool IsOverlayOpen { get; set; }

    [Reactive] public object? OverlayContent { get; set; }
    [Reactive] public object? SuggestionsContent { get; set; }
    [Reactive] public bool IsTransitioning { get; set; }

    public ConnectivityNotificationViewModel ConnectivityNotification { get; }
    public NavigationSidebarViewModel NavigationSidebar { get; }
    public ReactiveCommand<SystemU, SystemU> CloseOverlayCommand { get; }

    public MasterViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IModuleViewFactory moduleViewFactory,
        ILogoutService logoutService,
        IProfileMenuService profileMenuService,
        IApplicationSecureStorageProvider storageProvider,
        MainWindowViewModel mainWindowViewModel,
        IGlobalModalService globalModalService)
        : base(networkProvider, localizationService, globalModalService)
    {
        _moduleViewFactory = moduleViewFactory;
        ConnectivityNotification = mainWindowViewModel.ConnectivityNotification;
        SuggestionsContent = mainWindowViewModel.SuggestionsViewModel;
        NavigationSidebar = new NavigationSidebarViewModel(networkProvider, localizationService, logoutService, profileMenuService, storageProvider);
        IMessageBus? messageBus = Locator.Current?.GetService<IMessageBus>();

        Dispatcher.UIThread.Post(() =>
        {
            ConversationView dummy = new();
        });

        CloseOverlayCommand = ReactiveCommand.Create(() =>
        {
            messageBus?.PublishAsync(new CloseOverlayEvent());
        });

        if (messageBus != null)
        {
            messageBus.Subscribe<OpenCreateWizardEvent>(async _ =>
            {
                await HandleOpenWizard();
            }).DisposeWith(_disposables);

            messageBus.Subscribe<CloseOverlayEvent>(async evt =>
            {

                await HandleCloseOverlayEvent();
            }).DisposeWith(_disposables);
        }

        this.WhenAnyValue(x => x.IsTransitioning)
            .Subscribe(isAnimating =>
            {
                NavigationSidebar.IsParentAnimating = isAnimating;
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.NavigationSidebar.SelectedMenuItem)
            .WhereNotNull()
            .Subscribe(async menuItem =>
            {
                ModuleIdentifier? moduleId = menuItem.Id switch
                {
                    "feed" => ModuleIdentifier.FEED,
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

    private async Task HandleCloseOverlayEvent()
    {
        IsOverlayOpen = false;

        await Task.Delay(250);

        IsOverlayVisible = false;
        OverlayContent = null;
    }

    private async Task HandleOpenWizard()
    {
        OverlayContent = new CreateWizardViewModel();

        IsOverlayVisible = true;
        await Task.Delay(10);
        IsOverlayOpen = true;
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
