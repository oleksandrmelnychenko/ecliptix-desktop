using System;

namespace Ecliptix.Core.Services.Abstractions.Core;

public interface IWindowActivationService
{
    void ActivateMainWindow();

    event EventHandler? WindowActivationRequested;

    void RequestActivation();
}