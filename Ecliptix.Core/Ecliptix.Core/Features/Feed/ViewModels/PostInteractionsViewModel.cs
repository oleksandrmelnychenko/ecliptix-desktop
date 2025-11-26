using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Core.Features.Feed.Services.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Feed.ViewModels;

public sealed class PostInteractionsViewModel : ViewModelBase
{
    private readonly string _postId;
    private readonly IPostInteractionService _interactionService;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public PostInteraction Interaction { get; set; }
    [Reactive] public bool IsProcessing { get; set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleLikeCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleSaveCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleCommentsCommand { get; }

    public PostInteractionsViewModel(
        string postId,
        PostInteraction interaction,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IPostInteractionService interactionService)
        : base(networkProvider, localizationService, null)
    {
        _postId = postId;
        _interactionService = interactionService;
        Interaction = interaction;

        ToggleLikeCommand = ReactiveCommand.CreateFromTask(ToggleLikeAsync);
        ToggleSaveCommand = ReactiveCommand.CreateFromTask(ToggleSaveAsync);
        ToggleCommentsCommand = ReactiveCommand.Create(() =>
        {
            WeakReferenceMessenger.Default.Send(new EditPostMessage(postId));
        });
    }

    private async Task ToggleLikeAsync()
    {
        if (IsProcessing)
        {
            return;
        }

        IsProcessing = true;

        try
        {
            Result<PostInteraction, string> result = await _interactionService.ToggleLikeAsync(_postId);

            if (result.IsOk && result.Unwrap() != null)
            {
                Interaction = result.Unwrap();
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task ToggleSaveAsync()
    {
        if (IsProcessing)
        {
            return;
        }

        IsProcessing = true;

        try
        {
            Result<PostInteraction, string> result = await _interactionService.ToggleSaveAsync(_postId);

            if (result.IsOk && result.Unwrap() != null)
            {
                Interaction = result.Unwrap();
            }
        }
        finally
        {
            IsProcessing = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _disposables.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
