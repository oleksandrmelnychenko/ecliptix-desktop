using System;
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

public class BottomSheetEvents(IEventAggregator aggregator, ILocalizationService localizationService)
    : IBottomSheetEvents
{
    private readonly IReadOnlyDictionary<BottomSheetComponentType, Func<UserControl>> _bottomSheetComponents =
        new Dictionary<BottomSheetComponentType, Func<UserControl>>
        {
            { BottomSheetComponentType.DetectedLocalization, () => new LanguageDetectionModal(localizationService) }
        }.AsReadOnly();

    public IObservable<BottomSheetChangedEvent> BottomSheetChanged { get; } =
        aggregator.GetEvent<BottomSheetChangedEvent>();

    public void BottomSheetChangedState(BottomSheetChangedEvent message)
    {
        Log.Information($"BottomSheetChangedState called with ComponentType={message.ComponentType}, ShowScrim={message.ShowScrim}");
        UserControl? userControl = GetBottomSheetControl(message.ComponentType);
        BottomSheetChangedEvent updatedMessage =
            BottomSheetChangedEvent.New(message.ComponentType, message.ShowScrim, userControl);

        aggregator.Publish(updatedMessage);
    }

    private UserControl? GetBottomSheetControl(BottomSheetComponentType componentType) =>
        _bottomSheetComponents.TryGetValue(componentType, out Func<UserControl>? factory) ? factory() : null;
}