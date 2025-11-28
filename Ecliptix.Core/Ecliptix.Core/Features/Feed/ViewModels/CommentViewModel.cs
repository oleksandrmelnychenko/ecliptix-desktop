using System.Windows.Input;
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

    [Reactive] public bool IsTextExpanded { get; set; }

    [Reactive] public string ExpandedText { get; set; }

    public string DisplayCommentText =>
        IsTextExpanded || !RequiresExpansion
        ? Comment.Text
        : Comment.Text.Substring(0, MAX_COMMENT_LENGHT) + "...";

    public ICommand ToggleTextCommand { get; }

    public CommentViewModel(Comment comment)
    {
        Comment = comment;
        IsTextExpanded = false;

        ToggleTextCommand = ReactiveCommand.Create(ToggleText);

        ExpandedText = IsTextExpanded ? SHOW_LESS : SHOW_MORE;
    }

    private void ToggleText()
    {
        IsTextExpanded = !IsTextExpanded;
        ExpandedText = IsTextExpanded ? SHOW_LESS : SHOW_MORE;
        this.RaisePropertyChanged(nameof(DisplayCommentText));
    }
}
