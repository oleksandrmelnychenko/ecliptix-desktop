using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Utilities;
using Microsoft.VisualBasic;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Feed.ViewModels;

public sealed class PostControlViewModel : ViewModelBase
{
    [Reactive] public bool IsProcessing { get; set; }
    [Reactive] public string Text { get; set; } = "This is an example post content. Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.";
    [Reactive] public string DisplayName { get; set; } = "Roland Gilead";
    [Reactive] public string? AvatarUrl { get; set; }
    [Reactive] public bool IsVerified { get; set; } = false;
    [Reactive] public string Username { get; set; } = "Roland";
    [Reactive] public bool IsLikedByCurrentUser { get; set; } = true;
    [Reactive] public int LikesCount { get; set; } = 10;
    [Reactive] public int CommentsCount { get; set; }
    [Reactive] public int SavesCount { get; set; } = 5;
    [Reactive] public bool IsSavedByCurrentUser { get; set; } = false;
    [Reactive] public bool ShowComments { get; set; } = false;
    [Reactive] public bool ShowAllComments { get; set; } = false;
    [Reactive] public bool IsVisibleShowAllComments { get; set; }
    [Reactive] public string RelativeTime { get; set; } = "2 hours ago";
    [Reactive] public ObservableCollection<Comment> Comments { get; set; }
    [Reactive] public ObservableCollection<Comment> OriginalComments { get; set; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleLikeCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleSaveCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleCommentsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ToggleFullVisibilityCommentsCommand { get; }

    public PostControlViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IConnectivityService? connectivityService = null)
        : base(networkProvider, localizationService, connectivityService)
    {
        ToggleCommentsCommand = ReactiveCommand.Create(ToggleComments);
        ToggleFullVisibilityCommentsCommand = ReactiveCommand.Create(ToggleFullVisibilityComments);

        SetMockData();

        IsVisibleShowAllComments = UpdateVisibilityAllComments();
    }

    private void ToggleFullVisibilityComments()
    {
        Comments.Clear();

        if (ShowAllComments)
        {
            foreach (Comment comment in OriginalComments.Take(2))
            {
                Comments.Add(comment);
            }
        }
        else
        {
            foreach (Comment comment in OriginalComments)
            {
                Comments.Add(comment);
            }
        }

        ShowAllComments = !ShowAllComments;
        IsVisibleShowAllComments = !IsVisibleShowAllComments;
    }

    private bool UpdateVisibilityAllComments() => CommentsCount > 2;

    private void ToggleComments()
    {
        if (ShowComments)
        {
            Comments.Clear();

            foreach (Comment comment in OriginalComments.Take(2))
            {
                Comments.Add(comment);
            }
        }

        ShowComments = !ShowComments;
    }

    private void SetMockData()
    {
        OriginalComments = new ObservableCollection<Comment>()
        {
            new Comment
            {
                Author = new PostAuthor
                {
                    UserId = "user_001",
                    DisplayName = "Mark Johnson",
                    Username = "mark",
                    IsVerified = true,
                    AvatarUrl = null
                },
                CommentId = "comment_001",
                Text = "Great post! Really enjoyed reading it.",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                LikesCount = 18,
                PostId = "post_001"
            },
            new Comment
            {
                Author = new PostAuthor
                {
                    UserId = "user_002",
                    DisplayName = "Alice Johnson",
                    Username = "alicej",
                    IsVerified = true,
                    AvatarUrl = null
                },
                CommentId = "comment_002",
                Text = "Awesome post! Really enjoyed reading it.",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                LikesCount = 22,
                PostId = "post_002"
            },
            new Comment
            {
                Author = new PostAuthor
                {
                    UserId = "user_003",
                    DisplayName = "Sandy Abrams",
                    Username = "sandy",
                    IsVerified = true,
                    AvatarUrl = null
                },
                CommentId = "comment_003",
                Text = "Has exited with code 4294967295 (0xffffffff)",
                CreatedAt = DateTime.UtcNow.AddHours(-1),
                LikesCount = 7,
                PostId = "post_003"
            }
        };

        Comments = new ObservableCollection<Comment>(OriginalComments.Take(2));
        CommentsCount = OriginalComments.Count;
    }
}
