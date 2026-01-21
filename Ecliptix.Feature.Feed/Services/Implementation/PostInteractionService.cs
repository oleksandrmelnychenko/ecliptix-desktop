#pragma warning disable CA5394

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Feed.Domain.Models;
using Ecliptix.Feature.Feed.Services.Abstractions;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Feature.Feed.Services.Implementation;

public sealed class PostInteractionService : IPostInteractionService
{
    private readonly Dictionary<string, PostInteraction> _interactionCache = new();

    public async Task<Result<PostInteraction, string>> ToggleLikeAsync(string postId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Toggling like for post {PostId}", postId);

            await Task.Delay(200, cancellationToken);

            if (!_interactionCache.TryGetValue(postId, out PostInteraction? currentInteraction))
            {
                currentInteraction = new PostInteraction
                {
                    LikesCount = Random.Shared.Next(10, 1000),
                    CommentsCount = Random.Shared.Next(0, 500),
                    SavesCount = Random.Shared.Next(0, 200),
                    IsLikedByCurrentUser = false,
                    IsSavedByCurrentUser = false
                };
            }

            PostInteraction updatedInteraction = currentInteraction with
            {
                IsLikedByCurrentUser = !currentInteraction.IsLikedByCurrentUser,
                LikesCount = currentInteraction.IsLikedByCurrentUser
                    ? currentInteraction.LikesCount - 1
                    : currentInteraction.LikesCount + 1
            };

            _interactionCache[postId] = updatedInteraction;

            return Result<PostInteraction, string>.Ok(updatedInteraction);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to toggle like for post {PostId}", postId);
            return Result<PostInteraction, string>.Err($"Failed to toggle like: {ex.Message}");
        }
    }

    public async Task<Result<PostInteraction, string>> ToggleSaveAsync(string postId, CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Toggling save for post {PostId}", postId);

            await Task.Delay(200, cancellationToken);

            if (!_interactionCache.TryGetValue(postId, out PostInteraction? currentInteraction))
            {
                currentInteraction = new PostInteraction
                {
                    LikesCount = Random.Shared.Next(10, 1000),
                    CommentsCount = Random.Shared.Next(0, 500),
                    SavesCount = Random.Shared.Next(0, 200),
                    IsLikedByCurrentUser = false,
                    IsSavedByCurrentUser = false
                };
            }

            PostInteraction updatedInteraction = currentInteraction with
            {
                IsSavedByCurrentUser = !currentInteraction.IsSavedByCurrentUser,
                SavesCount = currentInteraction.IsSavedByCurrentUser
                    ? currentInteraction.SavesCount - 1
                    : currentInteraction.SavesCount + 1
            };

            _interactionCache[postId] = updatedInteraction;

            return Result<PostInteraction, string>.Ok(updatedInteraction);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to toggle save for post {PostId}", postId);
            return Result<PostInteraction, string>.Err($"Failed to toggle save: {ex.Message}");
        }
    }

}
