using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Features.Feed.Services.Abstractions;

public interface IPostInteractionService
{
    Task<Result<PostInteraction, string>> ToggleLikeAsync(string postId, CancellationToken cancellationToken = default);
    Task<Result<PostInteraction, string>> ToggleSaveAsync(string postId, CancellationToken cancellationToken = default);
    Task<Result<ShareResult, string>> SharePostAsync(string postId, ShareDestination destination, CancellationToken cancellationToken = default);
}

public enum ShareDestination
{
    CopyLink,
    ShareToChat,
    ShareToStory,
    ShareExternal
}

public sealed record ShareResult
{
    public required bool Success { get; init; }
    public string? SharedUrl { get; init; }
    public string? Message { get; init; }
}
