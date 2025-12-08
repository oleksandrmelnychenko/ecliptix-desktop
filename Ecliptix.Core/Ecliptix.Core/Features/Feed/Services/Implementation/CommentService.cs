using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Features.Feed.Models;
using Ecliptix.Core.Features.Feed.Services.Abstractions;
using Ecliptix.Utilities;
using Microsoft.Extensions.Logging;

namespace Ecliptix.Core.Features.Feed.Services.Implementation;

public sealed class CommentService : ICommentService
{
    private readonly ILogger<CommentService> _logger;
    private readonly Dictionary<string, List<Comment>> _commentsCache = new();

    public CommentService(ILogger<CommentService> logger)
    {
        _logger = logger;
    }

    public async Task<Result<CommentsPage, string>> LoadCommentsAsync(string postId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading comments for post {PostId}, page {Page}", postId, page);

            await Task.Delay(400, cancellationToken);

            if (!_commentsCache.TryGetValue(postId, out List<Comment>? comments))
            {
                comments = GenerateMockComments(postId, 20);
                _commentsCache[postId] = comments;
            }

            int skip = (page - 1) * pageSize;
            List<Comment> pageComments = comments.Skip(skip).Take(pageSize).ToList();

            CommentsPage commentsPage = new()
            {
                Comments = pageComments,
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = comments.Count,
                HasNextPage = skip + pageSize < comments.Count
            };

            return Result<CommentsPage, string>.Ok(commentsPage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load comments for post {PostId}", postId);
            return Result<CommentsPage, string>.Err($"Failed to load comments: {ex.Message}");
        }
    }

    public async Task<Result<Comment, string>> PostCommentAsync(string postId, string text, string? parentCommentId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Posting comment on post {PostId}", postId);

            await Task.Delay(300, cancellationToken);

            Comment newComment = new()
            {
                CommentId = Guid.NewGuid().ToString(),
                PostId = postId,
                Author = new PostAuthor
                {
                    UserId = "current_user",
                    Username = "currentuser",
                    DisplayName = "Current User",
                    AvatarUrl = "https://i.pravatar.cc/150?img=1",
                    IsVerified = false
                },
                Text = text,
                CreatedAt = DateTime.UtcNow,
                LikesCount = 0,
                IsLikedByCurrentUser = false,
                ParentCommentId = parentCommentId,
                RepliesCount = 0
            };

            if (!_commentsCache.ContainsKey(postId))
            {
                _commentsCache[postId] = new List<Comment>();
            }

            _commentsCache[postId].Insert(0, newComment);

            return Result<Comment, string>.Ok(newComment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post comment on post {PostId}", postId);
            return Result<Comment, string>.Err($"Failed to post comment: {ex.Message}");
        }
    }

    public async Task<Result<Comment, string>> ToggleCommentLikeAsync(string commentId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Toggling like for comment {CommentId}", commentId);

            await Task.Delay(200, cancellationToken);

            foreach (List<Comment> comments in _commentsCache.Values)
            {
                Comment? comment = comments.FirstOrDefault(c => c.CommentId == commentId);
                if (comment != null)
                {
                    Comment updatedComment = comment with
                    {
                        IsLikedByCurrentUser = !comment.IsLikedByCurrentUser,
                        LikesCount = comment.IsLikedByCurrentUser
                            ? comment.LikesCount - 1
                            : comment.LikesCount + 1
                    };

                    int index = comments.IndexOf(comment);
                    comments[index] = updatedComment;

                    return Result<Comment, string>.Ok(updatedComment);
                }
            }

            return Result<Comment, string>.Err("Comment not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle like for comment {CommentId}", commentId);
            return Result<Comment, string>.Err($"Failed to toggle comment like: {ex.Message}");
        }
    }

    public async Task<Result<bool, string>> DeleteCommentAsync(string commentId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting comment {CommentId}", commentId);

            await Task.Delay(300, cancellationToken);

            foreach (List<Comment> comments in _commentsCache.Values)
            {
                Comment? comment = comments.FirstOrDefault(c => c.CommentId == commentId);
                if (comment != null)
                {
                    comments.Remove(comment);
                    return Result<bool, string>.Ok(true);
                }
            }

            return Result<bool, string>.Err("Comment not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete comment {CommentId}", commentId);
            return Result<bool, string>.Err($"Failed to delete comment: {ex.Message}");
        }
    }

    private List<Comment> GenerateMockComments(string postId, int count)
    {
        List<Comment> comments = new();

        for (int i = 0; i < count; i++)
        {
            comments.Add(new Comment
            {
                CommentId = $"comment_{postId}_{i}",
                PostId = postId,
                Author = new PostAuthor
                {
                    UserId = $"user_{i % 10}",
                    Username = $"user{i % 10}",
                    DisplayName = $"User {i % 10}",
                    AvatarUrl = $"https://i.pravatar.cc/150?img={i % 70}",
                    IsVerified = i % 5 == 0
                },
                Text = $"This is a great post! Comment #{i} 👍",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i * 5),
                LikesCount = Random.Shared.Next(0, 100),
                IsLikedByCurrentUser = i % 7 == 0,
                ParentCommentId = null,
                RepliesCount = Random.Shared.Next(0, 5)
            });
        }

        return comments;
    }
}
