using System.Collections.ObjectModel;
using ReactiveUI;

namespace Ecliptix.Core.Features.Profile.ViewModels;

public class PersonalFavoritesViewModel : ReactiveObject
{
    public PersonalFavoritesViewModel()
    {
        Favorites = new ObservableCollection<string>();
    }

    public ObservableCollection<string> Favorites { get; }

    public bool IsEmpty => Favorites.Count == 0;
}
