using System;
using System.Reactive.Disposables;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Feature.Feed.Feed.Domain.Models;
using Ecliptix.Feature.Feed.Feed.Services.Abstractions;
using Ecliptix.Network.Network.Core.Providers;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Feed.Feed.ViewModels;

public sealed class TextPostViewModel : FeedItemViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public TextContent TextContent { get; set; }
    [Reactive] public bool IsExpanded { get; set; }

    public TextPostViewModel(
        FeedPost post,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IPostInteractionService interactionService,
        ICommentService commentService)
        : base(post, networkProvider, localizationService, interactionService, commentService)
    {
        if (post.Content is not TextContent textContent)
        {
            throw new ArgumentException("Post content must be TextContent", nameof(post));
        }

        TextContent = textContent;
        IsExpanded = textContent.Text.Length <= 200;
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
