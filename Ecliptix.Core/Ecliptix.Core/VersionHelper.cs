using System;
using System.Reflection;

namespace Ecliptix.Core;

public static class VersionHelper
{
    public static string GetApplicationVersion()
    {
        Version? version = Assembly.GetExecutingAssembly().GetName().Version
                           ?? Assembly.GetEntryAssembly()?.GetName().Version
                           ?? new Version(0, 0, 0, 0);
        return version.ToString(); 
    }
}