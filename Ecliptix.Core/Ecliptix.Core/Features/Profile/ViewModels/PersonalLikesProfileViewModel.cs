using System.Collections.ObjectModel;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Profile.ViewModels;

public class PersonalLikesProfileViewModel : ReactiveObject
{
    public ObservableCollection<ProfilePostItem> LikedPosts { get; } = new();

    [Reactive] public bool IsEmpty { get; private set; }

    public PersonalLikesProfileViewModel()
    {
        UpdateEmptyState();
        // AddTestLikes();
    }

    private void AddTestLikes()
    {
        LikedPosts.Add(new ProfilePostItem
        {
            AuthorName = "Sarah Chen",
            AuthorHandle = "@sarahchen",
            AuthorInitials = "SC",
            Content = "Great article on OPAQUE protocol! Thanks for sharing.",
            HasImage = false,
            TimeAgo = "1 day ago",
            LikesCount = "45",
            CommentsCount = "2"
        });

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        IsEmpty = LikedPosts.Count == 0;
    }
}
