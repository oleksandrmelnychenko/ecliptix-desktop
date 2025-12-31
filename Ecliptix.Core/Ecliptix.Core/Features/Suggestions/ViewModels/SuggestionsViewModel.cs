using System.Collections.ObjectModel;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Suggestions.ViewModels;

public sealed class SuggestionsViewModel : ViewModelBase
{
    [Reactive] public ObservableCollection<Suggestion> TrendingItems { get; set; } = GetData();

    [Reactive] public ObservableCollection<Suggestion> Items { get; set; } = GetData();

    /// <summary>
    /// ctor().
    /// </summary>
    /// <param name="networkProvider"></param>
    /// <param name="localizationService"></param>
    /// <param name="globalModalService"></param>
    /// <param name="connectivityService"></param>
    public SuggestionsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IGlobalModalService globalModalService,
        IConnectivityService? connectivityService = null)
        : base(networkProvider, localizationService, globalModalService, connectivityService)
    {

    }

    private static ObservableCollection<Suggestion> GetData() => new()
    {
        new Suggestion
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
        new Suggestion
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
    };
}
