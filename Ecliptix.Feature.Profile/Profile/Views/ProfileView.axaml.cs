using Avalonia.ReactiveUI;
using Ecliptix.Feature.Profile.Profile.ViewModels;

namespace Ecliptix.Feature.Profile.Profile.Views;

public partial class ProfileView : ReactiveUserControl<ProfileViewModel>
{
    public ProfileView()
    {
        InitializeComponent();
    }
}

