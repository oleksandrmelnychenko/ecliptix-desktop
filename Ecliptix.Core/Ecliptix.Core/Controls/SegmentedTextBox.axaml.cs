using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ecliptix.Core.Controls
{
    public static class SegmentedTextBoxConstants
    {
        public const string SegmentStyleClass = "segment";
        public const string ActiveStyleClass = "active";
        public const int DefaultSegmentCount = 4;
        public const double DefaultSegmentSpacing = 8.0;
        public const double DefaultSegmentWidth = 40.0;
        public const bool DefaultAllowOnlyNumbers = false;
        public const bool IsPonterInteractionEnabled = true;
    }
    public partial class SegmentedTextBox : UserControl
    {
        private List<TextBox> _segments = new();
        private bool _lastIsComplete;
        private int _currentActiveIndex;

        public static readonly StyledProperty<int> SegmentCountProperty =
            AvaloniaProperty.Register<SegmentedTextBox, int>(nameof(SegmentCount), SegmentedTextBoxConstants.DefaultSegmentCount, validate: value => value > 0);

        public static readonly StyledProperty<bool> AllowOnlyNumbersProperty =
            AvaloniaProperty.Register<SegmentedTextBox, bool>(nameof(AllowOnlyNumbers), SegmentedTextBoxConstants.DefaultAllowOnlyNumbers);

        public static readonly StyledProperty<bool> IsPointerInteractionEnabledProperty =
            AvaloniaProperty.Register<SegmentedTextBox, bool>(nameof(IsPointerInteractionEnabled), SegmentedTextBoxConstants.IsPonterInteractionEnabled);

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
            AvaloniaProperty.Register<SegmentedTextBox, double>(nameof(SegmentSpacing), SegmentedTextBoxConstants.DefaultSegmentSpacing);

        public static readonly StyledProperty<double> SegmentWidthProperty =
            AvaloniaProperty.Register<SegmentedTextBox, double>(nameof(SegmentWidth), SegmentedTextBoxConstants.DefaultSegmentWidth);

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
        }

        private string GetConcatenatedValue()
        {
            return string.Concat(_segments.Select(tb => tb.Text ?? ""));
        }

        private static string ValidateNumericInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var firstDigit = input.FirstOrDefault(char.IsDigit);
            return firstDigit != default ? firstDigit.ToString() : "";
        }

        private bool ShouldProcessInput(int segmentIndex)
        {
            return IsPointerInteractionEnabled || segmentIndex == _currentActiveIndex;
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

            if (textBox.Text?.Length == 1 && ShouldProcessInput(index))
            {
                MoveToNextSegment();
            }
        }

        private void UpdateTextBoxText(TextBox textBox, string newText)
        {
            var selectionStart = textBox.SelectionStart;
            textBox.Text = newText;
            textBox.SelectionStart = Math.Min(selectionStart, newText.Length);
            textBox.SelectionEnd = textBox.SelectionStart;
        }

        private void BuildSegments()
        {
            if (SegmentsPanel == null) return;
            _segments.Clear();
            SegmentsPanel.Children.Clear();
            SegmentsPanel.Spacing = SegmentSpacing;
            _currentActiveIndex = 0;

            for (int i = 0; i < SegmentCount; i++)
            {
                var tb = new TextBox
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
                SegmentsPanel.Children.Add(tb);
            }
            UpdateTabIndexes();

        }

        private void CleanupSegments()
        {
            foreach (var segment in _segments)
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
            foreach (var tb in _segments)
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

            if (!ShouldProcessInput(index))
            {
                tb.Text = "";
                return;
            }

            ProcessTextInput(tb, index);
            OnSegmentChanged();
        }

        private void Segment_KeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;

            int index = _segments.IndexOf(tb);

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

        private void HandleBackspaceKey(int currentIndex)
        {
            var currentTextBox = _segments[currentIndex];

            if (!string.IsNullOrEmpty(currentTextBox.Text))
            {
                currentTextBox.Text = "";
            }
            else if (currentIndex > 0)
            {
                var previousIndex = currentIndex - 1;
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
                var tb = _segments[i];
                tb.Classes.Set(SegmentedTextBoxConstants.ActiveStyleClass, i == _currentActiveIndex);
            }

            FocusCurrentSegment();
        }

        public void ClearAllSegments()
        {
            foreach (var segment in _segments)
            {
                segment.Text = string.Empty;
            }
            SetActiveSegment(0);
            OnSegmentChanged();
        }

        public void ClearSegmentsAndSetActive(int activeIndex = 0)
        {
            ClearAllSegments();
            if (activeIndex >= 0 && activeIndex < _segments.Count)
            {
                SetActiveSegment(activeIndex);
            }
        }

        private void FocusCurrentSegment()
        {
            if (_currentActiveIndex >= 0 && _currentActiveIndex < _segments.Count)
            {
                var currentSegment = _segments[_currentActiveIndex];
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

            SetValue(ValueProperty, newValue);
            SetValue(IsCompleteProperty, newIsComplete);

            bool wasComplete = _lastIsComplete;
            if (wasComplete != newIsComplete)
            {
                _lastIsComplete = newIsComplete;
            }
        }

        private void UpdateTabIndexes()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                _segments[i].TabIndex = i == _currentActiveIndex ? BaseTabIndex : -1;
                _segments[i].IsTabStop = i == _currentActiveIndex;
            }
        }

        public void SetValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                ClearAllSegments();
                return;
            }

            for (int i = 0; i < Math.Min(value.Length, _segments.Count); i++)
            {
                _segments[i].Text = value[i].ToString();
            }

            for (int i = value.Length; i < _segments.Count; i++)
            {
                _segments[i].Text = string.Empty;
            }

            int nextActiveIndex = Math.Min(value.Length, _segments.Count - 1);
            SetActiveSegment(nextActiveIndex);
            OnSegmentChanged();
        }
    }
}