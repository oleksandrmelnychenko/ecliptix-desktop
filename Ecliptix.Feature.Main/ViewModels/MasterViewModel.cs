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
using Serilog;
using Splat;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Abstractions.Membership;
using ReactiveUI.Fody.Helpers;
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

    [Reactive] public bool ShowSuggestions { get; set; }

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

        Task.Run(() =>
        {
            Dispatcher.UIThread.Post(() => { _ = new ConversationView(); }, DispatcherPriority.Background);
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
            .SelectMany(async menuItem =>
            {
                try
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
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MASTER-VM] Error handling SelectedMenuItem change");
                }
                return System.Reactive.Unit.Default;
            })
            .Subscribe()
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
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    CurrentView = viewOption.Value;
                    if (moduleId == ModuleIdentifier.FEED)
                    {
                        ShowSuggestions = true;
                    }
                    else
                    {
                        ShowSuggestions = false;
                    }
                });
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
