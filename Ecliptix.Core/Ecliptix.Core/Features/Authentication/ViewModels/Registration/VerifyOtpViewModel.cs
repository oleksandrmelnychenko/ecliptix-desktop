using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using Serilog;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class VerifyOtpViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly ByteString _phoneNumberIdentifier;
    private string _errorMessage = string.Empty;
    private bool _isSent;
    private string _remainingTime = "01:00";
    private string _verificationCode;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    
    private uint? _streamConnectId;

    public string? UrlPathSegment { get; } = "/verification-code-entry";
    public IScreen HostScreen { get; }
    private Guid? VerificationSessionIdentifier { get; set; } = null;

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }

    public ReactiveCommand<Unit, Unit> NavToPasswordConfirmation { get; }

    public new ViewModelActivator Activator { get; } = new();

    private ulong _secondsRemaining;

    public string VerificationCode
    {
        get => _verificationCode;
        set => this.RaiseAndSetIfChanged(ref _verificationCode, value);
    }

    public bool IsSent
    {
        get => _isSent;
        private set => this.RaiseAndSetIfChanged(ref _isSent, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public string RemainingTime
    {
        get => _remainingTime;
        private set
        {
            this.RaiseAndSetIfChanged(ref _remainingTime, value);
        }
    }

    public ulong SecondsRemaining
    {
        get => _secondsRemaining;
        private set
        {
            this.RaiseAndSetIfChanged(ref _secondsRemaining, value);
            RemainingTime = FormatRemainingTime(value);
        }
    }

    public VerifyOtpViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        ByteString phoneNumberIdentifier,
        IApplicationSecureStorageProvider applicationSecureStorageProvider) : base(systemEventService, networkProvider,
        localizationService)
    {
        _phoneNumberIdentifier = phoneNumberIdentifier;
        _verificationCode = string.Empty;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;

        HostScreen = hostScreen;

        NavToPasswordConfirmation = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.ConfirmSecureKey);
        });

        IObservable<bool> canVerify = this.WhenAnyValue(
            x => x.VerificationCode,
            x => x.RemainingTime,
            (code, time) => code?.Length == 6 && code.All(char.IsDigit) && time != "00:00"
        );
        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canVerify);

        IObservable<bool> canResend = this.WhenAnyValue(x => x.SecondsRemaining, seconds => seconds == 0);
        ResendSendVerificationCodeCommand = ReactiveCommand.Create(ReSendVerificationCode, canResend);

        this.WhenActivated(disposables => 
        { 
            OnViewLoaded().Subscribe().DisposeWith(disposables); 
        });
    }

    private IObservable<Unit> OnViewLoaded()
    {
        return Observable.FromAsync(async () =>
        {
            await InitiateVerification(
                _phoneNumberIdentifier,
                InitiateVerificationRequest.Types.Type.SendOtp
            );
        });
    }

    private async Task InitiateVerification(ByteString phoneNumberIdentifier,
        InitiateVerificationRequest.Types.Type type)
    {
        Result<uint, NetworkFailure> protocolResult = 
            await EnsureStreamProtocolAsync(Protobuf.Protocol.PubKeyExchangeType.VerificationFlowStream);
        
        if (protocolResult.IsErr)
        {
            Log.Error("[VERIFICATION] Failed to establish stream protocol: {Error}", 
                protocolResult.UnwrapErr());
            ErrorMessage = "Failed to initialize verification stream";
            return;
        }
        
        uint streamConnectId = protocolResult.Unwrap();
        _streamConnectId = streamConnectId; 
        Log.Information("[VERIFICATION] Using stream connectId {ConnectId} for verification flow", 
            streamConnectId);

        using CancellationTokenSource cancellationTokenSource = new();

        string? systemDeviceIdentifier = SystemDeviceIdentifier();

        InitiateVerificationRequest membershipVerificationRequest = new()
        {
            MobileNumberIdentifier = phoneNumberIdentifier,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(systemDeviceIdentifier)),
            Purpose = VerificationPurpose.Registration,
            Type = type
        };
        _ = await NetworkProvider.ExecuteReceiveStreamRequestAsync(
            streamConnectId,  
            RpcServiceType.InitiateVerification,
            SecureByteStringInterop.WithByteStringAsSpan(membershipVerificationRequest.ToByteString(),
                span => span.ToArray()),
            payload =>
            {
                VerificationCountdownUpdate timerTick =
                    Helpers.ParseFromBytes<VerificationCountdownUpdate>(payload);
                if (timerTick.AlreadyVerified)
                {
                }

                if (timerTick.Status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed)
                {
                    _ = Task.Run(async () => await CleanupStreamProtocolAsync(), cancellationTokenSource.Token);
                }

                if (timerTick.Status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired)
                {
                    _ = Task.Run(async () => await CleanupStreamProtocolAsync(), cancellationTokenSource.Token);
                }

                if (timerTick.Status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached)
                {
                    _ = Task.Run(async () => await CleanupStreamProtocolAsync(), cancellationTokenSource.Token);
                }

                if (timerTick.Status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound)
                {
                    _ = Task.Run(async () => await CleanupStreamProtocolAsync(), cancellationTokenSource.Token);
                }

                VerificationSessionIdentifier ??= Helpers.FromByteStringToGuid(timerTick.SessionIdentifier);
                RxApp.MainThreadScheduler.Schedule(() => SecondsRemaining = timerTick.SecondsRemaining);
                RxApp.MainThreadScheduler.Schedule(() =>
                    RemainingTime = FormatRemainingTime(timerTick.SecondsRemaining));

                return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value));
            }, true,
            cancellationTokenSource.Token
        );
    }

    private async Task SendVerificationCode()
    {
        string? systemDeviceIdentifier = SystemDeviceIdentifier();

        IsSent = true;
        ErrorMessage = string.Empty;

        VerifyCodeRequest verifyCodeRequest = new()
        {
            Code = VerificationCode,
            Purpose = VerificationPurpose.Registration,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(systemDeviceIdentifier))
        };

        _ = await NetworkProvider.ExecuteUnaryRequestAsync(
            ComputeConnectId(),
            RpcServiceType.VerifyOtp,
            SecureByteStringInterop.WithByteStringAsSpan(verifyCodeRequest.ToByteString(),
                span => span.ToArray()),
            async payload =>
            {
                VerifyCodeResponse verifyCodeReply = Helpers.ParseFromBytes<VerifyCodeResponse>(payload);
                if (verifyCodeReply.Result == VerificationResult.Succeeded)
                {
                    Membership membership = verifyCodeReply.Membership;
                    await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);
                    
                    await CleanupStreamProtocolAsync();
                    
                    NavToPasswordConfirmation.Execute().Subscribe();
                }
                else if (verifyCodeReply.Result == VerificationResult.InvalidOtp)
                {
                }

                return Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value);
            }, true,
            CancellationToken.None
        );
    }

    private void ReSendVerificationCode()
    {
        Task.Run(async () => await InitiateVerification(
            _phoneNumberIdentifier,
            InitiateVerificationRequest.Types.Type.ResendOtp));
    }

    private static string FormatRemainingTime(ulong seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string formattedDataTimeString = time.ToString(@"mm\:ss");
        return formattedDataTimeString;
    }

    public void ResetState()
    {
        _errorMessage = string.Empty;
        _isSent = false;
        this.RaisePropertyChanged(nameof(ErrorMessage));
        this.RaisePropertyChanged(nameof(IsSent));
    }

    private async Task CleanupStreamProtocolAsync()
    {
        if (_streamConnectId.HasValue)
        {
            Log.Information("[VERIFICATION] Cleaning up stream protocol with connectId {ConnectId}", _streamConnectId.Value);
            
            Result<ShieldUnit, NetworkFailure> cleanupResult = 
                await NetworkProvider.CleanupStreamProtocolAsync(_streamConnectId.Value);
            
            if (cleanupResult.IsErr)
            {
                Log.Warning("[VERIFICATION] Failed to cleanup stream protocol: {Error}", cleanupResult.UnwrapErr());
            }
            
            _streamConnectId = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _streamConnectId.HasValue)
        {
            _ = Task.Run(async () => await CleanupStreamProtocolAsync());
        }
        
        base.Dispose(disposing);
    }
}