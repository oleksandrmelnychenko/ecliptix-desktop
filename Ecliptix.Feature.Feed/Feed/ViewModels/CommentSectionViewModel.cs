using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Feature.Feed.Feed.Domain.Models;
using Ecliptix.Feature.Feed.Feed.Messages;
using Ecliptix.Feature.Feed.Feed.Services.Abstractions;
using Ecliptix.Network.Network.Core.Providers;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Feed.Feed.ViewModels;

public sealed class CommentSectionViewModel : ViewModelBase
{
    private readonly string _postId;
    private readonly ICommentService _commentService;
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;
    private int _currentPage = 1;
    private const int PAGE_SIZE = 10;

    [Reactive] public ObservableCollection<CommentViewModel> Comments { get; set; }
    [Reactive] public string CommentText { get; set; }
    [Reactive] public bool IsLoadingComments { get; set; }
    [Reactive] public bool IsPostingComment { get; set; }
    [Reactive] public bool HasMoreComments { get; set; }
    [Reactive] public bool HasReplyComment { get; set; }
    [Reactive] public Comment? ReplyComment { get; set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadCommentsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> PostCommentCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> LoadMoreCommentsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ClearReplyCommand { get; }

    public CommentSectionViewModel(
        string postId,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ICommentService commentService)
        : base(networkProvider, localizationService, null!)
    {
        _postId = postId;
        _commentService = commentService;
        Comments = new ObservableCollection<CommentViewModel>();
        CommentText = string.Empty;

        LoadCommentsCommand = ReactiveCommand.CreateFromTask(LoadCommentsAsync);
        PostCommentCommand = ReactiveCommand.CreateFromTask(PostCommentAsync);
        LoadMoreCommentsCommand = ReactiveCommand.CreateFromTask(LoadMoreCommentsAsync);
        ClearReplyCommand = ReactiveCommand.Create(ClearReply);

        WeakReferenceMessenger.Default.Register<ReplyCommentMessage>(this, (r, m) =>
        {
            CommentText = string.Empty;
            ReplyComment = m.Value;
            HasReplyComment = m.Value != null;
        });
    }

    public void ClearReply()
    {
        ReplyComment = null;
        HasReplyComment = false;
        CommentText = string.Empty;
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
                PAGE_SIZE
            );

            if (result.IsOk && result.Unwrap() != null)
            {
                Comments.Clear();
                AddCommentsToCollection(result.Unwrap().Comments);
                HasMoreComments = result.Unwrap().HasNextPage;
            }
        }
        finally
        {
            IsLoadingComments = false;
        }
    }

    private void AddCommentsToCollection(List<Comment> comments)
    {
        foreach (Comment comment in comments)
        {
            CommentViewModel viewModel = CreateCommentViewModel(comment);
            Comments.Add(viewModel);

            // Replies (temp).
            foreach (Comment comment1 in comments)
            {
                CommentViewModel viewModel1 = CreateCommentViewModel(comment1);
                viewModel.OrigonalReplies.Add(viewModel1);
            }
        }

        //temp
        CommentViewModel tt = Comments.First();
        tt.Comment.Text = "Deserialization vulnerabilities are a threat category where request payloads are processed insecurely. An attacker who successfully leverages these vulnerabilities against an app can cause denial of service (DoS), information disclosure, or remote code execution inside the target app. This risk category consistently makes the OWASP Top 10. Targets include";
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
                PAGE_SIZE
            );

            if (result.IsOk && result.Unwrap() != null)
            {
                AddCommentsToCollection(result.Unwrap().Comments);
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
                Comments.Insert(0, CreateCommentViewModel(result.Unwrap()));
                CommentText = string.Empty;
            }
        }
        finally
        {
            IsPostingComment = false;
            HasReplyComment = false;
            ReplyComment = null;
        }
    }

    private CommentViewModel CreateCommentViewModel(Comment comment)
    {
        return new CommentViewModel(comment);
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
            WeakReferenceMessenger.Default.Unregister<ReplyCommentMessage>(this);
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
