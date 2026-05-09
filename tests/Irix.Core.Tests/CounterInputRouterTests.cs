using Irix;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class CounterInputRouterTests
{
    [Fact]
    public void TryMapInput_maps_left_click_to_button_action()
    {
        var inputEvent = new RawInputEvent(
            RawInputEventKind.PointerReleased,
            Timestamp: 1,
            X: 32,
            Y: 140,
            Button: PointerButton.Left);

        var mapped = CounterInputRouter.TryMapInput(
            inputEvent,
            (x, y) => x == 32 && y == 140 ? nameof(CounterMessage.Increment) : null,
            out var message);

        Assert.True(mapped);
        Assert.IsType<CounterMessage.Increment>(message);
    }

    [Fact]
    public void TryMapInput_does_not_map_left_click_without_hit_target()
    {
        var inputEvent = new RawInputEvent(
            RawInputEventKind.PointerReleased,
            Timestamp: 1,
            X: 500,
            Y: 500,
            Button: PointerButton.Left);

        var mapped = CounterInputRouter.TryMapInput(inputEvent, (_, _) => null, out _);

        Assert.False(mapped);
    }

    [Theory]
    [InlineData(0x26, typeof(CounterMessage.Increment))]
    [InlineData(0x28, typeof(CounterMessage.Decrement))]
    public void TryMapInput_maps_arrow_keys(int keyCode, Type expectedMessageType)
    {
        var inputEvent = new RawInputEvent(
            RawInputEventKind.KeyPressed,
            Timestamp: 1,
            X: 0,
            Y: 0,
            KeyCode: keyCode);

        var mapped = CounterInputRouter.TryMapInput(inputEvent, (_, _) => null, out var message);

        Assert.True(mapped);
        Assert.IsType(expectedMessageType, message);
    }

    [Theory]
    [InlineData(120, -40)]   // scroll up → negative deltaY (scroll content up)
    [InlineData(-120, 40)]   // scroll down → positive deltaY (scroll content down)
    [InlineData(240, -80)]   // two notches up
    [InlineData(0, 0)]       // zero delta → scroll 0
    public void TryMapInput_maps_mouse_wheel_to_scroll(int delta, int expectedDeltaY)
    {
        var inputEvent = new RawInputEvent(
            RawInputEventKind.PointerWheel,
            Timestamp: 1,
            X: 0,
            Y: 0,
            Delta: delta);

        var mapped = CounterInputRouter.TryMapInput(inputEvent, (_, _) => null, out var message);

        if (delta == 0)
        {
            Assert.False(mapped);
            return;
        }

        Assert.True(mapped);
        var scroll = Assert.IsType<CounterMessage.Scroll>(message);
        Assert.Equal(expectedDeltaY, scroll.DeltaY);
    }

    [Theory]
    [InlineData('r')]
    [InlineData('R')]
    public void TryMapInput_maps_reset_shortcut(char character)
    {
        var inputEvent = new RawInputEvent(
            RawInputEventKind.CharacterInput,
            Timestamp: 1,
            X: 0,
            Y: 0,
            Character: character);

        var mapped = CounterInputRouter.TryMapInput(inputEvent, (_, _) => null, out var message);

        var reset = Assert.IsType<CounterMessage.Reset>(message);
        Assert.True(mapped);
        Assert.Equal(0, reset.Value);
    }

    [Fact]
    public void MapActionId_throws_for_unsupported_action_id()
    {
        Assert.Throws<NotSupportedException>(() => CounterInputRouter.MapActionId("Unsupported"));
    }

    [Fact]
    public void Scroll_message_updates_model_scrollY()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll down
        var result = app.Update(model, new CounterMessage.Scroll(40));
        Assert.Equal(40, result.NextModel.ScrollY);

        // Scroll up
        result = app.Update(result.NextModel, new CounterMessage.Scroll(-20));
        Assert.Equal(20, result.NextModel.ScrollY);
    }

    [Fact]
    public void Scroll_negative_scrollY_clamped_to_zero()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll up when already at 0 → stays at 0
        var result = app.Update(model, new CounterMessage.Scroll(-100));
        Assert.Equal(0, result.NextModel.ScrollY);
    }

    [Fact]
    public void ScrollY_appears_in_view_as_attribute()
    {
        var app = new CounterApplication();
        var model = app.Initialize() with { ScrollY = 80 };
        var tree = app.BuildView(model);

        // The root ScrollContainer should have ScrollY=80 attribute
        Assert.Equal(VirtualNodeKind.ScrollContainer, tree.Root.Kind);
        var scrollYAttr = default(VirtualNodeAttribute);
        foreach (var attr in tree.Root.Attributes)
        {
            if (attr.Name == "ScrollY")
            {
                scrollYAttr = attr;
                break;
            }
        }
        Assert.Equal("ScrollY", scrollYAttr.Name);
        Assert.Equal(80.0, scrollYAttr.Value.Number);
    }

    [Fact]
    public void ScrollY_in_view_produces_scroll_diagnostics()
    {
        var app = new CounterApplication();
        var model = app.Initialize() with { ScrollY = 100 };
        var tree = app.BuildView(model);

        var builder = new LayoutTreeBuilder();
        var viewport = new PixelRectangle(0, 0, 960, 200);
        var result = builder.BuildLayoutTree(tree.Root, viewport);

        Assert.Single(result.ScrollDiagnostics);
        var diag = result.ScrollDiagnostics[0];
        Assert.True(diag.ScrollY > 0);
        Assert.True(diag.MaxScrollY > 0);
        Assert.Equal(diag.ScrollY, Math.Min(100, diag.MaxScrollY));
    }
}
