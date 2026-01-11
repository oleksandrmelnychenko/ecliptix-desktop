using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Feed.Domain.Models;
using Ecliptix.Utilities;

namespace Ecliptix.Feature.Feed.Services.Abstractions;

public interface IPostInteractionService
{
    Task<Result<PostInteraction, string>> ToggleLikeAsync(string postId, CancellationToken cancellationToken = default);
    Task<Result<PostInteraction, string>> ToggleSaveAsync(string postId, CancellationToken cancellationToken = default);
}
