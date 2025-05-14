using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Grpc.Core;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels;

public class ChatMessage
{
    public string Message { get; set; }
    public bool IsFromUser { get; set; }
    public DateTime Timestamp { get; set; }
}
public class BooleanToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? new SolidColorBrush(Color.Parse("#007AFF")) : new SolidColorBrush(Color.Parse("#E9ECEF"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BooleanToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? new SolidColorBrush(Colors.White) : new SolidColorBrush(Color.Parse("#212529"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BooleanToAlignmentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}


public class MainViewModel : ReactiveObject
{
    public ObservableCollection<ChatMessage> ChatItems { get; }

    
    public MainViewModel()
    {
        ChatItems = new ObservableCollection<ChatMessage>();
        GenerateSampleMessages();

        
        SendRequestCommand = ReactiveCommand.Create(() =>
        {
            try
            {
                StatusText = "Sending request...";


                ShowContent = !ShowContent;
            }
            catch (RpcException ex)
            {
                StatusText = $"Error: {ex.Status.Detail}";
                Console.WriteLine($"gRPC request failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                StatusText = $"Unexpected error: {ex.Message}";
                Console.WriteLine($"Unexpected error: {ex.Message}");
            }
        });
    }
    
    private void GenerateSampleMessages()
    {
        var random = new Random();
        var sampleMessages = new[]
        {
            "Hello there!Hello there!Hello there!Hello there!Hello there!Hello there!Hello there!Hello there!Hello there!",
            "How are you?How are you?How are you?How are you?How are you?How are you?How are you?How are you?How are you?How are you?How are you?",
            "Nice to meet you!Nice to meet you!Nice to meet you!Nice to meet you!Nice to meet you!Nice to meet you!",
            "What's new?What's new?What's new?What's new?What's new?What's new?What's new?What's new?What's new?What's new?",
            "Have a great day!Have a great day!Have a great day!Have a great day!Have a great day!Have a great day!Have a great day!",
            "That's interesting!That's interesting!That's interesting!That's interesting!That's interesting!That's interesting!That's interesting!",
            "Tell me more!Tell me more!Tell me more!Tell me more!Tell me more!Tell me more!Tell me more!Tell me more!",
            "I agree with that.I agree with that.I agree with that.I agree with that.I agree with that.I agree with that.",
            "Could you explain?Could you explain?Could you explain?Could you explain?Could you explain?Could you explain?Could you explain?",
            "Makes sense!Makes sense!Makes sense!Makes sense!Makes sense!Makes sense!Makes sense!Makes sense!Makes sense!"
        };

        var baseTime = DateTime.Now.AddHours(-24);
        
        for (int i = 0; i < 50000; i++)
        {
            ChatItems.Add(new ChatMessage
            {
                Message = sampleMessages[random.Next(sampleMessages.Length)],
                IsFromUser = random.Next(2) == 0,
                Timestamp = baseTime.AddMinutes(i * 2)
            });
        }
    }

    public string StatusText { get; set; } = "Ready";
    public bool ShowContent { get; set; }
    public ReactiveCommand<Unit, Unit> SendRequestCommand { get; }
}