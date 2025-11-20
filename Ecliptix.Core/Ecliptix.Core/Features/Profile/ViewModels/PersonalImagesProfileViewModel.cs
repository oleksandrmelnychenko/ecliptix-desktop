using System.Collections.ObjectModel;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Profile.ViewModels;

// Модель для одного зображення
public class ProfileImageItem : ReactiveObject
{
    // У реальному додатку тут був би шлях до файлу або Bitmap.
    // Для прикладу використовуємо колір для заглушки.
    public string PlaceholderColor { get; set; }
}

public class PersonalImagesProfileViewModel : ReactiveObject
{
    public ObservableCollection<ProfileImageItem> Images { get; } = new();

    public PersonalImagesProfileViewModel()
    {

        Images.Add(new ProfileImageItem { PlaceholderColor = "#1E1E1E" });
        Images.Add(new ProfileImageItem { PlaceholderColor = "#F0F0F0" });
        Images.Add(new ProfileImageItem { PlaceholderColor = "#A0C0E0" });
        Images.Add(new ProfileImageItem { PlaceholderColor = "#E0A080" });

        Images.Add(new ProfileImageItem { PlaceholderColor = "#90E090" });
        Images.Add(new ProfileImageItem { PlaceholderColor = "#E090E0" });
        Images.Add(new ProfileImageItem { PlaceholderColor = "#E0E090" });
        Images.Add(new ProfileImageItem { PlaceholderColor = "#90E0E0" });
    }
}
