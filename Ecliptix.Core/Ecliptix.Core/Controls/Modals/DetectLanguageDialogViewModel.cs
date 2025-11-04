using System;
using System.Globalization;
using System.Reactive.Disposables;
using System.Threading.Tasks;

using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Utilities;

using ReactiveUI;

using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Controls.Modals;

public sealed class DetectLanguageDialogViewModel : ReactiveObject, IDisposable, IActivatableViewModel
{
    private readonly ILanguageDetectionService _languageDetectionService;

    public ViewModelActivator Activator { get; } = new();

    private bool _disposed;
    public string Title { get; }
    public string PromptText { get; }
    public string ConfirmButtonText { get; }
    public string DeclineButtonText { get; }
    public string FlagPath { get; }
    private readonly string _targetCulture;

    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> DeclineCommand { get; }

    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;


    public DetectLanguageDialogViewModel(
        ILocalizationService localizationService,
        ILanguageDetectionService languageDetectionService,
        NetworkProvider networkProvider
        )
    {
        _languageDetectionService = languageDetectionService;

        ConfirmCommand = ReactiveCommand.CreateFromTask(OnConfirm);
        DeclineCommand = ReactiveCommand.CreateFromTask(OnDecline);

        string country = networkProvider.ApplicationInstanceSettings.Country;

        if (string.IsNullOrWhiteSpace(country))
        {
            _targetCulture = CultureInfo.CurrentCulture.Name;
        }
        else
        {
            _targetCulture = LanguageConfig.GetCultureByCountry(country) ?? CultureInfo.CurrentCulture.Name;
        }

        CultureInfo targetInfo;
        try
        {
            targetInfo = CultureInfo.GetCultureInfo(_targetCulture);
        }
        catch (CultureNotFoundException)
        {
            targetInfo = CultureInfo.CurrentCulture;
            _targetCulture = targetInfo.Name;
        }

        string languageName = targetInfo.DisplayName;
        int parenthesisIndex = languageName.IndexOf('(');
        if (parenthesisIndex > 0)
        {
            languageName = languageName.Substring(0, parenthesisIndex).Trim();
        }

        Title = localizationService?["LanguageDetection.Title"] ?? "Language Detection";
        PromptText = localizationService?.GetString("LanguageDetection.Prompt",
                LanguageConfig.GetDisplayName(languageName)) ?? $"Switch to {languageName}?";
        ConfirmButtonText = localizationService?["LanguageDetection.Button.Confirm"] ?? "Confirm";
        DeclineButtonText = localizationService?["LanguageDetection.Button.Decline"] ?? "Decline";


        Option<LanguageItem> languageItem = LanguageConfig.GetLanguageByCode(_targetCulture);
        FlagPath = languageItem.Select(item => item.FlagImagePath)
            .GetValueOrDefault("avares://Ecliptix.Core/Assets/Icons/Flags/usa_flag.svg");

        this.WhenActivated(disposables =>
        {
            ConfirmCommand.DisposeWith(disposables);
            DeclineCommand.DisposeWith(disposables);
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }

    private async Task OnConfirm()
    {
        await _languageDetectionService.ConfirmLanguageChangeAsync(targetCulture: _targetCulture);
    }

    private async Task OnDecline()
    {
        await _languageDetectionService.DeclineLanguageChangeAsync();
    }
}
