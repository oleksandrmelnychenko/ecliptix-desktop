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
        public const string BUTTON_HEIGHT = nameof(BUTTON_HEIGHT);
        public const string TITLE_FONT_SIZE = nameof(TITLE_FONT_SIZE);
        public const string SUBTITLE_FONT_SIZE = nameof(SUBTITLE_FONT_SIZE);
        public const string BUTTON_TRANSITION_DURATION = nameof(BUTTON_TRANSITION_DURATION);
    }

    private static readonly FrozenDictionary<string, object> PrecompiledResources =
        new Dictionary<string, object>
        {
            [ResourceKeys.BUTTON_HEIGHT] = 44.0,
            [ResourceKeys.TITLE_FONT_SIZE] = 22.0,
            [ResourceKeys.SUBTITLE_FONT_SIZE] = 17.0,
            [ResourceKeys.BUTTON_TRANSITION_DURATION] = TimeSpan.FromMilliseconds(250)
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
