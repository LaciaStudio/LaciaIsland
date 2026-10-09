using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;

namespace LaciaIsland.UI;

/// <summary>
/// Avalonia host control for the island (used on Linux, in the headless renderer and as a fallback).
/// It just calls the controller's immediate-mode painter and forwards pointer input.
/// </summary>
public sealed class IslandView : Control, ICustomHitTest
{
    public Action<DrawingContext>? Painter;

    public event Action<Point>? PointerMovedAt;
    public event Action? PointerLeft;
    public event Action<Point, MouseButton>? Pressed;
    public event Action<Point, MouseButton>? Released;
    public event Action<double, bool>? Wheel;

    public IslandView()
    {
        ClipToBounds = false;
    }

    public bool HitTest(Point point) => true;

    public override void Render(DrawingContext context) => Painter?.Invoke(context);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        PointerMovedAt?.Invoke(e.GetPosition(this));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        PointerLeft?.Invoke();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        var button = props.IsRightButtonPressed ? MouseButton.Right : props.IsMiddleButtonPressed ? MouseButton.Middle : MouseButton.Left;
        e.Pointer.Capture(this);
        Pressed?.Invoke(e.GetPosition(this), button);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        Released?.Invoke(e.GetPosition(this), e.InitialPressMouseButton);
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var d = Math.Abs(e.Delta.Y) >= Math.Abs(e.Delta.X) ? e.Delta.Y : -e.Delta.X;
        Wheel?.Invoke(d, e.KeyModifiers.HasFlag(KeyModifiers.Control));
        e.Handled = true;
    }
}
