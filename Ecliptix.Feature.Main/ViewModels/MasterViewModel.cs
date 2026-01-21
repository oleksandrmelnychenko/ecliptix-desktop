using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.MVVM;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Shell.Messaging;
using Ecliptix.Core.Shell.ViewModels;
using Ecliptix.Core.Controls.Navigation;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Main;
using Ecliptix.Feature.Chats.Views;
using Ecliptix.Feature.NewContent.ViewModels;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Splat;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Abstractions.Membership;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Feature.Main.ViewModels;

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

    private UserControl? _currentView;
    private bool _isLoadingView;
    private bool _isTransitionReversed;
    private bool _isOverlayVisible;
    private bool _isOverlayOpen;
    private object? _overlayContent;
    private object? _suggestionsContent;
    private bool _isTransitioning;

    public UserControl? CurrentView
    {
        get => _currentView;
        set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    public bool IsLoadingView
    {
        get => _isLoadingView;
        set => this.RaiseAndSetIfChanged(ref _isLoadingView, value);
    }

    public bool IsTransitionReversed
    {
        get => _isTransitionReversed;
        set => this.RaiseAndSetIfChanged(ref _isTransitionReversed, value);
    }

    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set => this.RaiseAndSetIfChanged(ref _isOverlayVisible, value);
    }

    public bool IsOverlayOpen
    {
        get => _isOverlayOpen;
        set => this.RaiseAndSetIfChanged(ref _isOverlayOpen, value);
    }

    public object? OverlayContent
    {
        get => _overlayContent;
        set => this.RaiseAndSetIfChanged(ref _overlayContent, value);
    }

    public object? SuggestionsContent
    {
        get => _suggestionsContent;
        set => this.RaiseAndSetIfChanged(ref _suggestionsContent, value);
    }

    public bool IsTransitioning
    {
        get => _isTransitioning;
        set => this.RaiseAndSetIfChanged(ref _isTransitioning, value);
    }

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
        Log.Information("[MASTER-VM] NavigationSidebar created, initial SelectedMenuItem={Id}",
            NavigationSidebar.SelectedMenuItem?.Id ?? "null");

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

        Log.Information("[MASTER-VM] Setting up SelectedMenuItem subscription...");
        this.WhenAnyValue(x => x.NavigationSidebar.SelectedMenuItem)
            .WhereNotNull()
            .Do(menuItem => Log.Information("[MASTER-VM] WhenAnyValue triggered for SelectedMenuItem: {MenuItemId}", menuItem.Id))
            .SelectMany(async menuItem =>
            {
                try
                {
                    Log.Information("[MASTER-VM] SelectedMenuItem changed to: {MenuItemId}", menuItem.Id);

                    ModuleIdentifier? moduleId = menuItem.Id switch
                    {
                        "feed" => ModuleIdentifier.FEED,
                        "chats" => ModuleIdentifier.CHATS,
                        "settings" => ModuleIdentifier.SETTINGS,
                        "profile" => ModuleIdentifier.PROFILE,
                        _ => null
                    };

                    Log.Information("[MASTER-VM] Resolved ModuleIdentifier: {ModuleId}", moduleId?.ToString() ?? "null");

                    if (moduleId.HasValue)
                    {
                        CalculateTransitionDirection(moduleId.Value);
                        Log.Information("[MASTER-VM] Loading module view for: {ModuleName}", moduleId.Value.ToName());
                        await LoadModuleViewAsync(moduleId.Value);
                        Log.Information("[MASTER-VM] Module view loaded, CurrentView is now: {ViewType}", CurrentView?.GetType().Name ?? "null");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MASTER-VM] Error handling SelectedMenuItem change");
                }
                return System.Reactive.Unit.Default;
            })
            .Subscribe()
            .DisposeWith(_disposables);
        Log.Information("[MASTER-VM] SelectedMenuItem subscription set up");
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
        Log.Information("[MASTER-VM] LoadModuleViewAsync called for moduleId={ModuleId}", moduleId);
        IsLoadingView = true;

        try
        {
            Log.Information("[MASTER-VM] Calling CreateViewForModuleAsync...");
            Option<UserControl> viewOption = await _moduleViewFactory.CreateViewForModuleAsync(moduleId);

            Log.Information("[MASTER-VM] CreateViewForModuleAsync returned, IsSome={IsSome}", viewOption.IsSome);

            if (viewOption.IsSome)
            {
                Log.Information("[MASTER-VM] Setting CurrentView to {ViewType}", viewOption.Value?.GetType().Name ?? "null");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CurrentView = viewOption.Value;
                    Log.Information("[MASTER-VM] CurrentView set on UI thread successfully");
                });
            }
            else
            {
                Log.Warning("[MASTER-VM] No view returned from CreateViewForModuleAsync for {ModuleId}", moduleId);
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
