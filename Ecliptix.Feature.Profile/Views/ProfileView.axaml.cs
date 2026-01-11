using Avalonia.ReactiveUI;
using Ecliptix.Feature.Profile.ViewModels;

namespace Ecliptix.Feature.Profile.Views;

public partial class ProfileView : ReactiveUserControl<ProfileViewModel>
{
    public ProfileView()
    {
        InitializeComponent();
    }
}

