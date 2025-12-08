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

public sealed class FeedService : IFeedService
{
    private readonly ILogger<FeedService> _logger;

    public FeedService(ILogger<FeedService> logger)
    {
        _logger = logger;
    }

    public async Task<Result<FeedPage, string>> LoadFeedAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading feed page {Page} with page size {PageSize}", page, pageSize);

            await Task.Delay(500, cancellationToken);

            List<FeedPost> mockPosts = GenerateMockPosts(page, pageSize);

            FeedPage feedPage = new()
            {
                Posts = mockPosts,
                CurrentPage = page,
                PageSize = pageSize,
                TotalCount = 100,
                HasNextPage = page < 10
            };

            return Result<FeedPage, string>.Ok(feedPage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load feed page {Page}", page);
            return Result<FeedPage, string>.Err($"Failed to load feed: {ex.Message}");
        }
    }

    public async Task<Result<FeedPage, string>> RefreshFeedAsync(int pageSize, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refreshing feed with page size {PageSize}", pageSize);
        return await LoadFeedAsync(1, pageSize, cancellationToken);
    }

    public async Task<Result<FeedPost, string>> GetPostByIdAsync(string postId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Loading post {PostId}", postId);

            await Task.Delay(300, cancellationToken);

            List<FeedPost> mockPosts = GenerateMockPosts(1, 1);
            FeedPost post = mockPosts.First() with { PostId = postId };

            return Result<FeedPost, string>.Ok(post);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load post {PostId}", postId);
            return Result<FeedPost, string>.Err($"Failed to load post: {ex.Message}");
        }
    }

    private List<FeedPost> GenerateMockPosts(int page, int pageSize)
    {
        List<FeedPost> posts = new();
        int startIndex = (page - 1) * pageSize;

        for (int i = 0; i < pageSize; i++)
        {
            int postIndex = startIndex + i;
            PostContent content = CreateMockText(postIndex);

            FeedPost post = new()
            {
                PostId = $"post_{postIndex}",
                Author = new PostAuthor
                {
                    UserId = $"user_{postIndex % 5}",
                    Username = $"user{postIndex % 5}",
                    DisplayName = $"User {postIndex % 5}",
                    AvatarUrl = $"https://i.pravatar.cc/150?img={postIndex % 70}",
                    IsVerified = postIndex % 7 == 0
                },
                Content = content,
                Interaction = new PostInteraction
                {
                    LikesCount = Random.Shared.Next(10, 10000),
                    CommentsCount = Random.Shared.Next(0, 500),
                    SavesCount = Random.Shared.Next(0, 200),
                    IsLikedByCurrentUser = postIndex % 5 == 0,
                    IsSavedByCurrentUser = postIndex % 7 == 0
                },
                CreatedAt = DateTime.UtcNow.AddHours(-postIndex * 2),
                Location = postIndex % 3 == 0 ? "New York, NY" : null
            };

            posts.Add(post);
        }

        return posts;
    }

    private ImageCarouselContent CreateMockImageCarousel(int index)
    {
        int imageCount = Random.Shared.Next(1, 5);
        List<ImageItem> images = new();

        for (int i = 0; i < imageCount; i++)
        {
            images.Add(new ImageItem
            {
                ImageUrl = $"https://picsum.photos/800/600?random={index}_{i}",
                ThumbnailUrl = $"https://picsum.photos/200/150?random={index}_{i}",
                Width = 800,
                Height = 600,
                AltText = $"Image {i + 1} from post {index}"
            });
        }

        return new ImageCarouselContent
        {
            Images = images,
            Caption = $"Check out these amazing photos! #{index} #photography #amazing"
        };
    }

    private VideoContent CreateMockVideo(int index)
    {
        return new VideoContent
        {
            VideoUrl = $"https://example.com/video_{index}.mp4",
            ThumbnailUrl = $"https://picsum.photos/800/600?random=video_{index}",
            DurationSeconds = Random.Shared.Next(15, 180),
            Width = 1920,
            Height = 1080,
            Caption = $"Amazing video content! #{index} #video #content",
            HasAudio = true
        };
    }

    private TextContent CreateMockText(int index)
    {
        List<string> sampleTexts = new()
        {
            "Just finished an amazing book on software architecture! The patterns discussed are game-changing for scalable applications. Highly recommend it to all developers out there.",
            "Working on a new feature for our desktop app. The UI is looking sleek and the performance improvements are incredible. Can't wait to share more updates soon!",
            "Coffee and code - the perfect combination for a productive morning. What's your favorite programming setup?",
            "Discovered a brilliant solution to a problem I've been stuck on for days. Sometimes taking a break really helps with problem-solving.",
            "Attending an online tech conference today. The keynote on modern UI frameworks was absolutely fascinating!",
            "Finally deployed the new update. Everything went smoothly thanks to great planning and testing. Teamwork makes the dream work!",
            "Exploring new design patterns and architectural approaches. The developer community never ceases to amaze me with innovative solutions.",
            "Late night coding session paying off. The refactoring is complete and the codebase is so much cleaner now.",
            "Sharing some insights from today's development work. Clean code and good documentation make all the difference.",
            "Just hit a major milestone in the project! Celebrating small wins along the way keeps the motivation high."
        };

        string text = sampleTexts[index % sampleTexts.Count];

        List<List<string>> hashtagSets = new()
        {
            new List<string> { "coding", "development", "tech" },
            new List<string> { "software", "engineering", "design" },
            new List<string> { "programming", "developer", "code" },
            new List<string> { "technology", "innovation", "build" },
            new List<string> { "devlife", "productivity", "learning" }
        };

        return new TextContent
        {
            Text = text,
            Hashtags = hashtagSets[index % hashtagSets.Count],
            Mentions = index % 3 == 0 ? new List<string> { "@user1", "@user2" } : null
        };
    }
}
