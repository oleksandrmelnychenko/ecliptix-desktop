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
    public partial class SegmentedTextBox : UserControl
    {
        private List<TextBox> _segments = new();
        private bool _lastIsComplete = false;
        private int _currentActiveIndex = 0; // Track current active segment

        public static readonly StyledProperty<int> SegmentCountProperty =
            AvaloniaProperty.Register<SegmentedTextBox, int>(nameof(SegmentCount), 4, validate: value => value > 0);

        public static readonly StyledProperty<bool> AllowOnlyNumbersProperty =
            AvaloniaProperty.Register<SegmentedTextBox, bool>(nameof(AllowOnlyNumbers), false);

        public static readonly StyledProperty<bool> IsPointerInteractionEnabledProperty =
            AvaloniaProperty.Register<SegmentedTextBox, bool>(nameof(IsPointerInteractionEnabled), true);

        public static readonly StyledProperty<IBrush> SegmentBackgroundProperty =
            AvaloniaProperty.Register<SegmentedTextBox, IBrush>(nameof(SegmentBackground), Brushes.Transparent);

        public static readonly StyledProperty<IBrush> ActiveSegmentBorderColorProperty =
            AvaloniaProperty.Register<SegmentedTextBox, IBrush>(nameof(ActiveSegmentBorderColor), Brushes.Blue);

        public static readonly StyledProperty<string> ValueProperty =
            AvaloniaProperty.Register<SegmentedTextBox, string>(nameof(Value), "",
                defaultBindingMode: Avalonia.Data.BindingMode.OneWay);
        
        public string Value
        {
            get
            {
                var sb = new StringBuilder(_segments.Count);
                foreach (var tb in _segments)
                {
                    sb.Append(tb.Text ?? "");
                }
                var value = sb.ToString();
                SetValue(ValueProperty, value);
                return value;
            }
        }
        
        public string GetText()
        {
            return Value; 
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

        public int CurrentActiveIndex
        {
            get => _currentActiveIndex;
            private set
            {
                if (_currentActiveIndex != value && value >= 0 && value < _segments.Count)
                {
                    _currentActiveIndex = value;
                    UpdateActiveSegment();
                }
            }
        }

        public bool IsComplete
        {
            get => _segments.All(tb => !string.IsNullOrEmpty(tb.Text));
        }

        public event EventHandler? IsCompleteChanged;

        public SegmentedTextBox()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            base.OnAttachedToLogicalTree(e);
            BuildSegments();
        }
        
     
        

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == SegmentCountProperty)
            {
                BuildSegments();
            }
            else if (change.Property == AllowOnlyNumbersProperty)
            {
                UpdateInputScope();
            }
        }

        private void BuildSegments()
        {
            if (SegmentsPanel == null) return;
            _segments.Clear();
            SegmentsPanel.Children.Clear();
            _currentActiveIndex = 0;

            for (int i = 0; i < SegmentCount; i++)
            {
                var tb = new TextBox
                {
                    TabIndex = i == 0 ? 0 : -1,
                    Classes = { "segment" } 
                };

                tb.AddHandler(KeyDownEvent, Segment_KeyDown, RoutingStrategies.Tunnel);
                tb.TextChanged += Segment_TextChanged;
                tb.LostFocus += Segment_LostFocus;
                tb.GotFocus += Segment_GotFocus; 
                _segments.Add(tb);
                SegmentsPanel.Children.Add(tb);
            }
            
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
            if (sender is TextBox tb)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    bool focusIsWithinControl = _segments.Any(segment => segment.IsFocused);
                    if (!focusIsWithinControl)
                    {
                        ClearActiveSegment();
                    }
                });
            }
        }

        private void ClearActiveSegment()
        {
            foreach (var tb in _segments)
            {
                tb.Classes.Remove("active");
            }
        }

        private void UpdateInputScope()
        {
            // Note: Avalonia doesn't have InputScope like WinUI
            // Input validation is handled in TextChanging event
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

            // Block text input if not the active segment when pointer interaction is disabled
            if (!IsPointerInteractionEnabled && index != _currentActiveIndex)
            {
                tb.Text = "";
                return;
            }

            // Validate numbers-only input
            if (AllowOnlyNumbers && !string.IsNullOrEmpty(tb.Text))
            {
                string validText = "";
                foreach (char c in tb.Text)
                {
                    if (char.IsDigit(c))
                    {
                        validText += c;
                        if (validText.Length >= 1) break; // Only allow one character
                    }
                }
                
                if (tb.Text != validText)
                {
                    var selectionStart = tb.SelectionStart;
                    tb.Text = validText;
                    tb.SelectionStart = Math.Min(selectionStart, validText.Length);
                    tb.SelectionEnd = tb.SelectionStart;
                }
            }

            // Only process if this is the active segment or pointer interaction is enabled
            if (IsPointerInteractionEnabled || index == _currentActiveIndex)
            {
                if (tb.Text?.Length == 1)
                {
                    // Move to next segment
                    MoveToNextSegment();
                }
            }

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

            // Block arrow keys to prevent manual navigation between segments
            if (e.Key == Key.Right || e.Key == Key.Left)
            {
                e.Handled = true;
                return;
            }

            // Allow Tab key to work normally (don't handle it) so focus moves to next control
            if (e.Key == Key.Tab)
            {
                // Don't handle Tab - let it work normally to move focus to next element
                return;
            }

            // Only process other keyboard input for the active segment when pointer interaction is disabled
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

            if (currentIndex == _segments.Count - 1) // Last segment
            {
                if (!string.IsNullOrEmpty(currentTextBox.Text))
                {
                    // If the last segment has text, just clear it and stay there
                    currentTextBox.Text = "";
                }
                else if (currentIndex > 0)
                {
                    // If the last segment is empty, move to previous and clear it
                    var previousIndex = currentIndex - 1;
                    SetActiveSegment(previousIndex);
                    _segments[previousIndex].Text = "";
                }
            }
            else if (currentIndex > 0) // Other segments (not first, not last)
            {
                if (!string.IsNullOrEmpty(currentTextBox.Text))
                {
                    // If current segment has text, clear it and stay there
                    currentTextBox.Text = "";
                }
                else
                {
                    // If current segment is empty, move to previous and clear it
                    var previousIndex = currentIndex - 1;
                    SetActiveSegment(previousIndex);
                    _segments[previousIndex].Text = "";
                }
            }
            else // First segment (index 0)
            {
                // Just clear the first segment and stay there
                currentTextBox.Text = "";
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
        
                // Remove or add the "active" class
                if (i == _currentActiveIndex)
                {
                    tb.Classes.Add("active");
                }
                else
                {
                    tb.Classes.Remove("active");
                }
            }

            if (_currentActiveIndex >= 0 && _currentActiveIndex < _segments.Count)
            {
                _segments[_currentActiveIndex].Focus();
                var tb = _segments[_currentActiveIndex];
                if (!string.IsNullOrEmpty(tb.Text))
                {
                    tb.SelectionStart = tb.Text.Length;
                    tb.SelectionEnd = tb.Text.Length;
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

        private void MoveToPreviousSegment()
        {
            if (_currentActiveIndex > 0)
            {
                SetActiveSegment(_currentActiveIndex - 1);
            }
        }

        // Updated method to find the correct active segment based on the new logic
        private int FindNextActiveSegment()
        {
            // Find the first empty segment, or the last segment if all are filled
            for (int i = 0; i < _segments.Count; i++)
            {
                if (string.IsNullOrEmpty(_segments[i].Text))
                {
                    return i;
                }
            }
            // If all segments are filled, return the last segment
            return _segments.Count - 1;
        }

        private void OnSegmentChanged()
        {
            bool wasComplete = _lastIsComplete;
            bool nowComplete = IsComplete;
            if (wasComplete != nowComplete)
            {
                _lastIsComplete = nowComplete;
                IsCompleteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        

        public void SetText(string text)
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                if (i < text.Length)
                {
                    _segments[i].Text = text[i].ToString();
                }
                else
                {
                    _segments[i].Text = "";
                }
            }

            // Set active segment to the next empty one or the last one
            int nextActiveIndex = FindNextActiveSegment();
            SetActiveSegment(nextActiveIndex);

            OnSegmentChanged();
        }

        public void Clear()
        {
            foreach (var tb in _segments)
            {
                tb.Text = "";
            }
            SetActiveSegment(0);
            OnSegmentChanged();
        }

        public void FocusFirstSegment()
        {
            SetActiveSegment(0);
        }

        private void UpdateTabIndexes()
        {
            for (int i = 0; i < _segments.Count; i++)
            {
                _segments[i].TabIndex = i == _currentActiveIndex ? 0 : -1;
                _segments[i].IsTabStop = i == _currentActiveIndex;
            }
        }
    }
}