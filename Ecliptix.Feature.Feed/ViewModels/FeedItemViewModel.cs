using System;
using System.Reactive.Disposables;
using CommunityToolkit.Mvvm.Messaging;
using Ecliptix.Contracts.Messaging;
using Ecliptix.Core.MVVM;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Feed.Domain.Models;
using Ecliptix.Feature.Feed.Services.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Feed.ViewModels;

public abstract class FeedItemViewModel : ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public FeedPost Post { get; set; }
    [Reactive] public PostInteractionsViewModel Interactions { get; set; }
    [Reactive] public CommentSectionViewModel Comments { get; set; }
    [Reactive] public string RelativeTime { get; set; }
    [Reactive] public bool ShowComments { get; set; }
    [Reactive] public bool IsEdit { get; set; }
    [Reactive] public bool IsFlyoutOpen { get; set; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> BackCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CopyLinkCommand { get; }

    protected FeedItemViewModel(
        FeedPost post,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IPostInteractionService interactionService,
        ICommentService commentService)
        : base(networkProvider, localizationService, null!)
    {
        Post = post;
        RelativeTime = FormatRelativeTime(post.CreatedAt);

        Interactions = new PostInteractionsViewModel(
            post.PostId,
            post.Interaction,
            networkProvider,
            localizationService,
            interactionService
        );

        Comments = new CommentSectionViewModel(
            post.PostId,
            networkProvider,
            localizationService,
            commentService
        );

        BackCommand = ReactiveCommand.Create(Back);

        CopyLinkCommand = ReactiveCommand.Create(CopyLink);

        UpdateRelativeTime();
    }

    private void CopyLink() => IsFlyoutOpen = false;

    private void Back() => WeakReferenceMessenger.Default.Send(new BackMessage(true));

    private void UpdateRelativeTime()
    {
        System.Reactive.Linq.Observable
            .Timer(TimeSpan.Zero, TimeSpan.FromMinutes(1))
            .Subscribe(_ => RelativeTime = FormatRelativeTime(Post.CreatedAt))
            .DisposeWith(_disposables);
    }

    private string FormatRelativeTime(DateTime dateTime)
    {
        TimeSpan difference = DateTime.UtcNow - dateTime;

        return difference switch
        {
            { TotalSeconds: < 60 } => "Just now",
            { TotalMinutes: < 60 } => $"{(int)difference.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)difference.TotalHours}h ago",
            { TotalDays: < 7 } => $"{(int)difference.TotalDays}d ago",
            { TotalDays: < 30 } => $"{(int)(difference.TotalDays / 7)}w ago",
            _ => dateTime.ToString("MMM dd, yyyy")
        };
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
            Interactions.Dispose();
            Comments.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
