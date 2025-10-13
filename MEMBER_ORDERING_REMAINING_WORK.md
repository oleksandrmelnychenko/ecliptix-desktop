# Member Ordering - Remaining Work

## Quick Reference for Completing the Audit

This document provides a prioritized checklist for completing the member ordering audit of Ecliptix.Core.

---

## ✅ Completed (4 files)

1. `/Core/Modularity/ModuleBase.cs` - Core base class for all modules
2. `/Core/MVVM/ViewModelBase.cs` - Core base class for all ViewModels
3. `/Core/Communication/ModuleMessageBus.cs` - Core messaging infrastructure
4. `/Core/Modularity/ModuleScope.cs` - Module scoping infrastructure

---

## 🔴 High Priority (7 files) - Start Here

These files are frequently modified and serve as examples for other developers:

### Large Avalonia Controls
- [ ] `/Controls/Core/HintedTextBox.axaml.cs` (1343 lines)
  - Most complex file, needs 2-3 hours
  - Pattern: StyledProperty → RoutedEvent → Events → Instance Fields → Constructor → Properties → Methods

### Critical ViewModels
- [ ] `/Features/Authentication/ViewModels/Hosts/MembershipHostWindowModel.cs`
- [ ] `/Features/Authentication/ViewModels/Registration/SecureKeyVerifierViewModel.cs`
- [ ] `/Features/Authentication/ViewModels/SignIn/SignInViewModel.cs`
- [ ] `/Features/Authentication/ViewModels/Registration/VerifyOtpViewModel.cs`
- [ ] `/Features/Authentication/ViewModels/PasswordRecovery/ForgotPasswordResetViewModel.cs`
- [ ] `/Features/Authentication/ViewModels/Registration/MobileVerificationViewModel.cs`

**Pattern for ViewModels:**
```csharp
// 1. Static fields (if any)
private static readonly ...

// 2. Instance fields (private)
private readonly IService _service;
private bool _field;

// 3. Constructor
public ViewModel(...)
{
}

// 4. Properties (public first, then private)
public string Property { get; set; }
private string PrivateProperty { get; set; }

// 5. Methods (public first, then private)
public void PublicMethod() { }
private void PrivateMethod() { }
```

---

## 🟡 Medium Priority (13 files)

### Infrastructure
- [ ] `/Infrastructure/Network/Core/Providers/NetworkProvider.cs` (26 fields, 22 properties)
- [ ] `/Infrastructure/Network/Transport/Grpc/Interceptors/GrpcMetadataHandler.cs`
- [ ] `/Infrastructure/Security/Platform/CrossPlatformSecurityProvider.cs`
- [ ] `/Infrastructure/Security/Storage/SecureProtocolStateStorage.cs`
- [ ] `/Infrastructure/Network/Core/Connectivity/InternetConnectivityObserver.cs`
- [ ] `/Infrastructure/Data/SecureStorage/ApplicationSecureStorageProvider.cs`

### Services
- [ ] `/Services/Authentication/OpaqueAuthenticationService.cs`
- [ ] `/Services/Network/Resilience/SecrecyChannelRetryStrategy.cs`
- [ ] `/Services/Membership/SecureKeyValidator.cs`
- [ ] `/Services/Membership/MobileNumberValidator.cs`
- [ ] `/Services/Network/Resilience/ImprovedRetryConfiguration.cs`
- [ ] `/Services/Network/Resilience/RetryPolicyHelpers.cs`
- [ ] `/Services/Core/ApplicationRouter.cs`

---

## 🟢 Low Priority (41 files)

### Constant Files (Simple, Low Risk)
- [ ] `/Constants/ApplicationErrorMessages.cs`
- [ ] `/Controls/Constants/HintedTextBoxConstants.cs`
- [ ] `/Controls/Constants/NetworkStatusConstants.cs`
- [ ] `/Controls/Constants/SegmentedTextBoxConstants.cs`
- [ ] `/Services/Authentication/Constants/AuthenticationConstants.cs`
- [ ] `/Services/Membership/Constants/MobileNumberValidatorConstants.cs`
- [ ] `/Services/Membership/Constants/SecureKeyValidatorConstants.cs`
- [ ] `/Settings/Constants/AppCultureSettingsConstants.cs`

**Pattern for Constants:**
```csharp
// 1. Const fields (grouped logically)
public const string Constant1 = "value";
public const string Constant2 = "value";

// 2. Static readonly fields (if any)
public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
```

### Control Files
- [ ] `/Controls/Core/NetworkStatusNotification.axaml.cs`
- [ ] `/Controls/Core/SegmentedTextBox.axaml.cs`
- [ ] `/Controls/LanguageSelector/LanguageSelectorViewModel.cs`
- [ ] `/Controls/Modals/BottomSheetModal/BottomSheetControl.axaml.cs`
- [ ] `/Controls/Modals/BottomSheetModal/BottomSheetViewModel.cs`
- [ ] `/Controls/Modals/DetectLanguageDialogViewModel.cs`
- [ ] `/Controls/Modals/RedirectNotificationViewModel.cs`
- [ ] `/Controls/Modals/UserRequestErrorViewModel.cs`

### Event & Message Files
- [ ] `/Core/Messaging/Events/BottomSheetEvents.cs`
- [ ] `/Core/Messaging/Events/LanguageDetectionEvents.cs`
- [ ] `/Core/Messaging/Events/NetworkEvents.cs`
- [ ] `/Core/Messaging/Events/SystemEvents.cs`
- [ ] `/Core/Messaging/MessageTypes.cs`
- [ ] `/Core/Messaging/Subscriptions/SubscriptionTypes.cs`

### Additional Service & Model Files
- [ ] `/Services/Network/Rpc/RpcFlow.cs`
- [ ] `/Services/Network/Rpc/ServiceRequest.cs`
- [ ] `/Services/Network/Rpc/SecrecyKeyExchangeServiceRequest.cs`
- [ ] `/Services/Common/InternalServiceApiFailure.cs`
- [ ] `/Services/Core/ApplicationInitializer.cs`
- [ ] `/Services/Core/ApplicationStateManager.cs`
- [ ] `/Services/External/IpGeolocation/IpGeolocationService.cs`
- [ ] `/Settings/AppCultureSettings.cs`
- [ ] `/VersionHelper.cs`

### Remaining ViewModels
- [ ] `/Features/Main/ViewModels/MainViewModel.cs`
- [ ] `/Features/Authentication/ViewModels/Welcome/WelcomeViewModel.cs`
- [ ] `/Features/Authentication/ViewModels/Registration/PassPhaseViewModel.cs`
- [ ] `/ViewModels/Core/MainWindowViewModel.cs`

### View Files
- [ ] `/Features/Splash/Views/SplashWindow.axaml.cs`
- [ ] `/Views/Memberships/Components/TitleBar.axaml.cs`

---

## Automated Tooling Options

### Option 1: ReSharper/Rider Code Cleanup
1. Open Rider
2. Right-click on project → "Cleanup Code"
3. Select "Reorder Members" profile
4. Review changes before committing

### Option 2: dotnet-format with EditorConfig
```bash
# Install dotnet-format
dotnet tool install -g dotnet-format

# Run with editorconfig rules
dotnet format /Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop/Ecliptix.Core/Ecliptix.Core/Ecliptix.Core.csproj
```

### Option 3: Manual with VS Code + C# Extension
1. Install "C# Dev Kit" extension
2. Use "Format Document" (Shift+Alt+F)
3. Ensure .editorconfig has member ordering rules

---

## Testing After Changes

After reordering any file, verify:

```bash
# Ensure solution still builds
cd /Users/oleksandrmelnychenko/RiderProjects/ecliptix-desktop
dotnet build Ecliptix.Core/Ecliptix.Core/Ecliptix.Core.csproj

# Run tests if available
dotnet test Ecliptix.Core/Ecliptix.Core.Tests/ 2>/dev/null || echo "No tests found"

# Check for compilation errors
dotnet build --no-incremental
```

---

## Progress Tracking

Update this checklist as you complete files. Estimated total effort: **25-30 hours**

- ✅ Completed: 4 files (Core infrastructure)
- 🔴 High Priority: 0/7 completed
- 🟡 Medium Priority: 0/13 completed
- 🟢 Low Priority: 0/41 completed

**Total**: 4/65 files completed (6%)

---

## Questions or Issues?

Refer to:
- **Full Report**: `MEMBER_ORDERING_AUDIT_REPORT.md`
- **Microsoft Guidelines**: [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- **CLAUDE.md**: Project-specific coding standards
- **Analysis Data**: `member_order_analysis.txt`
