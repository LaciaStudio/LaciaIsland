using Avalonia.Platform;

namespace LaciaIsland.UI;

/// <summary>
/// Monitor numbering shared by the settings list and the island hosts: primary first, then left to right
/// (the Win32 host applies the same rule to its own EnumDisplayMonitors list).
/// </summary>
public static class ScreenOrder
{
    public static List<Screen> Sort(IEnumerable<Screen> screens) =>
        screens.OrderByDescending(s => s.IsPrimary)
            .ThenBy(s => s.Bounds.X)
            .ThenBy(s => s.Bounds.Y)
            .ToList();
}
