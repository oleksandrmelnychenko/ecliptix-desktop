using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Ecliptix.Core.Controls.Constants;
using Serilog;

namespace Ecliptix.Core.Controls.Core;

public partial class SegmentedTextBox : UserControl
{
    private readonly List<TextBox> _segments = [];
    private bool _lastIsComplete;
    private int _currentActiveIndex;
    private static readonly string[] DigitStrings = new string[10];
    private bool _isInternalUpdate = false;
    static SegmentedTextBox()
    {
        for (int i = 0; i < 10; i++)
        {
            DigitStrings[i] = i.ToString();
        }
    }

    public static readonly StyledProperty<int> SegmentCountProperty =
        AvaloniaProperty.Register<SegmentedTextBox, int>(nameof(SegmentCount),
            SegmentedTextBoxConstants.DefaultSegmentCount, validate: value => value > 0);

    public static readonly StyledProperty<bool> AllowOnlyNumbersProperty =
        AvaloniaProperty.Register<SegmentedTextBox, bool>(nameof(AllowOnlyNumbers),
            SegmentedTextBoxConstants.DefaultAllowOnlyNumbers);

    public static readonly StyledProperty<bool> IsPointerInteractionEnabledProperty =
        AvaloniaProperty.Register<SegmentedTextBox, bool>(nameof(IsPointerInteractionEnabled),
            SegmentedTextBoxConstants.IsPointerInteractionEnabled);

    public static readonly StyledProperty<IBrush> SegmentBackgroundProperty =
        AvaloniaProperty.Register<SegmentedTextBox, IBrush>(nameof(SegmentBackground), Brushes.Transparent);

    public static readonly StyledProperty<IBrush> ActiveSegmentBorderColorProperty =
        AvaloniaProperty.Register<SegmentedTextBox, IBrush>(nameof(ActiveSegmentBorderColor), Brushes.Blue);

    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<SegmentedTextBox, string>(nameof(Value), "",
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsCompleteProperty =
        AvaloniaProperty.Register<SegmentedTextBox, bool>(nameof(IsComplete), false,
            defaultBindingMode: Avalonia.Data.BindingMode.OneWayToSource);

    public static readonly StyledProperty<double> SegmentSpacingProperty =
        AvaloniaProperty.Register<SegmentedTextBox, double>(nameof(SegmentSpacing),
            SegmentedTextBoxConstants.DefaultSegmentSpacing);

    public static readonly StyledProperty<double> SegmentWidthProperty =
        AvaloniaProperty.Register<SegmentedTextBox, double>(nameof(SegmentWidth),
            SegmentedTextBoxConstants.DefaultSegmentWidth);

    public static readonly StyledProperty<int> BaseTabIndexProperty =
        AvaloniaProperty.Register<SegmentedTextBox, int>(nameof(BaseTabIndex), int.MaxValue);

    public int BaseTabIndex
    {
        get => GetValue(BaseTabIndexProperty);
        set => SetValue(BaseTabIndexProperty, value);
    }

    public double SegmentSpacing
    {
        get => GetValue(SegmentSpacingProperty);
        set => SetValue(SegmentSpacingProperty, value);
    }

    public double SegmentWidth
    {
        get => GetValue(SegmentWidthProperty);
        set => SetValue(SegmentWidthProperty, value);
    }

    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IBrush SegmentBackground
    {
        get => GetValue(SegmentBackgroundProperty);
        set => SetValue(SegmentBackgroundProperty, value);
    }

    public IBrush ActiveSegmentBorderColor
    {
        get => GetValue(ActiveSegmentBorderColorProperty);
        set => SetValue(ActiveSegmentBorderColorProperty, value);
    }

    public int SegmentCount
    {
        get => GetValue(SegmentCountProperty);
        set => SetValue(SegmentCountProperty, value);
    }

    public bool AllowOnlyNumbers
    {
        get => GetValue(AllowOnlyNumbersProperty);
        set => SetValue(AllowOnlyNumbersProperty, value);
    }

    public bool IsPointerInteractionEnabled
    {
        get => GetValue(IsPointerInteractionEnabledProperty);
        set => SetValue(IsPointerInteractionEnabledProperty, value);
    }

    public bool IsComplete
    {
        get => GetValue(IsCompleteProperty);
        private set => SetValue(IsCompleteProperty, value);
    }

    public SegmentedTextBox()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        BuildSegments();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        CleanupSegments();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SegmentCountProperty)
        {
            BuildSegments();
        }

        if (change.Property == ValueProperty && !_isInternalUpdate)
        {
            string newValue = change.NewValue as string ?? string.Empty;
            
            if(string.IsNullOrEmpty(newValue)) ClearAllSegments();
            else UpdateSegmentsFromValue(newValue);
        }
    }
    
    private void UpdateSegmentsFromValue(string newValue)
    {
        _isInternalUpdate = true;
        try
        {
            foreach (TextBox segment in _segments)
            {
                segment.Text = string.Empty;
            }
            
            for (int i = 0; i < _segments.Count; i++)
            {
                if (i >= newValue.Length)
                    break;
                _segments[i].Text = newValue[i].ToString();
            }

            int nextIndex = Math.Min(newValue.Length, _segments.Count - 1);
            SetActiveSegment(nextIndex);
        }
        finally
        {
            _isInternalUpdate = false;
        }
    }

    private string GetConcatenatedValue()
    {
        return string.Concat(_segments.Select(tb => tb.Text ?? ""));
    }

    private static string ValidateNumericInput(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        char firstDigit = input.FirstOrDefault(char.IsDigit);
        if (firstDigit == 0) return "";

        int digitValue = firstDigit - '0';
        return digitValue is >= 0 and <= 9 ? DigitStrings[digitValue] : firstDigit.ToString();
    }

    private void ProcessTextInput(TextBox textBox, int index)
    {
        if (AllowOnlyNumbers)
        {
            string inputText = textBox.Text ?? string.Empty;
            string validText = ValidateNumericInput(inputText);
            if (inputText != validText)
            {
                UpdateTextBoxText(textBox, validText);
            }
        }

        if (textBox.Text?.Length == 1 && !_isInternalUpdate)
        {
            MoveToNextSegment();
        }
    }

    private void UpdateTextBoxText(TextBox textBox, string newText)
    {
        int selectionStart = textBox.SelectionStart;
        textBox.Text = newText;
        textBox.SelectionStart = Math.Min(selectionStart, newText.Length);
        textBox.SelectionEnd = textBox.SelectionStart;
    }

    private void BuildSegments()
    {
        StackPanel? segmentsPanel = this.FindControl<StackPanel>("SegmentsPanel");
        if (segmentsPanel == null) return;
        _segments.Clear();
        segmentsPanel.Children.Clear();
        segmentsPanel.Spacing = SegmentSpacing;
        _currentActiveIndex = 0;

        for (int i = 0; i < SegmentCount; i++)
        {
            TextBox tb = new()
            {
                TabIndex = i == 0 ? 0 : -1,
                Classes = { SegmentedTextBoxConstants.SegmentStyleClass },
                Width = SegmentWidth,
            };

            tb.AddHandler(KeyDownEvent, Segment_KeyDown, RoutingStrategies.Tunnel);
            tb.TextChanged += Segment_TextChanged;
            tb.LostFocus += Segment_LostFocus;
            tb.GotFocus += Segment_GotFocus;
            _segments.Add(tb);
            segmentsPanel.Children.Add(tb);
        }

        UpdateTabIndexes();
    }

    private void CleanupSegments()
    {
        foreach (TextBox segment in _segments)
        {
            UnsubscribeSegmentEvents(segment);
        }

        _segments.Clear();
    }

    private void UnsubscribeSegmentEvents(TextBox textBox)
    {
        textBox.RemoveHandler(KeyDownEvent, Segment_KeyDown);
        textBox.TextChanged -= Segment_TextChanged;
        textBox.LostFocus -= Segment_LostFocus;
        textBox.GotFocus -= Segment_GotFocus;
    }

    private void Segment_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            int index = _segments.IndexOf(tb);
            if (index >= 0)
            {
                _currentActiveIndex = index;
                UpdateActiveSegment();
            }
        }
    }

    private void Segment_LostFocus(object? sender, RoutedEventArgs e)
    {
        HandleFocusChange();
    }

    private void HandleFocusChange()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_segments.Any(segment => segment.IsFocused))
            {
                ClearActiveSegment();
            }
        });
    }

    private void ClearActiveSegment()
    {
        foreach (TextBox tb in _segments)
        {
            tb.Classes.Remove(SegmentedTextBoxConstants.ActiveStyleClass);
        }
    }

    private void OverlayRectangle_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_currentActiveIndex >= 0 && _currentActiveIndex < _segments.Count)
        {
            _segments[_currentActiveIndex].Focus();
        }

        e.Handled = true;
    }

    private void OverlayRectangle_Tapped(object? sender, TappedEventArgs e)
    {
        if (_currentActiveIndex >= 0 && _currentActiveIndex < _segments.Count)
        {
            _segments[_currentActiveIndex].Focus();
        }

        e.Handled = true;
    }

    private void Segment_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        int index = _segments.IndexOf(tb);

        ProcessTextInput(tb, index);
        OnSegmentChanged();
    }

    private void Segment_KeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;

        int index = _segments.IndexOf(tb);
        
        if (e.Key == Key.V && e.KeyModifiers == KeyModifiers.Control)
        {
            HandlePasteAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            HandleBackspaceKey(index);
            e.Handled = true;
            OnSegmentChanged();
            return;
        }

        if (e.Key == Key.Right || e.Key == Key.Left)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Tab)
        {
            return;
        }

        if (!IsPointerInteractionEnabled && index != _currentActiveIndex)
        {
            e.Handled = true;
            return;
        }

        OnSegmentChanged();
    }
    
    private async void HandlePasteAsync()
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            string? clipboardText = await clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(clipboardText)) return;

            ProcessPastedText(clipboardText);
        }
        catch
        {
            Log.Warning("Failed to access clipboard for pasting.");
        }
    }
    
    private void ProcessPastedText(string pastedText)
    {
        string validText = AllowOnlyNumbers
            ? new string(pastedText.Where(char.IsDigit).ToArray())
            : pastedText;

        if (string.IsNullOrEmpty(validText)) return;
        
        if (AllowOnlyNumbers && !validText.All(char.IsDigit))
        {
            Log.Warning("Pasted text contains non-numeric characters when only numbers are allowed.");
            return;
        }
        
        if (validText.Length != _segments.Count)
        {
            Log.Warning($"Pasted text length ({validText.Length}) does not match segment count ({_segments.Count}).");
            return;
        }
        
        UpdateSegmentsFromValue(validText);
    }

    private void HandleBackspaceKey(int currentIndex)
    {
        TextBox currentTextBox = _segments[currentIndex];

        if (!string.IsNullOrEmpty(currentTextBox.Text))
        {
            currentTextBox.Text = "";
        }
        else if (currentIndex > 0)
        {
            int previousIndex = currentIndex - 1;
            SetActiveSegment(previousIndex);
            _segments[previousIndex].Text = "";
        }
    }

    private void SetActiveSegment(int index)
    {
        if (index >= 0 && index < _segments.Count)
        {
            _currentActiveIndex = index;
            UpdateTabIndexes();
            UpdateActiveSegment();
        }
    }

    private void UpdateActiveSegment()
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            TextBox tb = _segments[i];
            tb.Classes.Set(SegmentedTextBoxConstants.ActiveStyleClass, i == _currentActiveIndex);
        }

        FocusCurrentSegment();
    }

    public void ClearAllSegments()
    {
        foreach (TextBox segment in _segments)
        {
            segment.Text = string.Empty;
        }

        SetActiveSegment(0);
        OnSegmentChanged();
    }

    private void FocusCurrentSegment()
    {
        if (_currentActiveIndex >= 0 && _currentActiveIndex < _segments.Count)
        {
            TextBox currentSegment = _segments[_currentActiveIndex];
            currentSegment.Focus();

            if (!string.IsNullOrEmpty(currentSegment.Text))
            {
                currentSegment.SelectionStart = currentSegment.Text.Length;
                currentSegment.SelectionEnd = currentSegment.Text.Length;
            }
        }
    }

    private void MoveToNextSegment()
    {
        if (_currentActiveIndex < _segments.Count - 1)
        {
            SetActiveSegment(_currentActiveIndex + 1);
        }
    }

    private void OnSegmentChanged()
    {
        string newValue = GetConcatenatedValue();
        bool newIsComplete = _segments.All(tb => !string.IsNullOrEmpty(tb.Text));

        if (!_isInternalUpdate && Value != newValue)
        {
            _isInternalUpdate = true;
            try
            {
                SetValue(ValueProperty, newValue);
            }
            finally
            {
                _isInternalUpdate = false;
            }
        }
        
        if (IsComplete != newIsComplete)
            SetValue(IsCompleteProperty, newIsComplete);
        
        _lastIsComplete = newIsComplete;
    }

    private void UpdateTabIndexes()
    {
        for (int i = 0; i < _segments.Count; i++)
        {
            _segments[i].TabIndex = i == _currentActiveIndex ? BaseTabIndex : -1;
            _segments[i].IsTabStop = i == _currentActiveIndex;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}