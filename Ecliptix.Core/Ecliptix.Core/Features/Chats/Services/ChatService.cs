using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ecliptix.Core.Features.Chats.Models;
using Ecliptix.Core.Features.Chats.ViewModels;
using Ecliptix.Core.Features.Chats.ViewModels.Messages;

namespace Ecliptix.Core.Features.Chats.Services;

public interface IChatService
{
    Task<IEnumerable<ChatListItemViewModel>> GetChatsAsync();

    IAsyncEnumerable<IEnumerable<MessageViewModelBase>> GetMessagesStreamAsync(Guid chatId);
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

        Participant me = new Participant(_currentUserId, "Me", "");
        Participant sarah = new Participant(Guid.NewGuid(), "Sarah Chen", "user1.jpg"); // Переконайся, що файл є в Assets
        Participant designTeam = new Participant(Guid.NewGuid(), "Design Team", "");

        _users.Add(me);
        _users.Add(sarah);
        _users.Add(designTeam);

        Guid chatSarahId = Guid.NewGuid();
        Guid chatGroupId = Guid.NewGuid();

        _chats.Add(new ChatModel(chatSarahId, "Sarah Chen", ChatType.Personal, new List<Guid> { me.Id, sarah.Id }));
        _chats.Add(new ChatModel(chatGroupId, "Design Team", ChatType.Group, new List<Guid> { me.Id, sarah.Id, designTeam.Id }));

        _messages.Add(new MessageModel(Guid.NewGuid(), chatSarahId, sarah.Id, "Awesome! Can't wait to see them.", DateTime.Now.AddMinutes(-5), MessageType.Text));
        _messages.Add(new MessageModel(Guid.NewGuid(), chatGroupId, designTeam.Id, "Guys, check the Figma updates.", DateTime.Now.AddMinutes(-30), MessageType.Text));
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
        catch
        {
            return null;
        }
    }

    public async Task<IEnumerable<ChatListItemViewModel>> GetChatsAsync()
    {
        await Task.Delay(50);

        List<ChatListItemViewModel> sidebarItems = new List<ChatListItemViewModel>();
        Random rnd = new Random();

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

            ChatListItemViewModel item = new ChatListItemViewModel
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
            };

            sidebarItems.Add(item);
        }

        return sidebarItems.OrderByDescending(x => x.LastMessageTime);
    }

    public async IAsyncEnumerable<IEnumerable<MessageViewModelBase>> GetMessagesStreamAsync(Guid chatId)
    {
        Participant me = _users.First(u => u.Id == _currentUserId);
        Participant partner = _users.FirstOrDefault(u => u.Id != _currentUserId) ?? new Participant(Guid.NewGuid(), "Partner", "");

        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(500);

            Guid msg1Id = Guid.NewGuid();


            List<MessageModel> batchModels = new List<MessageModel>
            {
                // 1. Вхідне
                new MessageModel(msg1Id, chatId, partner.Id, $"Batch {i+1}: Hey! How's the project going?", DateTime.Now.AddMinutes(-50 + i), MessageType.Text),

                // 2. Вихідне
                new MessageModel(Guid.NewGuid(), chatId, me.Id, "Going great! Just finished the new designs", DateTime.Now.AddMinutes(-45 + i), MessageType.Text),

                // 3. Reply (Відповідь на перше повідомлення в цій пачці)
                new MessageModel(Guid.NewGuid(), chatId, me.Id, "Yes, absolutely! I'll send you the files right now.", DateTime.Now.AddMinutes(-40 + i), MessageType.Reply, msg1Id),

                // 4. Вхідне
                new MessageModel(Guid.NewGuid(), chatId, partner.Id, "Awesome! Can't wait to see them.", DateTime.Now.AddMinutes(-35 + i), MessageType.Text)
            };

            List<MessageViewModelBase> batchViewModels = new List<MessageViewModelBase>();

            foreach (MessageModel msg in batchModels)
            {
                Participant sender = (msg.SenderId == me.Id) ? me : partner;
                bool isMine = msg.SenderId == _currentUserId;

                if (msg.Type == MessageType.Reply && msg.ReplyToMessageId.HasValue)
                {

                    MessageModel? originalMsg = batchModels.FirstOrDefault(m => m.Id == msg.ReplyToMessageId.Value);
                    Participant? originalSender = (originalMsg?.SenderId == me.Id) ? me : partner;

                    batchViewModels.Add(new ReplyMessageViewModel
                    {
                        Text = msg.Text,
                        Time = msg.Timestamp,
                        IsMine = isMine,
                        SenderName = sender.Name,
                        QuotedText = originalMsg?.Text ?? "Deleted message",
                        QuotedAuthor = originalSender?.Name ?? "Unknown"
                    });
                }
                else
                {
                    batchViewModels.Add(new SimpleMessageViewModel
                    {
                        Text = msg.Text,
                        Time = msg.Timestamp,
                        IsMine = isMine,
                        SenderName = sender.Name
                    });
                }
            }

            yield return batchViewModels;
        }
    }
}
