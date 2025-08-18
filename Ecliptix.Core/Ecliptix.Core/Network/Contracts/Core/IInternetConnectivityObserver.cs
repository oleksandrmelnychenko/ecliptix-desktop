using System;

namespace Ecliptix.Core.Network.Contracts.Core;

public interface IInternetConnectivityObserver : IObservable<bool>, IDisposable;