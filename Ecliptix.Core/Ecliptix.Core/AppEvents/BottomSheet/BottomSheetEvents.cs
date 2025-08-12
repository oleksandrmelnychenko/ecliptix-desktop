using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Avalonia.Controls;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Services;
using Serilog;

namespace Ecliptix.Core.AppEvents.BottomSheet;

public record BottomSheetChangedEvent
{
    public UserControl? Control { get; init; }
    public BottomSheetComponentType ComponentType { get; init; }

    public bool ShowScrim { get; init; }

    private BottomSheetChangedEvent(BottomSheetComponentType componentType, bool showScrim, UserControl? userControl)
    {
        ShowScrim = showScrim;
        ComponentType = componentType;
        Control = userControl;
    }

    public static BottomSheetChangedEvent
        New(BottomSheetComponentType componentType, bool showScrim = true, UserControl? userControl = null) {
        return new(componentType, showScrim, userControl);
    }
}

public sealed class BottomSheetEvents(IEventAggregator aggregator, ILocalizationService localizationService)
    : IBottomSheetEvents
{
    private readonly Func<UserControl> _languageDetectionModalFactory = () => new LanguageDetectionModal(localizationService);

    public IObservable<BottomSheetChangedEvent> BottomSheetChanged { get; } =
        aggregator.GetEvent<BottomSheetChangedEvent>();

    public void BottomSheetChangedState(BottomSheetChangedEvent message)
    {
        Log.Information($"BottomSheetChangedState called with ComponentType={message.ComponentType}, ShowScrim={message.ShowScrim}, Control={message.Control}");
        
        UserControl? userControl = message.Control ?? GetBottomSheetControl(message.ComponentType);
        
        BottomSheetChangedEvent updatedMessage =
            BottomSheetChangedEvent.New(message.ComponentType, message.ShowScrim, userControl);

        aggregator.Publish(updatedMessage);
    }

    private UserControl? GetBottomSheetControl(BottomSheetComponentType componentType)
    {
        return componentType switch
        {
            BottomSheetComponentType.DetectedLocalization => _languageDetectionModalFactory(),
            _ => null
        };
    }
}