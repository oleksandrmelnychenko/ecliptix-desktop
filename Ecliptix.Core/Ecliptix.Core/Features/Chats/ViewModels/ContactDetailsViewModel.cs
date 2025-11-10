using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Ecliptix.Core.Features.Chats.Models;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public sealed class ContactDetailsViewModel : Ecliptix.Core.Core.MVVM.ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public ChatContact? SelectedContact { get; set; }
    [Reactive] public bool IsVisible { get; set; } = true;
    [Reactive] public string SelectedTab { get; set; } = "Media";

    public ObservableCollection<SharedMediaItem> SharedMedia { get; }
    public ObservableCollection<SharedMediaItem> SharedPhotos { get; }
    public ObservableCollection<SharedMediaItem> SharedFiles { get; }

    public ReactiveCommand<Unit, Unit> ToggleVisibilityCommand { get; }
    public ReactiveCommand<Unit, Unit> MuteCommand { get; }
    public ReactiveCommand<Unit, Unit> BlockCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearHistoryCommand { get; }
    public ReactiveCommand<string, Unit> SelectTabCommand { get; }

    public ContactDetailsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService)
        : base(networkProvider, localizationService, null)
    {
        SharedMedia = new ObservableCollection<SharedMediaItem>();
        SharedPhotos = new ObservableCollection<SharedMediaItem>();
        SharedFiles = new ObservableCollection<SharedMediaItem>();

        ToggleVisibilityCommand = ReactiveCommand.Create(() =>
        {
            IsVisible = !IsVisible;
        });

        MuteCommand = ReactiveCommand.Create(() =>
        {
        });

        BlockCommand = ReactiveCommand.Create(() =>
        {
        });

        ClearHistoryCommand = ReactiveCommand.Create(() =>
        {
        });

        SelectTabCommand = ReactiveCommand.Create<string>(tab =>
        {
            SelectedTab = tab;
        });

        GenerateMockContact();
    }

    private void GenerateMockContact()
    {
        SelectedContact = new ChatContact
        {
            Id = "1",
            Name = "Alice Johnson",
            PhoneNumber = "+1 (555) 123-4567",
            Email = "alice.johnson@example.com",
            Avatar = "AJ",
            IsOnline = true,
            LastSeen = null,
            StatusMessage = "Available",
            About = "Product Designer at TechCorp"
        };

        SharedMedia.Add(new SharedMediaItem
        {
            Id = "media-1",
            Type = MessageType.Image,
            ThumbnailPath = "",
            FilePath = "",
            FileName = "vacation.jpg",
            Size = 2048000,
            Timestamp = DateTime.Now.AddDays(-5)
        });

        SharedMedia.Add(new SharedMediaItem
        {
            Id = "media-2",
            Type = MessageType.File,
            ThumbnailPath = "",
            FilePath = "",
            FileName = "report.pdf",
            Size = 1024000,
            Timestamp = DateTime.Now.AddDays(-10)
        });

        SharedMedia.Add(new SharedMediaItem
        {
            Id = "media-3",
            Type = MessageType.Image,
            ThumbnailPath = "",
            FilePath = "",
            FileName = "screenshot.png",
            Size = 512000,
            Timestamp = DateTime.Now.AddDays(-15)
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            ToggleVisibilityCommand?.Dispose();
            MuteCommand?.Dispose();
            BlockCommand?.Dispose();
            ClearHistoryCommand?.Dispose();
            SelectTabCommand?.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
