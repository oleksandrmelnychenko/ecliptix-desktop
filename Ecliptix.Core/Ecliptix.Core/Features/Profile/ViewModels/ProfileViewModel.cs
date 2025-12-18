using System.Collections.ObjectModel;
using System.Linq;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Profile.ViewModels;

public sealed class ProfileMenuItem : ReactiveObject
{
    public string Title { get; set; }
    public string IconData { get; set; }

    public object ViewModel { get; set; }

    [Reactive] public bool IsSelected { get; set; }
}

public class ProfileViewModel : Core.MVVM.ViewModelBase, IActivatableViewModel
{

    [Reactive] public object CurrentProfilePage { get; set; }
    [Reactive] public bool IsTransitionReversed { get; set; }
    public ObservableCollection<ProfileMenuItem> MenuItems { get; }
    public ReactiveCommand<ProfileMenuItem, SystemU> NavigateCommand { get; private set; }
    public ViewModelActivator Activator { get; } = new();

    private int _currentPageIndex;


    public ProfileViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService,
        IApplicationSecureStorageProvider secureStorageProvider,
        IGlobalModalService globalModalService)
        : base(networkProvider, localizationService, globalModalService)
    {
        AboutProfileViewModel aboutVm = new();
        PersonalPostsProfileViewModel postsVm = new();
        PersonalImagesProfileViewModel imagesVm = new();
        PersonalLikesProfileViewModel likesVm = new();
        PersonalFavoritesViewModel favoritesVm = new();

       MenuItems = new ObservableCollection<ProfileMenuItem>
        {
            new()
            {
                Title = "About",
                IconData = "M11 7h2v2h-2zm0 4h2v6h-2zm1-9C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z",
                ViewModel = aboutVm,
                IsSelected = true
            },
            new()
            {
                Title = "Posts",
                IconData = "M4 10.5c-.83 0-1.5.67-1.5 1.5s.67 1.5 1.5 1.5 1.5-.67 1.5-1.5-.67-1.5-1.5-1.5zm0-6c-.83 0-1.5.67-1.5 1.5S3.17 7.5 4 7.5 5.5 6.83 5.5 6 4.83 4.5 4 4.5zm0 12c-.83 0-1.5.68-1.5 1.5s.68 1.5 1.5 1.5 1.5-.68 1.5-1.5-.67-1.5-1.5-1.5zM7 19h14v-2H7v2zm0-6h14v-2H7v2zm0-8v2h14V5H7z",
                ViewModel = postsVm,
            },
            new()
            {
                Title = "Media",
                IconData = "M19 5v14H5V5h14m0-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-4.86 8.86l-3 3.87L9 13.14 6 17h12l-3.86-5.14z",
                ViewModel = imagesVm
            },
            new()
            {
                Title = "Likes",
                IconData = "M16.5 3c-1.74 0-3.41.81-4.5 2.09C10.91 3.81 9.24 3 7.5 3 4.42 3 2 5.42 2 8.5c0 3.78 3.4 6.86 8.55 11.54L12 21.35l1.45-1.32C18.6 15.36 22 12.28 22 8.5 22 5.42 19.58 3 16.5 3zm-4.4 15.55l-.1.1-.1-.1C7.14 14.24 4 11.39 4 8.5 4 6.5 5.5 5 7.5 5c1.54 0 3.04.99 3.57 2.36h1.87C13.46 5.99 14.96 5 16.5 5c2 0 3.5 1.5 3.5 3.5 0 2.89-3.14 5.74-7.9 10.05z",
                ViewModel = likesVm
            },
            new()
            {
                Title = "Favorites",
                IconData = "M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2zm0 15l-5-2.18L7 18V5h10v13z",
                ViewModel = favoritesVm
            }
        };

        CurrentProfilePage = MenuItems.First(x => x.IsSelected).ViewModel;
        _currentPageIndex = 0;

        NavigateCommand = ReactiveCommand.Create<ProfileMenuItem>(item =>
        {
            int newIndex = MenuItems.IndexOf(item);

            IsTransitionReversed = newIndex < _currentPageIndex;

            _currentPageIndex = newIndex;

            foreach (ProfileMenuItem menuItem in MenuItems)
            {
                menuItem.IsSelected = false;
            }

            item.IsSelected = true;

            CurrentProfilePage = item.ViewModel;

        });



    }

}
