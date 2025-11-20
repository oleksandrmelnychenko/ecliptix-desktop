using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Profile.ViewModels;

public class AboutProfileViewModel : ReactiveObject
{
    [Reactive] public string DisplayName { get; set; }
    [Reactive] public string Handle { get; set; }
    [Reactive] public string Bio { get; set; }
    [Reactive] public string Location { get; set; }
    [Reactive] public string JoinedDate { get; set; }

    // Статистика
    [Reactive] public string PostsCount { get; set; }
    [Reactive] public string FollowersCount { get; set; }
    [Reactive] public string FollowingCount { get; set; }

    // Ініціали для заглушки фото
    [Reactive] public string Initials { get; set; }

    public AboutProfileViewModel()
    {
        // Заповнюємо даними як на скріншоті для прикладу
        DisplayName = "Sarah Chen";
        Handle = "@sarahchen";
        Bio = "Product Designer & Developer 🎨💻\nBuilding beautiful, secure experiences";
        Location = "Los Angeles, CA";
        JoinedDate = "Joined March 2023";

        PostsCount = "124";
        FollowersCount = "2,847";
        FollowingCount = "432";

        Initials = "SC";
    }
}
