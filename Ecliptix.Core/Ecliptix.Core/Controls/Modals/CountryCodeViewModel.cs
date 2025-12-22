using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
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


public record CountryCodeSelectedEvent(CountryPhoneModel SelectedCountry);

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
    private readonly ISideSheetService? _sideSheetService;
    private readonly IMessageBus? _messageBus;
    private bool _isDisposed;

    public ViewModelActivator Activator { get; } = new();
    public ObservableCollection<CountryPickerItemViewModel> Countries { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<CountryPickerItemViewModel, Unit> SelectCountryCommand { get; }

    public CountryCodeViewModel(IMessageBus messageBus, string currentIsoCode = "US")
    {
        _sideSheetService = Locator.Current.GetService<ISideSheetService>();
        _messageBus = messageBus;

        CountryPhoneModel[] supportedCountries = new[]
        {
            new CountryPhoneModel("United States", "US", "+1", AppCultureSettingsConstants.UNITED_STATES_FLAG_PATH),
            new CountryPhoneModel("Ukraine", "UA", "+380", AppCultureSettingsConstants.UKRAINE_FLAG_PATH),
        };

        Countries = new ObservableCollection<CountryPickerItemViewModel>(
            supportedCountries.Select(c => new CountryPickerItemViewModel(c, c.IsoCode == currentIsoCode))
        );

        CloseCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            if (_sideSheetService != null)
            {
                await _sideSheetService.HideAsync();
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
            await _messageBus.PublishAsync(new CountryCodeSelectedEvent(selectedItem.Model));
        }

        if (_sideSheetService != null)
        {
            await _sideSheetService.HideAsync();
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
