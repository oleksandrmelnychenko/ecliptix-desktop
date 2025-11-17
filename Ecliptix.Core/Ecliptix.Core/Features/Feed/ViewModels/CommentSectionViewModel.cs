using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Core.Features.Feed.Services.Abstractions;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Feed.ViewModels;

public sealed class CommentSectionViewModel : ViewModelBase
{
    private readonly string _postId;
    private readonly ICommentService _commentService;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;
    private int _currentPage = 1;
    private const int PageSize = 10;

    [Reactive] public ObservableCollection<Comment> Comments { get; set; }
    [Reactive] public string CommentText { get; set; }
    [Reactive] public bool IsLoadingComments { get; set; }
    [Reactive] public bool IsPostingComment { get; set; }
    [Reactive] public bool HasMoreComments { get; set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadCommentsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PostCommentCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadMoreCommentsCommand { get; }

    public CommentSectionViewModel(
        string postId,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ICommentService commentService)
        : base(networkProvider, localizationService, null)
    {
        _postId = postId;
        _commentService = commentService;
        Comments = new ObservableCollection<Comment>();
        CommentText = string.Empty;

        LoadCommentsCommand = ReactiveCommand.CreateFromTask(LoadCommentsAsync);
        PostCommentCommand = ReactiveCommand.CreateFromTask(PostCommentAsync);
        LoadMoreCommentsCommand = ReactiveCommand.CreateFromTask(LoadMoreCommentsAsync);
    }

    private async Task LoadCommentsAsync()
    {
        if (IsLoadingComments)
        {
            return;
        }

        IsLoadingComments = true;
        _currentPage = 1;

        try
        {
            Result<CommentsPage, string> result = await _commentService.LoadCommentsAsync(
                _postId,
                _currentPage,
                PageSize
            );

            if (result.IsOk && result.Unwrap() != null)
            {
                Comments.Clear();
                foreach (Comment comment in result.Unwrap().Comments)
                {
                    Comments.Add(comment);
                }
                HasMoreComments = result.Unwrap().HasNextPage;
            }
        }
        finally
        {
            IsLoadingComments = false;
        }
    }

    private async Task LoadMoreCommentsAsync()
    {
        if (IsLoadingComments || !HasMoreComments)
        {
            return;
        }

        IsLoadingComments = true;
        _currentPage++;

        try
        {
            Result<CommentsPage, string> result = await _commentService.LoadCommentsAsync(
                _postId,
                _currentPage,
                PageSize
            );

            if (result.IsOk && result.Unwrap() != null)
            {
                foreach (Comment comment in result.Unwrap().Comments)
                {
                    Comments.Add(comment);
                }
                HasMoreComments = result.Unwrap().HasNextPage;
            }
        }
        finally
        {
            IsLoadingComments = false;
        }
    }

    private async Task PostCommentAsync()
    {
        if (IsPostingComment || string.IsNullOrWhiteSpace(CommentText))
        {
            return;
        }

        IsPostingComment = true;
        string commentText = CommentText;

        try
        {
            Result<Comment, string> result = await _commentService.PostCommentAsync(
                _postId,
                commentText
            );

            if (result.IsOk && result.Unwrap() != null)
            {
                Comments.Insert(0, result.Unwrap());
                CommentText = string.Empty;
            }
        }
        finally
        {
            IsPostingComment = false;
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
