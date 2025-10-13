using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class DetectLanguageDialog : ReactiveUserControl<DetectLanguageDialogViewModel>
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

    public DetectLanguageDialog()
    {
        InitializeComponent();
        ApplyPrecompiledResources();
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

}
