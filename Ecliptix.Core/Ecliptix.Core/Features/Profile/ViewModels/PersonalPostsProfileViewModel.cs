using System.Collections.ObjectModel;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Profile.ViewModels;

// Модель для одного посту
public class ProfilePostItem : ReactiveObject
{
    public string AuthorName { get; set; }
    public string AuthorHandle { get; set; }
    public string AuthorInitials { get; set; } // Для аватара

    public string Content { get; set; }
    public bool HasImage { get; set; } // Чи є картинка
    public string ImagePlaceholderColor { get; set; } = "#E0E0E0"; // Колір заглушки картинки

    public string TimeAgo { get; set; }
    public string LikesCount { get; set; }
    public string CommentsCount { get; set; }
}

public class PersonalPostsProfileViewModel : ReactiveObject
{
    public ObservableCollection<ProfilePostItem> Posts { get; } = new();

    public PersonalPostsProfileViewModel()
    {
        // 1. Пост зі скріншоту
        Posts.Add(new ProfilePostItem
        {
            AuthorName = "Sarah Chen",
            AuthorHandle = "@sarahchen",
            AuthorInitials = "SC",
            Content = "Just deployed our new secure messaging feature! The OPAQUE protocol integration went smoother than expected. 🔐",
            HasImage = true,
            ImagePlaceholderColor = "#1E1E1E", // Темний фон як на фото (VS Code)
            TimeAgo = "2 hours ago",
            LikesCount = "1243",
            CommentsCount = "18"
        });

        // 2. Текстовий пост (без картинки)
        Posts.Add(new ProfilePostItem
        {
            AuthorName = "Sarah Chen",
            AuthorHandle = "@sarahchen",
            AuthorInitials = "SC",
            Content = "Thinking about refactoring the authentication module next week. Does anyone have experience migrating legacy tokens to PASETO in a high-load environment? 🤔",
            HasImage = false,
            TimeAgo = "5 hours ago",
            LikesCount = "89",
            CommentsCount = "12"
        });

        // 3. Ще один пост з картинкою
        Posts.Add(new ProfilePostItem
        {
            AuthorName = "Sarah Chen",
            AuthorHandle = "@sarahchen",
            AuthorInitials = "SC",
            Content = "Weekend hacking session setup! ☕️",
            HasImage = true,
            ImagePlaceholderColor = "#FFD700", // Жовтий приклад
            TimeAgo = "1 day ago",
            LikesCount = "450",
            CommentsCount = "34"
        });
    }
}
