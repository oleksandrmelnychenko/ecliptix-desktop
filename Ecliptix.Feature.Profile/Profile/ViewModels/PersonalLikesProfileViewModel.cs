using System.Collections.ObjectModel;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Profile.Profile.ViewModels;

public class PersonalLikesProfileViewModel : ReactiveObject
{
    public ObservableCollection<ProfilePostItem> LikedPosts { get; } = new();

    [Reactive] public bool IsEmpty { get; private set; }

    public PersonalLikesProfileViewModel()
    {
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        IsEmpty = LikedPosts.Count == 0;
    }
}
