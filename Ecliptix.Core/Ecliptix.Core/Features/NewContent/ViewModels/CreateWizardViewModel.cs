using System;
using System.Reactive.Disposables;
using Ecliptix.Core.Features.NewContent.ViewModels;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.NewContent;

public class CreateWizardViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    // This property controls what the user sees inside the wizard
    [Reactive] public object CurrentPage { get; set; }

    // Controls the animation direction (optional, for sliding effects)
    [Reactive] public bool IsReverseTransition { get; set; } = false;

    public CreateWizardViewModel()
    {
        // 1. Initialize with the Selection Menu
        CreateSelectionViewModel selectionVm = new CreateSelectionViewModel();
        CurrentPage = selectionVm;

        // 2. Listen to the menu selection
        selectionVm.SelectActionCommand
            .Subscribe(actionType => NavigateToContent(actionType))
            .DisposeWith(_disposables);
    }

    private void NavigateToContent(CreateActionType actionType)
    {
        // Create the specific ViewModel based on selection
        object? nextViewModel = actionType switch
        {
            CreateActionType.NewChannel => new NewChannelViewModel(),
            CreateActionType.NewGroupChat => new NewGroupChatViewModel(),
            CreateActionType.NewPost => new NewPostViewModel(),
            CreateActionType.NewContact => new NewContactViewModel(),
            _ => null
        };

        if (nextViewModel != null)
        {
            IsReverseTransition = false; // Slide forward
            CurrentPage = nextViewModel;
        }
    }

    // Optional: Back button logic
    public void GoBack()
    {
        if (CurrentPage is not CreateSelectionViewModel)
        {
            IsReverseTransition = true; // Slide backward

            // Re-create selection VM or cache it
            CreateSelectionViewModel selectionVm = new CreateSelectionViewModel();
            selectionVm.SelectActionCommand
                .Subscribe(NavigateToContent)
                .DisposeWith(_disposables);

            CurrentPage = selectionVm;
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

public record OpenCreateWizardEvent { }
