using System;
using System.Threading.Tasks;
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
    [Reactive] public int CommentsCount { get; set; } = 13;
    [Reactive] public int SavesCount { get; set; } = 5;
    [Reactive] public bool IsSavedByCurrentUser { get; set; } = false;
    [Reactive] public bool ShowComments { get; set; } = false;
    [Reactive] public bool ShowAllComments { get; set; } = false;
    [Reactive] public bool IsVisibleShowAllComments { get; set; }
    [Reactive] public string RelativeTime { get; set; } = "2 hours ago";

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

        IsVisibleShowAllComments = UpdateVisibilityAllComments();
    }

    private void ToggleFullVisibilityComments()
    {
        ShowAllComments = !ShowAllComments;
        IsVisibleShowAllComments = !IsVisibleShowAllComments;
    }

    private bool UpdateVisibilityAllComments() => CommentsCount > 2;

    private void ToggleComments() => ShowComments = !ShowComments;
}
