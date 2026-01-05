using System.Collections.ObjectModel;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity.Abstractions.Suggestions;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Feature.Feed.Feed.Domain.Models;
using Ecliptix.Network.Network.Core.Providers;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Suggestions.Suggestions.ViewModels;

public sealed class SuggestionsViewModel : ViewModelBase, ISuggestionsViewModel
{
    [Reactive]
    public ObservableCollection<Suggestion> TrendingItems { get; set; } =
    [
        new()
        {
            Id = "1",
            Author = new PostAuthor
            {
                UserId = "a1",
                DisplayName = "Alice Johnson",
                Username = "alicej",
                AvatarUrl = "https://i.pravatar.cc/150?img=0"
            },
            Title = "Designer & Creative Director",
            Text = "Passionate about creating beautiful and functional designs. Let's connect!",
            CreatedAt = System.DateTime.UtcNow.AddDays(-2),
            MutualConnectionsCount = 8,
            IsFollowing = false
        },

        new()
        {
            Id = "2",
            Author = new PostAuthor
            {
                UserId = "b2",
                DisplayName = "Bob Smith",
                Username = "bobsmith",
                AvatarUrl = "https://i.pravatar.cc/150?img=1"
            },
            Title = "Full-Stack Developer",
            Text = "Experienced in building scalable web applications. Looking to expand my network.",
            CreatedAt = System.DateTime.UtcNow.AddDays(-5),
            MutualConnectionsCount = 5,
            IsFollowing = true
        }
    ];

    public SuggestionsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IGlobalModalService globalModalService,
        IConnectivityService? connectivityService = null)
        : base(networkProvider, localizationService, globalModalService, connectivityService)
    {

    }
}
