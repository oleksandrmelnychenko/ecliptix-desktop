using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.MVVM;

internal static class StaticViewMapper
{
    private static readonly ConcurrentDictionary<Type, Lazy<Func<Control>>> ViewFactories = new();

    public static void RegisterView<TViewModel>(Func<Control> viewFactory)
    {
        ViewFactories[typeof(TViewModel)] = new Lazy<Func<Control>>(() => viewFactory);
    }

    public static void RegisterView(Type viewModelType, Func<Control> viewFactory)
    {
        ViewFactories[viewModelType] = new Lazy<Func<Control>>(() => viewFactory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Control? CreateView(Type viewModelType)
    {
        if (!ViewFactories.TryGetValue(viewModelType, out Lazy<Func<Control>>? lazyFactory))
        {
            return null;
        }

        Func<Control> factory = lazyFactory.Value;
        Control result = factory();
        return result;
    }
}
