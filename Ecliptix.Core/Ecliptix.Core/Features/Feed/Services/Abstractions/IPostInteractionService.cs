using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Features.Feed.Services.Abstractions;

public interface IPostInteractionService
{
    Task<Result<PostInteraction, string>> ToggleLikeAsync(string postId, CancellationToken cancellationToken = default);
    Task<Result<PostInteraction, string>> ToggleSaveAsync(string postId, CancellationToken cancellationToken = default);
}
