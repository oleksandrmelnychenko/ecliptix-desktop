using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Ecliptix.Core.Core.Messaging.Messages;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Core.Features.Feed.Services.Abstractions;
using Ecliptix.Core.Features.Profile.ViewModels;
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

    [Reactive] public ObservableCollection<ProfileMenuItem> MenuItems { get; set; }
    [Reactive] public ObservableCollection<FeedItemViewModel> Posts { get; set; }
    [Reactive] public bool IsLoadingPosts { get; set; }
    [Reactive] public bool IsRefreshing { get; set; }
    [Reactive] public bool HasMorePosts { get; set; }
    [Reactive] public bool IsEdit { get; set; }
    [Reactive] public string ErrorMessage { get; set; }
    [Reactive] public FeedItemViewModel? SelectedPost { get; set; }
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

        WeakReferenceMessenger.Default.Register<EditPostMessage>(this, (r, m) =>
        {
            ShouldShowEditPost(m.PostId);
        });

        WeakReferenceMessenger.Default.Register<BackMessage>(this, (r, m) =>
        {
            IsEdit = false;
            SelectedPost?.Interactions.IsEdit = IsEdit;
        });

        MenuItems = SetTempItems();
    }

    private void ShouldShowEditPost(string postId)
    {
        SelectedPost = null;

        FeedItemViewModel? foundViewModel = Posts.FirstOrDefault(p => p.Post.PostId == postId);
        if (foundViewModel != null)
        {
            IsEdit = true;
            SelectedPost = foundViewModel;
            SelectedPost.Interactions.IsEdit = IsEdit;
            SelectedPost.Comments.LoadCommentsCommand.Execute();
            return;
        }
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

    private ObservableCollection<ProfileMenuItem> SetTempItems()
    {
        return new ObservableCollection<ProfileMenuItem>
        {
            new()
            {
                Title = "For You",
                IconData = "M11 7h2v2h-2zm0 4h2v6h-2zm1-9C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z",
                ViewModel = new AboutProfileViewModel(),
                IsSelected = true
            },
            new()
            {
                Title = "Following",
                IconData = "M4 10.5c-.83 0-1.5.67-1.5 1.5s.67 1.5 1.5 1.5 1.5-.67 1.5-1.5-.67-1.5-1.5-1.5zm0-6c-.83 0-1.5.67-1.5 1.5S3.17 7.5 4 7.5 5.5 6.83 5.5 6 4.83 4.5 4 4.5zm0 12c-.83 0-1.5.68-1.5 1.5s.68 1.5 1.5 1.5 1.5-.68 1.5-1.5-.67-1.5-1.5-1.5zM7 19h14v-2H7v2zm0-6h14v-2H7v2zm0-8v2h14V5H7z",
                ViewModel = new PersonalPostsProfileViewModel()
            },
            new()
            {
                Title = "Trending",
                ViewModel = new PersonalImagesProfileViewModel(),
                IconData = "M19 5v14H5V5h14m0-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm-4.86 8.86l-3 3.87L9 13.14 6 17h12l-3.86-5.14z"
            }
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
            foreach (FeedItemViewModel post in Posts)
            {
                post.Comments.Dispose();
                post.Dispose();
            }
            Posts.Clear();
            WeakReferenceMessenger.Default.Unregister<EditPostMessage>(this);
            WeakReferenceMessenger.Default.Unregister<BackMessage>(this);
            _disposables.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
