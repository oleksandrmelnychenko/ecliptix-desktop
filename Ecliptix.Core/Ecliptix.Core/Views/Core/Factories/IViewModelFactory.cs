using System;

namespace Ecliptix.Core.Views.Core.Factories;

public interface IViewModelFactory
{
    T Create<T>() where T : class;
    T Create<T>(params object[] parameters) where T : class;
    void Track(IDisposable viewModel);
    void DisposeAll();
}
