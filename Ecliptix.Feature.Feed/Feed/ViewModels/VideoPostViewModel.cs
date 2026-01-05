using System;
using System.Reactive;
using System.Reactive.Disposables;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Feature.Feed.Feed.Domain.Models;
using Ecliptix.Feature.Feed.Feed.Services.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Feed.Feed.ViewModels;

public sealed class VideoPostViewModel : FeedItemViewModel
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public VideoContent VideoContent { get; set; }
    [Reactive] public bool IsPlaying { get; set; }
    [Reactive] public bool IsMuted { get; set; }
    [Reactive] public double CurrentPosition { get; set; }
    [Reactive] public double Duration { get; set; }
    [Reactive] public bool ShowControls { get; set; }

    public ReactiveCommand<Unit, Unit> TogglePlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleMuteCommand { get; }

    public VideoPostViewModel(
        FeedPost post,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IPostInteractionService interactionService,
        ICommentService commentService)
        : base(post, networkProvider, localizationService, interactionService, commentService)
    {
        if (post.Content is not VideoContent videoContent)
        {
            throw new ArgumentException("Post content must be VideoContent", nameof(post));
        }

        VideoContent = videoContent;
        Duration = videoContent.DurationSeconds;
        IsMuted = !videoContent.HasAudio;
        ShowControls = true;

        TogglePlayPauseCommand = ReactiveCommand.Create(TogglePlayPause);
        ToggleMuteCommand = ReactiveCommand.Create(ToggleMute);
    }

    private void TogglePlayPause()
    {
        IsPlaying = !IsPlaying;
    }

    private void ToggleMute()
    {
        IsMuted = !IsMuted;
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
