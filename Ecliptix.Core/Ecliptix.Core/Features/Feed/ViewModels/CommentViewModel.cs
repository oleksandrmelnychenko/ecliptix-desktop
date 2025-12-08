using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Ecliptix.Core.Core.Messaging.Messages;
using Ecliptix.Core.Features.Feed.Models;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Feed.ViewModels;

public sealed class CommentViewModel : ReactiveObject
{
    private const int MAX_COMMENT_LENGHT = 200;
    private const string SHOW_LESS = "Show Less";
    private const string SHOW_MORE = "Show More";

    public Comment Comment { get; }

    public bool RequiresExpansion => Comment.Text.Length > MAX_COMMENT_LENGHT;

    public string DisplayCommentText =>
        IsTextExpanded || !RequiresExpansion
        ? Comment.Text
        : Comment.Text.Substring(0, MAX_COMMENT_LENGHT) + "...";

    [Reactive] public bool IsTextExpanded { get; set; }
    [Reactive] public bool HasReplies { get; set; }
    [Reactive] public string ExpandedText { get; set; }
    [Reactive] public ObservableCollection<CommentViewModel> OrigonalReplies { get; set; } = new();
    [Reactive] public ObservableCollection<CommentViewModel> Replies { get; set; } = new();

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleTextCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ShowRepliesCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ShowMoreRepliesCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> HideRepliesCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ReplyCommand { get; }

    public CommentViewModel(Comment comment)
    {
        Comment = comment;
        IsTextExpanded = false;
        HasReplies = comment.RepliesCount > 0;

        ToggleTextCommand = ReactiveCommand.Create(ToggleText);
        ShowRepliesCommand = ReactiveCommand.Create(ShowReplies);
        ShowMoreRepliesCommand = ReactiveCommand.Create(ShowAllReplies);
        HideRepliesCommand = ReactiveCommand.Create(HideReplies);
        ReplyCommand = ReactiveCommand.Create(Reply);

        ExpandedText = IsTextExpanded ? SHOW_LESS : SHOW_MORE;
    }

    private void Reply()
    {
        WeakReferenceMessenger.Default.Send(new ReplyCommentMessage(Comment));
    }

    private void HideReplies()
    {
        HasReplies = OrigonalReplies.Count > 0;
        Replies.Clear();
    }

    private void ShowAllReplies()
    {
        Replies = new ObservableCollection<CommentViewModel>(OrigonalReplies);
    }

    private void ShowReplies()
    {
        HasReplies = false;
        Replies = new ObservableCollection<CommentViewModel>(OrigonalReplies.Take(3));
    }

    private void ToggleText()
    {
        IsTextExpanded = !IsTextExpanded;
        ExpandedText = IsTextExpanded ? SHOW_LESS : SHOW_MORE;
        this.RaisePropertyChanged(nameof(DisplayCommentText));
    }
}
