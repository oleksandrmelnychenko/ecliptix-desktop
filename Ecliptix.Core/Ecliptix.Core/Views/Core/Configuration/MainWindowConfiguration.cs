namespace Ecliptix.Core.Views.Core.Configuration;

public sealed class MainWindowConfiguration
{
    public string DefaultUserName { get; set; } = string.Empty;
    public string DefaultUserTag { get; set; } = string.Empty;
    public bool EnableAnimations { get; set; } = true;
    public bool SaveWindowPlacement { get; set; } = true;
}
