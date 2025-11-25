using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ecliptix.Core.Features.Chats.Models;
using Ecliptix.Core.Features.Chats.ViewModels;

namespace Ecliptix.Core.Features.Chats.Services;

public interface IChatSidebarService
{
    Task<IEnumerable<ChatListItemViewModel>> GetChatsAsync();
}

public class ChatSidebarService : IChatSidebarService
{
    private Bitmap? LoadAvatar(string fileName)
    {
        try
        {
            Uri uri = new($"avares://Ecliptix.Core/Assets/DataSeed/{fileName}");
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch
        {
            return null;
        }
    }

    public async Task<IEnumerable<ChatListItemViewModel>> GetChatsAsync()
    {
        await Task.Delay(100);

        return new List<ChatListItemViewModel>
        {
            new()
            {
                Title = "Sarah Chen",
                LastMessageRaw = "Hey! How's the project going?",
                LastMessageTime = DateTime.Now.AddMinutes(-5),
                UnreadCount = 2,
                IsOnline = true,
                Type = ChatType.Personal,
                IsPinned = true,
                AvatarImage = LoadAvatar("user1.jpg")
            },
            new()
            {
                Title = "Design Team",
                LastMessageSender = "Marcus",
                LastMessageRaw = "New mockups are ready",
                LastMessageTime = DateTime.Now.AddMinutes(-20),
                UnreadCount = 5,
                Type = ChatType.Group,
            },
            new()
            {
                Title = "Project Launch Team",
                LastMessageSender = "Emma",
                LastMessageRaw = "Let's finalize the timeline",
                LastMessageTime = DateTime.Now.AddHours(-1),
                UnreadCount = 3,
                Type = ChatType.Group
            },
            new()
            {
                Title = "Engineering",
                LastMessageSender = "Alex",
                LastMessageRaw = "Deployment complete ✓",
                LastMessageTime = DateTime.Now.AddDays(-1),
                UnreadCount = 0,
                Type = ChatType.Channel
            },
            new()
            {
                Title = "Lisa Park",
                LastMessageRaw = "commented on your post",
                LastMessageTime = DateTime.Now.AddDays(-1),
                UnreadCount = 0,
                Type = ChatType.Personal
            }
        };
    }
}
