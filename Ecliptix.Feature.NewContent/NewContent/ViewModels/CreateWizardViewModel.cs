using System;
using System.Reactive.Disposables;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.NewContent.NewContent.ViewModels;

public class CreateWizardViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    [Reactive] public object CurrentPage { get; set; }

    [Reactive] public bool IsReverseTransition { get; set; } = false;

    public CreateWizardViewModel()
    {
        CreateSelectionViewModel selectionVm = new();
        CurrentPage = selectionVm;


        selectionVm.SelectActionCommand
            .Subscribe(actionType => NavigateToContent(actionType))
            .DisposeWith(_disposables);
    }

    private void NavigateToContent(CreateActionType actionType)
    {
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
            IsReverseTransition = false;
            CurrentPage = nextViewModel;
        }
    }


    public void GoBack()
    {
        if (CurrentPage is not CreateSelectionViewModel)
        {
            IsReverseTransition = true;


            CreateSelectionViewModel selectionVm = new();
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
