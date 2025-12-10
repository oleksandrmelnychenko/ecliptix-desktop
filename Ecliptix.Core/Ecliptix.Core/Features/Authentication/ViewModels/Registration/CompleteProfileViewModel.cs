using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public sealed class CompleteProfileViewModel : ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly CompositeDisposable _disposables = new();

    private bool _isDisposed;

    private readonly Subject<string> _executionErrorSubject = new();
    public IObservable<string> ExecutionError => _executionErrorSubject.AsObservable();

    public CompleteProfileViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider)
        : base(networkProvider, localizationService, connectivityService)
    {
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;

        IObservable<bool> isFormValid = SetupValidation();

        SetupCommands(isFormValid);
    }

    public string? UrlPathSegment { get; } = "/complete-profile";
    public IScreen HostScreen { get; }

    [Reactive] public string ProfileName { get; set; } = string.Empty;
    [Reactive] public string DisplayName { get; set; } = string.Empty;
    [Reactive] public string DateOfBirth { get; set; } = string.Empty;

    [Reactive] public string ProfileNameError { get; private set; } = string.Empty;
    [Reactive] public bool HasProfileNameError { get; private set; }

    [Reactive] public string DisplayNameError { get; private set; } = string.Empty;
    [Reactive] public bool HasDisplayNameError { get; private set; }

    [ObservableAsProperty] public bool IsBusy { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> CompleteSetupCommand { get; private set; } = null!;

    public void ResetState()
    {
        ProfileName = string.Empty;
        DisplayName = string.Empty;
        DateOfBirth = string.Empty;
        ProfileNameError = string.Empty;
        HasProfileNameError = false;
        DisplayNameError = string.Empty;
        HasDisplayNameError = false;
        _executionErrorSubject.OnNext(string.Empty);
    }

    private IObservable<bool> SetupValidation()
    {
        this.WhenAnyValue(x => x.ProfileName)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(name =>
            {
                bool isValid = !string.IsNullOrWhiteSpace(name) && name.Length >= 3;
                ProfileNameError = isValid ? string.Empty : LocalizationService["Authentication.Error.InvalidName"];
                HasProfileNameError = !isValid;
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.DisplayName)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(name =>
            {
                bool isValid = !string.IsNullOrWhiteSpace(name) && name.StartsWith("@");
                DisplayNameError = isValid ? string.Empty : LocalizationService["Authentication.Error.InvalidDisplayName"];
                HasDisplayNameError = !isValid;
            })
            .DisposeWith(_disposables);

        return this.WhenAnyValue(
            x => x.HasProfileNameError,
            x => x.HasDisplayNameError,
            x => x.ProfileName,
            x => x.DisplayName,
            (nameErr, dispErr, name, disp) =>
                !nameErr && !dispErr && !string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(disp)
        );
    }

    private void SetupCommands(IObservable<bool> isFormValid)
    {
        IObservable<bool> canExecute = isFormValid.CombineLatest(
            this.WhenAnyValue(x => x.IsBusy),
            (valid, busy) => valid && !busy
        );

        CompleteSetupCommand = ReactiveCommand.CreateFromTask(ExecuteCompletionAsync, canExecute);

        CompleteSetupCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        CompleteSetupCommand.ThrownExceptions
             .Subscribe(ex => _executionErrorSubject.OnNext(ex.Message))
             .DisposeWith(_disposables);
    }

    private async Task ExecuteCompletionAsync()
    {
        try
        {
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error completing profile");
            _executionErrorSubject.OnNext(LocalizationService["Common.Error.Unexpected"]);
        }
    }

    public new void Dispose() => Dispose(true);

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            _disposables.Dispose();
            _executionErrorSubject.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }
}
