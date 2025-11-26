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
        Participant sarah = new Participant(Guid.NewGuid(), "Sarah Chen", "user1.jpg");
        Participant marcus = new Participant(Guid.NewGuid(), "Marcus Reid", "user2.jpg"); // Переконайся, що такий файл є, або юзай user1.jpg
        Participant emma = new Participant(Guid.NewGuid(), "Emma Wilson", "user3.jpg");   // Переконайся, що такий файл є

        _users.AddRange(new[] { me, sarah, marcus, emma });

        Guid chatSarahId = Guid.NewGuid();
        Guid chatGroupId = Guid.NewGuid();

        // Personal Chat
        _chats.Add(new ChatModel(chatSarahId, "Sarah Chen", ChatType.Personal, new List<Guid> { me.Id, sarah.Id }));

        // Group Chat (Додаємо IDs всіх учасників)
        _chats.Add(new ChatModel(chatGroupId, "Design Team", ChatType.Group, new List<Guid> { me.Id, sarah.Id, marcus.Id, emma.Id }));

        // === 3. ПОЧАТКОВІ ПОВІДОМЛЕННЯ (для Sidebar) ===
        _messages.Add(new MessageModel(Guid.NewGuid(), chatSarahId, sarah.Id, "Awesome! Can't wait to see them.", DateTime.Now.AddMinutes(-5), MessageType.Text));

        // Початкове повідомлення в групі від Маркуса
        _messages.Add(new MessageModel(Guid.NewGuid(), chatGroupId, marcus.Id, "Guys, check the Figma updates.", DateTime.Now.AddMinutes(-30), MessageType.Text));

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
        ChatModel? chat = _chats.FirstOrDefault(c => c.Id == chatId);
        if (chat == null)
        {
            yield break;
        }

        // 2. Знаходимо учасників З ГЛОБАЛЬНОГО СПИСКУ (це вирішує проблему Unknown)
        Participant me = _users.First(u => u.Id == _currentUserId);

        // Якщо це група "Design Team", знаходимо конкретних людей для сценарію
        Participant? marcus = _users.FirstOrDefault(u => u.Name.Contains("Marcus"));
        Participant? emma = _users.FirstOrDefault(u => u.Name.Contains("Emma"));
        Participant? sarah = _users.FirstOrDefault(u => u.Name.Contains("Sarah"));

        // Якщо раптом когось не знайшли (запобіжник), беремо рандомних
        if (marcus == null)
        {
            marcus = _users.First(u => u.Id != _currentUserId);
        }

        if (emma == null)
        {
            emma = marcus;
        }

        if (chat.Type == ChatType.Group)
        {

            List<MessageModel> batchModels = new List<MessageModel>();

            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(300); // Пауза для імітації набору тексту


                Guid msgId = Guid.NewGuid();

                switch (i)
                {
                    case 0:
                        batchModels.Add(new MessageModel(msgId, chatId, marcus.Id, "Hey team! Just uploaded the new icons to Figma.", DateTime.Now.AddMinutes(-10), MessageType.Text));
                        batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, emma.Id, "Thanks Marcus! Checking them now.", DateTime.Now.AddMinutes(-9), MessageType.Text));
                        break;
                    case 1:
                        batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, me.Id, "They look clean. Are we using the outlined version?", DateTime.Now.AddMinutes(-8), MessageType.Text));
                        break;
                    case 2:
                        batchModels.Add(new MessageModel(msgId, chatId, marcus.Id, "Yes, outlined for the main UI, filled for active states.", DateTime.Now.AddMinutes(-7), MessageType.Reply, msgId)); // Відповідь мені
                        break;
                    case 3:
                        batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, sarah.Id, "I think the 'Settings' icon is a bit too small compared to others.", DateTime.Now.AddMinutes(-5), MessageType.Text));
                        batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, marcus.Id, "Good catch, Sarah. I'll resize it to 24px.", DateTime.Now.AddMinutes(-4), MessageType.Text));
                        break;
                    case 4:
                        batchModels.Add(new MessageModel(Guid.NewGuid(), chatId, emma.Id, "Perfect. Let's freeze the design by 5 PM.", DateTime.Now.AddMinutes(-2), MessageType.Text));
                        break;
                }

                yield return MapToViewModels(batchModels);
            }
        }
        else
        {
            List<MessageModel> batchModels = new();
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(500);

                Guid msg1Id = Guid.NewGuid();

                Participant? partner = sarah;

                batchModels.AddRange(new List<MessageModel>
                {
                    new MessageModel(msg1Id, chatId, partner.Id, $"Batch {i+1}: Hey! How's the project going?", DateTime.Now.AddMinutes(-50 + i), MessageType.Text),

                    new MessageModel(Guid.NewGuid(), chatId, me.Id, "Going great! Just finished the new designs", DateTime.Now.AddMinutes(-45 + i), MessageType.Text),

                    new MessageModel(Guid.NewGuid(), chatId, me.Id, "Yes, absolutely! I'll send you the files right now.", DateTime.Now.AddMinutes(-40 + i), MessageType.Reply, msg1Id),

                    new MessageModel(Guid.NewGuid(), chatId, partner.Id, "Awesome! Can't wait to see them.", DateTime.Now.AddMinutes(-35 + i), MessageType.Text)
                });

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

    private IEnumerable<MessageViewModelBase> MapToViewModels(List<MessageModel> models)
    {
        List<MessageViewModelBase> result = new List<MessageViewModelBase>();
        foreach (MessageModel msg in models)
        {
            Participant? sender = _users.FirstOrDefault(u => u.Id == msg.SenderId);
            bool isMine = msg.SenderId == _currentUserId;
            Bitmap? avatar = LoadAvatar(sender?.AvatarPath ?? "");

            if (msg.Type == MessageType.Reply && msg.ReplyToMessageId.HasValue)
            {
                MessageModel? originalMsg = _messages.Concat(models).FirstOrDefault(m => m.Id == msg.ReplyToMessageId.Value);
                Participant? originalSender = _users.FirstOrDefault(u => u.Id == originalMsg?.SenderId);

                result.Add(new ReplyMessageViewModel
                {
                    Text = msg.Text,
                    Time = msg.Timestamp,
                    IsMine = isMine,
                    SenderName = sender?.Name ?? "Unknown",
                    SenderAvatar = avatar,
                    QuotedText = originalMsg?.Text ?? "...",
                    QuotedAuthor = originalSender?.Name ?? "..."
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
}
