using System.Collections.ObjectModel;
using System.Linq;
using Ecliptix.Core.Features.Settings.ViewModels;
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
    public ObservableCollection<ProfileMenuItem> MenuItems { get; }
    public ReactiveCommand<ProfileMenuItem, SystemU> NavigateCommand { get; private set; }
    public ViewModelActivator Activator { get; } = new();


    public ProfileViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService,
        IApplicationSecureStorageProvider secureStorageProvider)
        : base(networkProvider, localizationService, null)
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
                IconData = "M3 3v18h18V3H3zm16 16H5V5h14v14zM11 13h2v2h-2v-2zm-4 0h2v2H7v-2zm8 0h2v2h-2v-2zm-8-4h2v2H7V9zm4 0h2v2h-2V9zm4 0h2v2h-2V9z",
                ViewModel = postsVm,
            },
            new()
            {
                Title = "Media",
                IconData = "M21 19V5c0-1.1-.9-2-2-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2zM8.5 13.5l2.5 3.01L14.5 12l4.5 6H5l3.5-4.5z",
                ViewModel = imagesVm
            },
            new()
            {
                Title = "Likes",
                IconData = "M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z",
                ViewModel = likesVm
            },
            new()
            {
                Title = "Favorites",
                IconData = "M17 3H7c-1.1 0-1.99.9-1.99 2L5 21l7-3 7 3V5c0-1.1-.9-2-2-2z",
                ViewModel = favoritesVm
            }
        };

        CurrentProfilePage = MenuItems.First(x => x.IsSelected).ViewModel;

        NavigateCommand = ReactiveCommand.Create<ProfileMenuItem>(item =>
        {
            foreach (ProfileMenuItem menuItem in MenuItems)
            {
                menuItem.IsSelected = false;
            }

            item.IsSelected = true;

            CurrentProfilePage = item.ViewModel;

        });



    }

}
