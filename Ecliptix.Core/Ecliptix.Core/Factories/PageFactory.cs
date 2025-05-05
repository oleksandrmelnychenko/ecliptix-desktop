using System;
using System.Collections.Generic;
using Ecliptix.Core.Data;
using Ecliptix.Core.ViewModels;

namespace Ecliptix.Core.Factories;

public class PageFactory
{
    private readonly Dictionary<ApplicationPageNames, Func<PageViewModel>> _pageFactories;

    public PageFactory(Dictionary<ApplicationPageNames, Func<PageViewModel>> pageFactories)
    {
        _pageFactories = pageFactories;
    }

    public PageViewModel GetPageViewModel(ApplicationPageNames pageName)
    {
        if (_pageFactories.TryGetValue(pageName, out var factory))
        {
            return factory();
        }

        throw new InvalidOperationException($"Unknown page name: {pageName}");
    }
}