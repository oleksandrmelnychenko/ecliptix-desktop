using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Account;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;
using EUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Features.Settings.ViewModels;

public sealed partial class SettingsViewModel : Core.MVVM.ViewModelBase, IActivatableViewModel
{
    private readonly ILogoutService _logoutService;
    private readonly IApplicationSecureStorageProvider _secureStorage;
    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _logoutCancellationTokenSource;
    private bool _isDisposed;


    [Reactive] public string Title { get; set; }
    [Reactive] public string DisplayName { get; set; }
    [Reactive] public string ProfileName { get; set; }
    [Reactive] public string AccountId { get; set; }
    [Reactive] public string ProfileId { get; set; }
    [Reactive] public string ProfileInitials { get; set; }

    [ObservableAsProperty] public bool IsBusy { get; }
    [Reactive] public bool IsLoadingProfile { get; set; }

    public ViewModelActivator Activator { get; } = new();

    public ReactiveCommand<SystemU, SystemU> SaveChangesCommand { get; }
    public ReactiveCommand<SystemU, Result<EUnit, LogoutFailure>> LogoutCommand { get; }

    public SettingsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        ILogoutService logoutService,
        IApplicationSecureStorageProvider secureStorageProvider)
        : base(networkProvider, localizationService, null)
    {
        _logoutService = logoutService;
        _secureStorage = secureStorageProvider;

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.DisplayName)
                .Select(GetInitials)
                .Subscribe(initials => ProfileInitials = initials)
                .DisposeWith(_disposables);

            LoadUserProfileAsync(CancellationToken.None)
                .ConfigureAwait(false);
            Disposable.Create(() => { /* Cleanup if needed */ })
                .DisposeWith(disposables);
        });

        SaveChangesCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            // TODO: Implement save logic
            await Task.Delay(500);
            Log.Information("Settings saved: {DisplayName}", DisplayName);
        });

        IObservable<bool> canLogout = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy);

        LogoutCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                CancelLogoutOperation();

                CancellationTokenSource operationCts = new();
                _logoutCancellationTokenSource = operationCts;

                try
                {
                    Result<Ecliptix.Utilities.Unit, LogoutFailure> result = await _logoutService.LogoutAsync(
                        LogoutReason.USER_INITIATED,
                        operationCts.Token).ConfigureAwait(false);
                    return result;
                }
                catch (TimeoutException ex)
                {
                    return Result<Ecliptix.Utilities.Unit, LogoutFailure>.Err(
                        LogoutFailure.NetworkRequestFailed("Logout timed out - secrecy channel not restored.", ex));
                }
                catch (OperationCanceledException ex)
                {
                    return Result<Ecliptix.Utilities.Unit, LogoutFailure>.Err(
                        LogoutFailure.NetworkRequestFailed("Logout cancelled.", ex));
                }
                catch (Exception ex)
                {
                    return Result<Ecliptix.Utilities.Unit, LogoutFailure>.Err(
                        LogoutFailure.NetworkRequestFailed("Logout failed due to an unexpected error.", ex));
                }
                finally
                {
                    if (ReferenceEquals(_logoutCancellationTokenSource, operationCts))
                    {
                        _logoutCancellationTokenSource = null;
                    }

                    operationCts.Dispose();
                }
            },
            canLogout);

        LogoutCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy).DisposeWith(_disposables);

        LogoutCommand
            .Where(result => result.IsErr)
            .Select(result => result.UnwrapErr())
            .Subscribe(error =>
            {
                Log.Error("[SETTINGS-VM] Logout failed: {Message}", error.Message);
            })
            .DisposeWith(_disposables);

        _disposables.Add(LogoutCommand);
    }

    private async Task LoadUserProfileAsync(CancellationToken cancellationToken)
    {
        IsLoadingProfile = true;
        try
        {
            Option<Guid> accountIdOpt = await GetCurrentAccountIdAsync();

            if (!accountIdOpt.IsSome)
            {
                Log.Warning("[SETTINGS-VM] Cannot load profile: No active account session.");
                return;
            }

            Guid currentAccountId = accountIdOpt.Value;

            GetAccountProfileByIdRequest request = new()
            {
                AccountId = Helpers.GuidToByteString(currentAccountId)
            };

            TaskCompletionSource<GetAccountProfileByIdResponse> responseSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

            Result<EUnit, NetworkFailure> networkResult = await NetworkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.GetAccountProfileById,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
                payload =>
                {
                    GetAccountProfileByIdResponse response = Helpers.ParseFromBytes<GetAccountProfileByIdResponse>(payload);
                    responseSource.TrySetResult(response);
                    return Task.FromResult(Result<EUnit, NetworkFailure>.Ok(EUnit.Value));
                },
                allowDuplicates: true,
                token: cancellationToken
            ).ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                Log.Error("[SETTINGS-VM] Failed to load profile: {Error}", networkResult.UnwrapErr().Message);
                return;
            }

            GetAccountProfileByIdResponse response = await responseSource.Task.ConfigureAwait(false);

            if (response.Profile != null)
            {
                Guid accId = Helpers.FromByteStringToGuid(response.Profile.AccountId);
                Guid profId = Helpers.FromByteStringToGuid(response.Profile.ProfileId);

                DisplayName = response.Profile.DisplayName;
                ProfileName = response.Profile.ProfileName;
                AccountId = accId.ToString();
                ProfileId = profId.ToString();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[SETTINGS-VM] Unexpected error loading profile");
        }
        finally
        {
            IsLoadingProfile = false;
        }
    }

    private async Task<Option<Guid>> GetCurrentAccountIdAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await _secureStorage.GetApplicationInstanceSettingsAsync();

        if (settingsResult.IsOk)
        {
            Guid accountId = Helpers.FromByteStringToGuid(settingsResult.Unwrap().CurrentAccountId);
            return Option<Guid>.Some(accountId);
        }

        Log.Warning("[CHAT-VM] Cannot load the account id from secure storage: {Error}",
            settingsResult.UnwrapErr().Message);
        return Option<Guid>.None;
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            CancelLogoutOperation();
            LogoutCommand.Dispose();
            SaveChangesCommand.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }

    private void CancelLogoutOperation()
    {
        CancellationTokenSource? logoutSource = Interlocked.Exchange(ref _logoutCancellationTokenSource, null);
        if (logoutSource == null)
        {
            return;
        }

        try
        {
            logoutSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Intentionally suppressed
        }
        finally
        {
            logoutSource.Dispose();
        }
    }

    private static string GetInitials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return "?";
        }

        string[] parts = displayName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return "?";
        }

        if (parts.Length == 1)
        {
            return parts[0].Length >= 2
                ? parts[0].Substring(0, 2).ToUpper()
                : parts[0].ToUpper();
        }

        string initials = $"{parts[0][0]}{parts[^1][0]}";
        return initials.ToUpper();
    }
}
