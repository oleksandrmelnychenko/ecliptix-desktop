# Phosphor Icons (Light Weight)

This directory contains 221 curated Phosphor Icons in light weight style - only UI/UX relevant icons.

## Source
- **Library**: Phosphor Icons
- **Style**: Light (thin, minimal lines)
- **License**: MIT
- **Repository**: https://github.com/phosphor-icons/core

## Usage

### In AXAML Files

```xml
<!-- Method 1: Using helper class (Recommended) -->
<UserControl xmlns:utilities="using:Ecliptix.Core.Utilities">
  <Image Source="{x:Static utilities:PhosphorIcons+Common.User}" Width="24" Height="24" />
</UserControl>

<!-- Method 2: Direct path -->
<Image Source="avares://Ecliptix.Core/Assets/Icons/user-light.svg" Width="24" Height="24" />
```

### In Code

```csharp
using Ecliptix.Core.Utilities;

// Get icon path
string iconPath = PhosphorIcons.Common.User;
// or
string iconPath = PhosphorIcons.GetIconPath("user");
```

## Icon Naming Convention

All icons follow the pattern: `{icon-name}-light.svg`

Examples:
- `user-light.svg`
- `home-light.svg`
- `settings-light.svg`
- `chat-light.svg`

## Finding Icons

Browse all available icons in this directory or use the helper class categories:
- Common
- Navigation
- Communication
- Files
- Media
- Security
- Editor
- Actions
- Status
- System
- Social
- Time

See `PhosphorIcons.cs` for the complete list of organized icons.

## Adding New Icons to Helper

If you frequently use an icon, add it to `PhosphorIcons.cs`:

```csharp
public static class MyCategory
{
    public static string MyIcon => GetIconPath("my-icon");
}
```
