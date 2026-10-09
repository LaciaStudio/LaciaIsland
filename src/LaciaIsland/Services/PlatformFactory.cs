using LaciaIsland.UI;

namespace LaciaIsland.Services;

public static class PlatformFactory
{
    public static ServiceHub CreateServices()
    {
#if WINDOWS
        return Platform.Windows.WinServices.Create();
#else
        return DemoHub.Create();
#endif
    }

    /// <summary>Windows: own per-pixel-alpha layered window. Elsewhere (or with --avalonia-window): Avalonia window.</summary>
    public static IIslandHost CreateHost(IslandController controller, bool forceAvaloniaWindow)
    {
#if WINDOWS
        if (!forceAvaloniaWindow)
        {
            try { return new Platform.Windows.LayeredIslandHost(controller); }
            catch (Exception ex) { Log.Error("layered host", ex); }
        }
#endif
        return new IslandWindow(controller);
    }
}
