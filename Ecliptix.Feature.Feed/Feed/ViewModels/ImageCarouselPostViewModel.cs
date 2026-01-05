using System;
using System.Reactive;
using System.Reactive.Disposables;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Feature.Feed.Feed.Domain.Models;
using Ecliptix.Feature.Feed.Feed.Services.Abstractions;
using Ecliptix.Network.Network.Core.Providers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Feed.Feed.ViewModels;

public sealed class ImageCarouselPostViewModel : FeedItemViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public ImageCarouselContent CarouselContent { get; set; }
    [Reactive] public int CurrentImageIndex { get; set; }
    [Reactive] public int TotalImages { get; set; }

    public ReactiveCommand<Unit, Unit> NextImageCommand { get; }
    public ReactiveCommand<Unit, Unit> PreviousImageCommand { get; }

    public ImageCarouselPostViewModel(
        FeedPost post,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IPostInteractionService interactionService,
        ICommentService commentService)
        : base(post, networkProvider, localizationService, interactionService, commentService)
    {
        if (post.Content is not ImageCarouselContent carouselContent)
        {
            throw new ArgumentException("Post content must be ImageCarouselContent", nameof(post));
        }

        CarouselContent = carouselContent;
        CurrentImageIndex = 0;
        TotalImages = carouselContent.Images.Count;

        NextImageCommand = ReactiveCommand.Create(NextImage);
        PreviousImageCommand = ReactiveCommand.Create(PreviousImage);
    }

    private void NextImage()
    {
        if (CurrentImageIndex < TotalImages - 1)
        {
            CurrentImageIndex++;
        }
    }

    private void PreviousImage()
    {
        if (CurrentImageIndex > 0)
        {
            CurrentImageIndex--;
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
