using System.Drawing;

namespace DisplayModeSwitcher;

internal static class ApplicationIconProvider
{
    internal static Icon Create() =>
        Icon.ExtractAssociatedIcon(Application.ExecutablePath)
        ?? (Icon)SystemIcons.Application.Clone();
}
