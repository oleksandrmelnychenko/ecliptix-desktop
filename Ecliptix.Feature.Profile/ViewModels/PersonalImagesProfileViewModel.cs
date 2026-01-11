using System;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReactiveUI;

namespace Ecliptix.Feature.Profile.ViewModels;

public class ProfileImageItem : ReactiveObject
{
    public Bitmap? ImageContent { get; set; }

    public int LikesCount { get; set; }
    public int CommentsCount { get; set; }
}

public class PersonalImagesProfileViewModel : ReactiveObject
{
    public ObservableCollection<ProfileImageItem> Images { get; } = new();

    public PersonalImagesProfileViewModel()
    {

        AddImage("photo1.jpg", 1243, 18);
        AddImage("photo2.jpg", 856, 42);
        AddImage("photo3.jpg", 2300, 105);
        AddImage("photo4.jpg", 540, 6);

        AddImage("photo2.jpg", 332, 12);
        AddImage("photo1.jpg", 900, 30);
    }

    private void AddImage(string fileName, int likes, int comments)
    {
        Uri uri = new($"avares://Ecliptix.Core/Assets/DataSeed/{fileName}");

        try
        {
            Bitmap bitmap = new(AssetLoader.Open(uri));

            Images.Add(new ProfileImageItem
            {
                ImageContent = bitmap,
                LikesCount = likes,
                CommentsCount = comments
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading image {fileName}: {ex.Message}");
        }
    }
}
