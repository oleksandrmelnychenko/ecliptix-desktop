using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class DetectLanguageDialog : UserControl, IActivatableView, IDisposable
{
    private static class ResourceKeys
    {
        public const string ButtonHeight = nameof(ButtonHeight);
        public const string TitleFontSize = nameof(TitleFontSize);
        public const string SubtitleFontSize = nameof(SubtitleFontSize);
        public const string ButtonTransitionDuration = nameof(ButtonTransitionDuration);
    }

    private static readonly FrozenDictionary<string, object> PrecompiledResources =
        new Dictionary<string, object>
        {
            [ResourceKeys.ButtonHeight] = 44.0,
            [ResourceKeys.TitleFontSize] = 22.0,
            [ResourceKeys.SubtitleFontSize] = 17.0,
            [ResourceKeys.ButtonTransitionDuration] = TimeSpan.FromMilliseconds(250)
        }.ToFrozenDictionary();

    private CompositeDisposable? _activationDisposables;

    public ViewModelActivator Activator { get; } = new();

    public DetectLanguageDialog()
    {
        InitializeComponent();
        ApplyPrecompiledResources();
    }

    public void SetLocalizationService(
        ILocalizationService localizationService,
        IUnifiedMessageBus messageBus,
        NetworkProvider networkProvider)
    {
        DataContext = new DetectLanguageDialogViewModel(
            localizationService,
            messageBus,
            networkProvider
            ); SetupReactiveBindings();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ApplyPrecompiledResources()
    {
        foreach ((string key, object value) in PrecompiledResources)
        {
            Resources[key] = value;
        }
    }

    private void SetupReactiveBindings()
    {
        this.WhenActivated(disposables =>
        {
            _activationDisposables = disposables;
        });
    }

    public void Dispose()
    {
        _activationDisposables?.Dispose();
        GC.SuppressFinalize(this);
    }
}