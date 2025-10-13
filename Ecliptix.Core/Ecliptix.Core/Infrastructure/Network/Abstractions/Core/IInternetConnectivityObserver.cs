using System;

namespace Ecliptix.Core.Infrastructure.Network.Abstractions.Core;

public interface IInternetConnectivityObserver : IObservable<bool>, IDisposable;
