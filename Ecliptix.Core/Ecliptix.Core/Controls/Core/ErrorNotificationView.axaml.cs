using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Ecliptix.Core.Controls.Core;

public partial class ErrorNotificationView : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ErrorNotificationView, string?>(nameof(Text));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<ErrorNotificationView, bool>(nameof(IsActive));

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private CancellationTokenSource? _timerCts;

    public ErrorNotificationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    // Публічний метод для показу помилки
    public void ShowError(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            Hide();
            return;
        }

        // Скасовуємо попередній таймер
        _timerCts?.Cancel();
        _timerCts?.Dispose();

        // Встановлюємо текст
        Text = message;

        // Показуємо (або залишаємо видимим якщо вже показано)
        IsActive = true;

        // Запускаємо новий таймер
        _timerCts = new CancellationTokenSource();
        _ = AutoHideAsync(_timerCts.Token);
    }

    // Публічний метод для приховування
    public void Hide()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;

        IsActive = false;
        Text = string.Empty;
    }

    private async Task AutoHideAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);

            if (!token.IsCancellationRequested)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsActive = false;
                });
            }
        }
        catch (TaskCanceledException)
        {
            // Ігноруємо скасування
        }
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Hide();
}
