using System;
using System.Collections.ObjectModel;
using System.Reactive;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public record FeatureSlide(
    string IconPath,
    string Title,
    string Description,
    string BackgroundColor
);

public class WelcomeViewModel : ViewModelBase, IActivatableViewModel, IRoutableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    public string UrlPathSegment { get; } = "/welcome";
    public IScreen HostScreen { get; }

    public ObservableCollection<FeatureSlide> FeatureSlides { get; }

    [ReactiveUI.Fody.Helpers.Reactive]
    public int SelectedSlideIndex { get; set; }

    public ReactiveCommand<Unit, Unit> NavToCreateAccountCommand { get; }
    public ReactiveCommand<Unit, Unit> NavToSignInCommand { get; }

    public WelcomeViewModel(IScreen hostScreen)
    {
        HostScreen = hostScreen;

        FeatureSlides = new ObservableCollection<FeatureSlide>
        {
            new FeatureSlide(
                IconPath: "M12 2C13.1 2 14 2.9 14 4C14 5.1 13.1 6 12 6C10.9 6 10 5.1 10 4C10 2.9 10.9 2 12 2ZM21 9V7L15 7.5V9.5L16.5 9L21 9ZM12 7C14.8 7 17 9.2 17 12S14.8 17 12 17 7 14.8 7 12 9.2 7 12 7ZM12 9C10.3 9 9 10.3 9 12S10.3 15 12 15 15 13.7 15 12 13.7 9 12 9ZM3 9V7L9 7.5V9.5L7.5 9L3 9Z",
                Title: "Mental Wellness",
                Description: "AI-powered mental health support and mindfulness tools to help you maintain emotional balance",
                BackgroundColor: "#e8e9fe"
            ),
            new FeatureSlide(
                IconPath: "M12,1L3,5V11C3,16.55 6.84,21.74 12,23C17.16,21.74 21,16.55 21,11V5L12,1M12,7C13.4,7 14.8,7.45 16,8.26V11C16,14.78 14.33,18.23 12,19.5C9.67,18.23 8,14.78 8,11V8.26C9.2,7.45 10.6,7 12,7Z",
                Title: "Advanced Security",
                Description: "End-to-end encryption and multi-layer security protocols to protect your conversations",
                BackgroundColor: "#efeae7"
            ),
            new FeatureSlide(
                IconPath: "M12,8.5A2.5,2.5 0 0,0 9.5,11A2.5,2.5 0 0,0 12,13.5A2.5,2.5 0 0,0 14.5,11A2.5,2.5 0 0,0 12,8.5M12,17A4,4 0 0,1 8,13A4,4 0 0,1 12,9A4,4 0 0,1 16,13A4,4 0 0,1 12,17M12,1L8,5H16L12,1M21,10V14L17,18V10L21,10M7,18L3,14V10L7,10V18M16,19H8L12,23L16,19Z",
                Title: "Privacy First",
                Description: "Your data stays private with local processing and zero data collection policies",
                BackgroundColor: "#C9EAD0"
            ),
        };

        SelectedSlideIndex = 0;

        NavToCreateAccountCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(
                AuthViewType.PhoneVerification
            );
        });

        NavToSignInCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(AuthViewType.SignIn);
        });
    }
}
