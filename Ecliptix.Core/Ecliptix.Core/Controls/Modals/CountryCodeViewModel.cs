using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Core.Localization;
using Ecliptix.Core.Settings.Constants;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using IMessageBus = Ecliptix.Core.Core.Messaging.IMessageBus;

namespace Ecliptix.Core.Controls.Modals;

public record CountryPhoneModel(
    string DisplayName,
    string IsoCode,
    string PhonePrefix,
    string FlagImagePath
)
{
    public string FormattedCode => $"{IsoCode} {PhonePrefix}";
}


public record CountryCodeSelectedEvent(
    CountryPhoneModel SelectedCountry,
    string RequestorContext);

public class CountryPickerItemViewModel : ReactiveObject
{
    public CountryPhoneModel Model { get; }

    [Reactive]
    public bool IsSelected { get; set; }

    public CountryPickerItemViewModel(CountryPhoneModel model, bool isSelected)
    {
        Model = model;
        IsSelected = isSelected;
    }
}

public class CountryCodeViewModel : ReactiveObject, IActivatableViewModel, IDisposable
{
    private readonly IGlobalModalService? _globalModalService;
    private readonly ILocalizationService _localizationService;
    private readonly IMessageBus? _messageBus;
    private bool _isDisposed;
    private readonly string _requestorContext;

    public ViewModelActivator Activator { get; } = new();
    public ObservableCollection<CountryPickerItemViewModel> Countries { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<CountryPickerItemViewModel, Unit> SelectCountryCommand { get; }

    public string Title => _localizationService[LocalizationKeys.CountryPicker.TITLE];
    public string Subtitle => _localizationService[LocalizationKeys.CountryPicker.SUBTITLE];

    public CountryCodeViewModel(IMessageBus? messageBus, ILocalizationService localizationService, IGlobalModalService globalModalService, string currentIsoCode = "US", string requestorContext = "None")
    {
        _localizationService = localizationService;
        _globalModalService = globalModalService;
        _messageBus = messageBus;
        _requestorContext = requestorContext;

        CountryPhoneModel[] supportedCountries = new[]
        {
            new CountryPhoneModel(
                _localizationService[LocalizationKeys.Countires.US],
                AppCultureSettingsConstants.UNITED_STATES_COUNTRY_CODE,
                AppCultureSettingsConstants.UNITED_STATES_PHONE_PREFIX,
                AppCultureSettingsConstants.UNITED_STATES_FLAG_PATH),
            new CountryPhoneModel(
                _localizationService[LocalizationKeys.Countires.UA],
                AppCultureSettingsConstants.UKRAINE_COUNTRY_CODE,
                AppCultureSettingsConstants.UKRAINE_PHONE_PREFIX,
                AppCultureSettingsConstants.UKRAINE_FLAG_PATH),
        };

        Countries = new ObservableCollection<CountryPickerItemViewModel>(
            supportedCountries.Select(c => new CountryPickerItemViewModel(c, c.IsoCode == currentIsoCode))
        );

        CloseCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_globalModalService != null)
            {
                await _globalModalService.CloseAllAsync();
            }
        });

        SelectCountryCommand = ReactiveCommand.CreateFromTask<CountryPickerItemViewModel>(async (selectedItem) =>
        {
            await SelectCountryAsync(selectedItem);
        });
    }

    private async Task SelectCountryAsync(CountryPickerItemViewModel? selectedItem)
    {
        if (selectedItem?.Model == null)
        {
            return;
        }

        if (_messageBus != null)
        {
            await _messageBus.PublishAsync(new CountryCodeSelectedEvent(selectedItem.Model, _requestorContext));
        }

        if (_globalModalService != null)
        {
            await _globalModalService.CloseAllAsync();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        CloseCommand.Dispose();
        SelectCountryCommand.Dispose();
    }
}
