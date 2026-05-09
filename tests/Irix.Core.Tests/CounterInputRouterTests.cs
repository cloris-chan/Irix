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
    [InlineData(120)]   // one notch up
    [InlineData(-120)]  // one notch down
    [InlineData(240)]   // two notches up
    [InlineData(30)]    // quarter notch (high-precision touchpad)
    public void TryMapInput_maps_mouse_wheel_to_wheel_message(int delta)
    {
        var inputEvent = new RawInputEvent(
            RawInputEventKind.PointerWheel,
            Timestamp: 1,
            X: 0,
            Y: 0,
            Delta: delta);

        var mapped = CounterInputRouter.TryMapInput(inputEvent, (_, _) => null, out var message);

        Assert.True(mapped);
        var wheel = Assert.IsType<CounterMessage.Wheel>(message);
        Assert.Equal(delta, wheel.RawDelta); // raw delta preserved, no truncation
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

        // Apply one full notch (120 units) → should accumulate and produce target
        var result = app.Update(model, new CounterMessage.Wheel(120));
        Assert.Equal(40, result.NextModel.Scroll.TargetPosition); // 120/120 * 40 = 40px
        Assert.True(result.NextModel.Scroll.IsAnimating);

        // Tick to animate toward target
        result = app.Update(result.NextModel, new CounterMessage.Tick(1.0f));
        Assert.True(result.NextModel.Scroll.Position > 0);
    }

    [Fact]
    public void Scroll_negative_scrollY_clamped_to_zero()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll up when at 0 → target stays at 0
        var result = app.Update(model, new CounterMessage.Wheel(-120));
        Assert.Equal(0, result.NextModel.Scroll.TargetPosition); // clamped to 0
    }

    [Fact]
    public void Scroll_small_deltas_accumulate()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // 4 small deltas of 30 = 120 total → 40px
        for (var i = 0; i < 4; i++)
        {
            model = app.Update(model, new CounterMessage.Wheel(30)).NextModel;
        }
        Assert.Equal(40, model.Scroll.TargetPosition);
    }

    [Fact]
    public void Scroll_animation_converges_to_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Apply wheel and animate for 2 seconds (should converge)
        model = app.Update(model, new CounterMessage.Wheel(120)).NextModel;
        for (var i = 0; i < 120; i++) // 120 frames at 60fps = 2 seconds
        {
            model = app.Update(model, new CounterMessage.Tick(1f / 60f)).NextModel;
        }

        Assert.False(model.Scroll.IsAnimating);
        Assert.Equal(model.Scroll.TargetPosition, (int)Math.Round(model.Scroll.Position));
    }

    [Fact]
    public void ScrollY_appears_in_view_as_attribute()
    {
        var app = new CounterApplication();
        var model = app.Initialize() with { Scroll = new ScrollState { TargetPosition = 80, Position = 80 } };
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
        var model = app.Initialize() with { Scroll = new ScrollState { TargetPosition = 100, Position = 100 } };
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
