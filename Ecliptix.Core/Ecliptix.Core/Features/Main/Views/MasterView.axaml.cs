using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Main.ViewModels;
using Serilog;
using Splat;

namespace Ecliptix.Core.Features.Main.Views;

public partial class MasterView : UserControl
{

    public MasterView()
    {
        AvaloniaXamlLoader.Load(this);

    }
}
