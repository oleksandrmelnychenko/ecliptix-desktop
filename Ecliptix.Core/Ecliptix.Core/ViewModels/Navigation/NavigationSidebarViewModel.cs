using System;
using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Navigation;
using Ecliptix.Core.Services.Abstractions.Core;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Navigation;

public sealed partial class NavigationSidebarViewModel : Ecliptix.Core.Core.MVVM.ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public NavigationMenuItem? SelectedMenuItem { get; set; }
    [Reactive] public bool IsExpanded { get; set; }

    public ObservableCollection<NavigationMenuItem> MenuItems { get; }

    public ReactiveCommand<NavigationMenuItem, SystemU> NavigateCommand { get; }

    public NavigationSidebarViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService)
        : base(networkProvider, localizationService, null)
    {
        MenuItems = new ObservableCollection<NavigationMenuItem>
        {
            new NavigationMenuItem
            {
                Id = "home",
                Label = "Feed",
                IconPath = "FeedIconData",
                TooltipText = "Feed",
                Type = NavigationMenuItemType.Regular
            },
            new NavigationMenuItem
            {
                Id = "chats",
                Label = "Chats",
                IconPath = "ChatsIconData",
                TooltipText = "Chats",
                Type = NavigationMenuItemType.Regular
            },
            new NavigationMenuItem
            {
                Id = "settings",
                Label = "Settings",
                IconPath = "SettingsIconData",
                TooltipText = "Settings",
                Type = NavigationMenuItemType.Regular
            }
        };

        SelectedMenuItem = MenuItems[0];

        NavigateCommand = ReactiveCommand.Create<NavigationMenuItem, SystemU>(
            menuItem =>
            {
                if (SelectedMenuItem != menuItem)
                {
                    SelectedMenuItem?.IsSelected = false;
                    SelectedMenuItem = menuItem;
                    menuItem.IsSelected = true;
                }

                return SystemU.Default;
            });

        NavigateCommand
            .Subscribe()
            .DisposeWith(_disposables);

        SelectedMenuItem?.IsSelected = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            NavigateCommand?.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
