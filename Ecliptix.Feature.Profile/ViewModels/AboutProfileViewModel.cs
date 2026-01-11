using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Profile.ViewModels;

public class AboutProfileViewModel : ReactiveObject
{
    [Reactive] public string DisplayName { get; set; }
    [Reactive] public string Handle { get; set; }
    [Reactive] public string Bio { get; set; }
    [Reactive] public string Location { get; set; }
    [Reactive] public string JoinedDate { get; set; }

    [Reactive] public string PostsCount { get; set; }
    [Reactive] public string FollowersCount { get; set; }
    [Reactive] public string FollowingCount { get; set; }

    [Reactive] public string Initials { get; set; }

    [Reactive] public Bitmap? ProfileImage { get; set; }

    public AboutProfileViewModel()
    {
        DisplayName = "Sarah Chen";
        Handle = "@sarahchen";
        Bio = "Product Designer & Developer 🎨💻\nBuilding beautiful, secure experiences";
        Location = "Los Angeles, CA";
        JoinedDate = "Joined March 2023";

        PostsCount = "124";
        FollowersCount = "2,847";
        FollowingCount = "432";

        Initials = "SC";

        ProfileImage = LoadImage("user1.jpg");
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
