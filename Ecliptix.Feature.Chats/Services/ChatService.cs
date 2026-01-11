using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ecliptix.Feature.Chats.Domain.Models;
using Ecliptix.Feature.Chats.ViewModels;
using Ecliptix.Feature.Chats.ViewModels.Messages;

#pragma warning disable CA5394

namespace Ecliptix.Feature.Chats.Services;

public interface IChatService
{
    Task<IEnumerable<ChatListItemViewModel>> GetChatsAsync();
    IAsyncEnumerable<IEnumerable<MessageViewModelBase>> GetMessagesStreamAsync(Guid chatId);

    Task<List<Bitmap>> GetChatParticipantsAvatarsAsync(Guid chatId, int limit = 4);
    int GetChatParticipantsCount(Guid chatId);

}

public class ChatService : IChatService
{
    private readonly List<Participant> _users;
    private readonly List<ChatModel> _chats;
    private readonly List<MessageModel> _messages;

    private readonly Guid _currentUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public ChatService()
    {
        _users = new List<Participant>();
        _chats = new List<ChatModel>();
        _messages = new List<MessageModel>();

        InitializeData();
    }

    public async Task<IEnumerable<ChatListItemViewModel>> GetChatsAsync()
    {
        await Task.Delay(5);

        List<ChatListItemViewModel> sidebarItems = new();
        Random rnd = new();

        foreach (ChatModel chat in _chats)
        {
            MessageModel? lastMsg = _messages
                .Where(m => m.ChatId == chat.Id)
                .OrderByDescending(m => m.Timestamp)
                .FirstOrDefault();

            string avatarFileName = "";
            string chatTitle = chat.Title;

            if (chat.Type == ChatType.Personal)
            {
                Guid partnerId = chat.ParticipantIds.FirstOrDefault(id => id != _currentUserId);
                Participant? partner = _users.FirstOrDefault(u => u.Id == partnerId);
                if (partner != null)
                {
                    avatarFileName = partner.AvatarPath;
                    chatTitle = partner.Name;
                }
            }

            sidebarItems.Add(new ChatListItemViewModel
            {
                Id = chat.Id,
                Title = chatTitle,
                Type = chat.Type,
                LastMessageRaw = lastMsg?.Text ?? "No messages yet",
                LastMessageTime = lastMsg?.Timestamp ?? DateTime.Now,
                LastMessageSender = _users.FirstOrDefault(u => u.Id == lastMsg?.SenderId)?.Name ?? "",
                UnreadCount = rnd.Next(0, 6),
                IsOnline = true,
                AvatarImage = LoadAvatar(avatarFileName)
            });
        }

        return sidebarItems.OrderByDescending(x => x.LastMessageTime);
    }

    public async Task<List<Bitmap>> GetChatParticipantsAvatarsAsync(Guid chatId, int limit = 4)
    {
        ChatModel? chat = _chats.FirstOrDefault(c => c.Id == chatId);
        if (chat == null)
        {
            return new List<Bitmap>();
        }

        List<Guid> participantIds = chat.ParticipantIds
            .Where(id => id != _currentUserId)
            .Take(limit)
            .ToList();

        List<Bitmap> avatars = new();
        foreach (Guid id in participantIds)
        {
            Participant? user = _users.FirstOrDefault(u => u.Id == id);
            Bitmap? avatar = LoadAvatar(user?.AvatarPath ?? "");
            if (avatar != null)
            {
                avatars.Add(avatar);
            }
        }

        return avatars;
    }

    public int GetChatParticipantsCount(Guid chatId)
    {
        ChatModel? chat = _chats.FirstOrDefault(c => c.Id == chatId);
        return chat?.ParticipantIds.Count ?? 0;
    }

    public async IAsyncEnumerable<IEnumerable<MessageViewModelBase>> GetMessagesStreamAsync(Guid chatId)
    {
        ChatModel? chat = _chats.FirstOrDefault(c => c.Id == chatId);
        if (chat == null)
        {
            yield break;
        }

        IAsyncEnumerable<IEnumerable<MessageViewModelBase>> stream = chat.Type switch
        {
            ChatType.Channel => GenerateChannelStream(),
            ChatType.Group => GenerateGroupStream(chatId),
            ChatType.Personal => GeneratePersonalStream(chatId),
            _ => throw new ArgumentOutOfRangeException()
        };

        await foreach (IEnumerable<MessageViewModelBase> batch in stream)
        {
            yield return batch;
        }
    }

    private async IAsyncEnumerable<IEnumerable<MessageViewModelBase>> GenerateChannelStream()
    {
        Random rnd = new();
        DateTime? lastDate = null;

        string veryLongText =
            "🚀 **Ecliptix v2.4.0 Release Notes**\n\n" +
            "We are excited to announce the rollout of the latest update. This release focuses heavily on performance optimizations and UI consistency.\n\n" +
            "**Key Highlights:**\n" +
            "1. **Rendering Engine**: Switched to a new composition target, resulting in 60fps animations on lower-end devices.\n" +
            "2. **Memory Usage**: Reduced idle memory footprint by ~30% by optimizing image caching strategies.\n" +
            "3. **Dark Mode**: Fixed contrast issues in the settings panel and sidebar navigation.\n\n" +
            "**Bug Fixes:**\n" +
            "- Resolved an issue where the chat history would jump when loading new messages.\n" +
            "- Fixed a crash occurring when uploading large PDF files.\n" +
            "- Corrected timestamp formatting for users in UTC-12 timezones.\n\n" +
            "Please make sure to update your local environments by EOD. If you encounter any regressions, report them immediately to the QA channel. Great work everyone!";

        string mediumText =
            "Just a reminder that the design review meeting has been moved to Friday at 10:00 AM. Please have your Figma prototypes ready for presentation. We need to finalize the dashboard layout before the next sprint begins.";

        string shortText =
            "Server maintenance is scheduled for tonight at 02:00 AM UTC. Expected downtime: 15 mins.";

        for (int q = 0; q < 5; q++)
        {
            for (int i = 0; i < 4; i++)
            {
                await Task.Delay(30);
                List<MessageViewModelBase> batch = new();
                DateTime baseTime = DateTime.Now.AddDays(i - 2);

                for (int j = 0; j < 1; j++)
                {
                    DateTime postTime = baseTime.AddHours(10);

                    if (lastDate == null || lastDate.Value.Date != postTime.Date)
                    {
                        batch.Add(new DateSeparatorViewModel { Time = postTime });
                    }

                    lastDate = postTime;

                    ChannelPostViewModel post = new()
                    {
                        Time = postTime,
                        Likes = rnd.Next(50, 2000),
                        Comments = rnd.Next(5, 100),
                        Views = rnd.Next(500, 15000),
                        Shares = rnd.Next(2, 50),
                        SenderName = "System Admin"
                    };

                    switch (i)
                    {
                        case 0:
                            post.Text = "Check out the new marketing assets for the upcoming campaign! 🎨";
                            post.PostImage = LoadAvatar("user1.jpg");
                            break;

                        case 1:
                            post.Text = shortText;
                            break;

                        case 2:
                            post.Text = veryLongText;
                            break;

                        case 3:
                            post.Text = mediumText;
                            break;
                    }

                    batch.Add(post);
                }
                yield return batch;
            }
        }

    }

    private async IAsyncEnumerable<IEnumerable<MessageViewModelBase>> GenerateGroupStream(Guid chatId)
    {
        Participant marcus = _users.FirstOrDefault(u => u.Name.Contains("Marcus")) ?? _users.First();
        Participant emma = _users.FirstOrDefault(u => u.Name.Contains("Emma")) ?? _users.First();
        Participant sarah = _users.FirstOrDefault(u => u.Name.Contains("Sarah")) ?? _users.First();
        Participant me = _users.First(u => u.Id == _currentUserId);

        DateTime? lastDate = null;

        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(30);
            List<MessageModel> batchModels = new();
            Guid msgId = Guid.NewGuid();

            DateTime baseTime = (i == 0) ? DateTime.Now.AddDays(-2) : DateTime.Now.AddMinutes(-10 + i);

            switch (i)
            {
                case 0:
                    batchModels.Add(new MessageModel(msgId, chatId, marcus.Id, "Hey team! Just uploaded the new icons.", baseTime, MessageType.Text));
                    batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, emma.Id, "Thanks Marcus! Checking them now.", baseTime.AddMinutes(1), MessageType.Text));
                    break;
                case 1:
                    batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, me.Id, "They look clean. Good job.", baseTime, MessageType.Text));
                    break;
                case 2:
                    batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, marcus.Id, "Thanks! I used the outlined style.", baseTime, MessageType.Reply, msgId));
                    break;
                case 3:
                    batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, sarah.Id, "I think the 'Settings' icon is a bit too small.", baseTime, MessageType.Text));
                    break;
                case 4:
                    batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, emma.Id, "Agreed. Let's sync at 5 PM.", baseTime, MessageType.Text));
                    break;
            }

            if (batchModels.Any())
            {
                IEnumerable<MessageViewModelBase> vms = MapToViewModels(batchModels);
                List<MessageViewModelBase> vmsWithDates = InjectDateSeparators(vms, ref lastDate);

                yield return vmsWithDates;
            }
        }
    }

    private List<MessageViewModelBase> InjectDateSeparators(IEnumerable<MessageViewModelBase> messages, ref DateTime? lastDate)
    {
        List<MessageViewModelBase> result = new();

        foreach (MessageViewModelBase msg in messages.OrderBy(m => m.Time))
        {
            if (lastDate == null || lastDate.Value.Date != msg.Time.Date)
            {
                result.Add(new DateSeparatorViewModel { Time = msg.Time });
            }

            lastDate = msg.Time;
            result.Add(msg);
        }
        return result;
    }

    private async IAsyncEnumerable<IEnumerable<MessageViewModelBase>> GeneratePersonalStream(Guid chatId)
    {
        Participant me = _users.First(u => u.Id == _currentUserId);
        Participant partner = _users.FirstOrDefault(u => u.Id != _currentUserId) ?? new Participant(Guid.NewGuid(), "Partner", "");

        DateTime? lastDate = null;

        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(30);
            List<MessageModel> batchModels = new();

            DateTime baseTime = (i == 0) ? DateTime.Now.AddDays(-1) : DateTime.Now.AddMinutes(-20 + i * 5);

            if (i == 0)
            {
                batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, partner.Id, "Hey, did you see the report?", baseTime, MessageType.Text));
            }
            else
            {
                batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, me.Id, $"adsfadsdafadsfsadfasdfWorking on step {i}...", baseTime, MessageType.Text));
                if (i % 2 == 0)
                {
                    batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, partner.Id, "Cool.", baseTime.AddMinutes(1), MessageType.Text));
                }
            }

            if (batchModels.Any())
            {
                IEnumerable<MessageViewModelBase> vms = MapToViewModels(batchModels);
                yield return InjectDateSeparators(vms, ref lastDate);
            }
        }
    }

    private void InitializeData()
    {

        Participant me = new(_currentUserId, "Me", "");
        Participant sarah = new(Guid.NewGuid(), "Sarah Chen", "user1.jpg");
        Participant marcus = new(Guid.NewGuid(), "Marcus Reid", "user2.jpg");
        Participant emma = new(Guid.NewGuid(), "Emma Wilson", "user3.jpg");
        Participant alex = new(Guid.NewGuid(), "Alex Chen", "user4.jpg");
        Participant lisa = new(Guid.NewGuid(), "Lisa Park", "user5.jpg");
        Participant john = new(Guid.NewGuid(), "John Doe", "user6.jpg");

        _users.AddRange(new[] { me, sarah, marcus, emma, alex, lisa, john });

        Guid chatSarahId = Guid.NewGuid();
        Guid chatGroupId = Guid.NewGuid();
        Guid chatChannelId = Guid.NewGuid();

        _chats.Add(new ChatModel(chatSarahId, "Sarah Chen", ChatType.Personal, new List<Guid> { me.Id, sarah.Id }));

        _chats.Add(new ChatModel(chatGroupId, "Design Team", ChatType.Group, new List<Guid> { me.Id, sarah.Id, marcus.Id, emma.Id, alex.Id }));

        _chats.Add(new ChatModel(chatChannelId, "Announcements", ChatType.Channel, new List<Guid> { me.Id }));

        _messages.Add(new MessageModel(Guid.NewGuid(), chatSarahId, sarah.Id, "Awesome! Can't wait to see them.", DateTime.Now.AddMinutes(-5), MessageType.Text));
        _messages.Add(new MessageModel(Guid.NewGuid(), chatGroupId, marcus.Id, "Guys, check the Figma updates.", DateTime.Now.AddMinutes(-30), MessageType.Text));
        _messages.Add(new MessageModel(Guid.NewGuid(), chatChannelId, me.Id, "Release notes v2.0", DateTime.Now.AddDays(-1), MessageType.Text));
    }

    private IEnumerable<MessageViewModelBase> MapToViewModels(List<MessageModel> models)
    {
        List<MessageViewModelBase> result = new();

        List<MessageModel> allKnownMessages = _messages.Concat(models).ToList();

        foreach (MessageModel msg in models)
        {
            Participant? sender = _users.FirstOrDefault(u => u.Id == msg.SenderId);
            bool isMine = msg.SenderId == _currentUserId;
            Bitmap? avatar = LoadAvatar(sender?.AvatarPath ?? "");

            if (msg.Type == MessageType.Reply && msg.ReplyToMessageId.HasValue)
            {
                MessageModel? originalMsg = allKnownMessages.FirstOrDefault(m => m.Id == msg.ReplyToMessageId.Value);
                Participant? originalSender = _users.FirstOrDefault(u => u.Id == originalMsg?.SenderId);

                result.Add(new ReplyMessageViewModel
                {
                    Text = msg.Text,
                    Time = msg.Timestamp,
                    IsMine = isMine,
                    SenderName = sender?.Name ?? "Unknown",
                    SenderAvatar = avatar,
                    QuotedText = originalMsg?.Text ?? "Deleted message",
                    QuotedAuthor = originalSender?.Name ?? "Unknown"
                });
            }
            else
            {
                result.Add(new SimpleMessageViewModel
                {
                    Text = msg.Text,
                    Time = msg.Timestamp,
                    IsMine = isMine,
                    SenderName = sender?.Name ?? "Unknown",
                    SenderAvatar = avatar
                });
            }
        }
        return result;
    }

    private Bitmap? LoadAvatar(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        try
        {
            Uri uri = new($"avares://Ecliptix.Core/Assets/DataSeed/{fileName}");
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch { return null; }
    }
}
