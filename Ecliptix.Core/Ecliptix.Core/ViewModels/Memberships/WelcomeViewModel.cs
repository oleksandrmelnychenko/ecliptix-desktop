using System; 
using System.Collections.ObjectModel;
using System.Reactive;
using Ecliptix.Core.ViewModels.Authentication;
using ReactiveUI;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory; 

namespace Ecliptix.Core.ViewModels.Memberships;

public record FeatureSlide(string IconPath, string Description, bool IsSelected = false);

public class WelcomeViewModel : ViewModelBase, IActivatableViewModel, IRoutableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    public string UrlPathSegment { get; } = "/welcome";
    public IScreen HostScreen { get; }

    public ObservableCollection<FeatureSlide> FeatureSlides { get; }

    [ReactiveUI.Fody.Helpers.Reactive] public int SelectedSlideIndex { get; set; }

    public ReactiveCommand<Unit, Unit> NavToCreateAccountCommand { get; }
    public ReactiveCommand<Unit, Unit> NavToSignInCommand { get; }

    public WelcomeViewModel(IScreen hostScreen)
    {
        HostScreen = hostScreen;

        FeatureSlides = [];

        this.WhenAnyValue(x => x.SelectedSlideIndex)
            .Subscribe(new Action<int>(index =>
            {
                for (int i = 0; i < FeatureSlides.Count; i++)
                {
                    FeatureSlide item = FeatureSlides[i];
                    bool shouldBeSelected = (i == index);
                    
                    if (item.IsSelected != shouldBeSelected)
                    {
                        FeatureSlides[i] = item with { IsSelected = shouldBeSelected };
                    }
                }
            }));

        SelectedSlideIndex = 0;

        NavToCreateAccountCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(AuthViewType.PhoneVerification);
        });
        
        NavToSignInCommand = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(AuthViewType.SignIn);
        });
    }
}