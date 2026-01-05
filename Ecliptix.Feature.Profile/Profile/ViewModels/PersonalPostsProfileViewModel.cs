using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReactiveUI;

namespace Ecliptix.Feature.Profile.Profile.ViewModels;

public class ProfilePostItem : ReactiveObject
{
    public required string AuthorName { get; set; }
    public required string AuthorHandle { get; set; }
    public required string AuthorInitials { get; set; }

    public required string Content { get; set; }

    public bool HasImage { get; set; }

    public Bitmap? PostImage { get; set; }

    public required string TimeAgo { get; set; }
    public required string LikesCount { get; set; }
    public required string CommentsCount { get; set; }
}

public class PersonalPostsProfileViewModel : ReactiveObject
{
    public ObservableCollection<ProfilePostItem> Posts { get; } = new();

    public PersonalPostsProfileViewModel()
    {
        Posts.Add(new ProfilePostItem
        {
            AuthorName = "Sarah Chen",
            AuthorHandle = "@sarahchen",
            AuthorInitials = "SC",
            Content = "Just deployed our new secure messaging feature! The OPAQUE protocol integration went smoother than expected. 🔐",
            HasImage = true,
            PostImage = LoadImage("photo1.jpg"),
            TimeAgo = "2 hours ago",
            LikesCount = "1243",
            CommentsCount = "18"
        });

        Posts.Add(new ProfilePostItem
        {
            AuthorName = "Sarah Chen",
            AuthorHandle = "@sarahchen",
            AuthorInitials = "SC",
            Content = "Thinking about refactoring the authentication module next week. Does anyone have experience migrating legacy tokens to PASETO in a high-load environment? 🤔",
            HasImage = false,
            PostImage = null,
            TimeAgo = "5 hours ago",
            LikesCount = "89",
            CommentsCount = "12"
        });

        Posts.Add(new ProfilePostItem
        {
            AuthorName = "Sarah Chen",
            AuthorHandle = "@sarahchen",
            AuthorInitials = "SC",
            Content = "Weekend hacking session setup! ☕️",
            HasImage = true,
            PostImage = LoadImage("photo2.jpg"),
            TimeAgo = "1 day ago",
            LikesCount = "450",
            CommentsCount = "34"
        });
    }

    private Bitmap? LoadImage(string fileName)
    {
        try
        {
            Uri uri = new($"avares://Ecliptix.Core/Assets/DataSeed/{fileName}");
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
