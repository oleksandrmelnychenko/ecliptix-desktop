using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Services;

public interface IApplicationInitializer
{
    Task<bool> InitializeAsync(Action<string>? statusCallback = null);

    bool IsMembershipConfirmed { get; }
}