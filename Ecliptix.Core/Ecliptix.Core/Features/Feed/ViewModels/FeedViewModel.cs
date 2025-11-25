using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Core.Features.Feed.Services.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Feed.ViewModels;

public sealed partial class FeedViewModel : Core.MVVM.ViewModelBase
{
    private readonly IFeedService _feedService;
    private readonly IPostInteractionService _interactionService;
    private readonly ICommentService _commentService;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;
    private int _currentPage = 1;
    private const int PageSize = 10;

    [Reactive] public ObservableCollection<FeedItemViewModel> Posts { get; set; }
    [Reactive] public bool IsLoadingPosts { get; set; }
    [Reactive] public bool IsRefreshing { get; set; }
    [Reactive] public bool HasMorePosts { get; set; }
    [Reactive] public string ErrorMessage { get; set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadInitialPostsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadMorePostsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> RefreshFeedCommand { get; }

    public FeedViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IFeedService feedService,
        IPostInteractionService interactionService,
        ICommentService commentService)
        : base(networkProvider, localizationService, null)
    {
        _feedService = feedService;
        _interactionService = interactionService;
        _commentService = commentService;

        Posts = new ObservableCollection<FeedItemViewModel>();
        ErrorMessage = string.Empty;
        HasMorePosts = true;

        LoadInitialPostsCommand = ReactiveCommand.CreateFromTask(LoadInitialPostsAsync);
        LoadMorePostsCommand = ReactiveCommand.CreateFromTask(LoadMorePostsAsync);
        RefreshFeedCommand = ReactiveCommand.CreateFromTask(RefreshFeedAsync);

        LoadInitialPostsCommand.Execute().Subscribe().DisposeWith(_disposables);
    }

    private async Task LoadInitialPostsAsync()
    {
        if (IsLoadingPosts)
        {
            return;
        }

        IsLoadingPosts = true;
        _currentPage = 1;
        ErrorMessage = string.Empty;

        try
        {
            Result<FeedPage, string> result = await _feedService.LoadFeedAsync(_currentPage, PageSize);

            if (result.IsOk && result.Unwrap() != null)
            {
                Posts.Clear();
                AddPostsToCollection(result.Unwrap().Posts);
                HasMorePosts = result.Unwrap().HasNextPage;
            }
            else if (result.IsErr)
            {
                ErrorMessage = result.UnwrapErr() ?? "Failed to load feed";
            }
        }
        finally
        {
            IsLoadingPosts = false;
        }
    }

    private async Task LoadMorePostsAsync()
    {
        if (IsLoadingPosts || !HasMorePosts)
        {
            return;
        }

        IsLoadingPosts = true;
        _currentPage++;
        ErrorMessage = string.Empty;

        try
        {
            Result<FeedPage, string> result = await _feedService.LoadFeedAsync(_currentPage, PageSize);

            if (result.IsOk && result.Unwrap() != null)
            {
                AddPostsToCollection(result.Unwrap().Posts);
                HasMorePosts = result.Unwrap().HasNextPage;
            }
            else if (result.IsErr)
            {
                ErrorMessage = result.UnwrapErr() ?? "Failed to load more posts";
                _currentPage--;
            }
        }
        finally
        {
            IsLoadingPosts = false;
        }
    }

    private async Task RefreshFeedAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        _currentPage = 1;
        ErrorMessage = string.Empty;

        try
        {
            Result<FeedPage, string> result = await _feedService.RefreshFeedAsync(PageSize);

            if (result.IsOk && result.Unwrap() != null)
            {
                Posts.Clear();
                AddPostsToCollection(result.Unwrap().Posts);
                HasMorePosts = result.Unwrap().HasNextPage;
            }
            else if (result.IsErr)
            {
                ErrorMessage = result.UnwrapErr() ?? "Failed to refresh feed";
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void AddPostsToCollection(List<FeedPost> posts)
    {
        foreach (FeedPost post in posts)
        {
            FeedItemViewModel viewModel = CreatePostViewModel(post);
            Posts.Add(viewModel);
        }
    }

    private FeedItemViewModel CreatePostViewModel(FeedPost post)
    {
        return new TextPostViewModel(
            post,
            NetworkProvider,
            LocalizationService,
            _interactionService,
            _commentService
        );
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (FeedItemViewModel post in Posts)
            {
                post.Dispose();
            }
            Posts.Clear();
            _disposables.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
