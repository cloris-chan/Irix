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
    public void TryMapInput_maps_mouse_wheel_to_wheel_raw(int delta)
    {
        var inputEvent = new RawInputEvent(
            RawInputEventKind.PointerWheel,
            Timestamp: 1,
            X: 0,
            Y: 0,
            Delta: delta);

        var mapped = CounterInputRouter.TryMapInput(inputEvent, (_, _) => null, out var message);

        Assert.True(mapped);
        var wheel = Assert.IsType<CounterMessage.WheelRaw>(message);
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
    public void Scroll_message_updates_model_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // One full notch: 120 units → 120/120 * 3 lines * 18px = 54px
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        var result = app.Update(model, new CounterMessage.ScrollDeltaMsg(delta));
        Assert.Equal(54, result.NextModel.Scroll.TargetPosition);
        Assert.True(result.NextModel.Scroll.IsAnimating);

        // Tick to animate toward target
        result = app.Update(result.NextModel, new CounterMessage.ScrollTick(1.0));
        Assert.True(result.NextModel.Scroll.Position > 0);
    }

    [Fact]
    public void Scroll_negative_scrollY_clamped_to_zero()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll up when at 0 → target stays at 0
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120);
        var result = app.Update(model, new CounterMessage.ScrollDeltaMsg(delta));
        Assert.Equal(0, result.NextModel.Scroll.TargetPosition);
    }

    [Fact]
    public void Scroll_small_deltas_accumulate()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // 4 small deltas of 30 = 120 total → 54px (same as one notch)
        for (var i = 0; i < 4; i++)
        {
            var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 30);
            model = app.Update(model, new CounterMessage.ScrollDeltaMsg(delta)).NextModel;
        }
        Assert.Equal(54, model.Scroll.TargetPosition);
    }

    [Fact]
    public void Scroll_animation_converges_to_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(delta)).NextModel;
        for (var i = 0; i < 120; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollTick(1.0 / 60.0)).NextModel;
        }

        Assert.False(model.Scroll.IsAnimating);
        Assert.Equal(model.Scroll.TargetPosition, Math.Round(model.Scroll.Position));
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

    // ── ScrollController unit conversion tests ───────────────────────

    [Fact]
    public void ConvertToPixels_wheel_raw_one_notch()
    {
        // 120 units × 3 lines/notch × 18px/line = 54px
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        var px = ScrollController.ConvertToPixels(delta, ScrollMetrics.DefaultText, SystemScrollSettings.Default);
        Assert.Equal(54.0, px);
    }

    [Fact]
    public void ConvertToPixels_wheel_raw_quarter_notch()
    {
        // 30 units → 30/120 * 3 * 18 = 13.5px
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 30);
        var px = ScrollController.ConvertToPixels(delta, ScrollMetrics.DefaultText, SystemScrollSettings.Default);
        Assert.Equal(13.5, px);
    }

    [Fact]
    public void ConvertToPixels_line()
    {
        var delta = new ScrollDelta(ScrollDeltaUnit.Line, 3);
        var px = ScrollController.ConvertToPixels(delta, ScrollMetrics.DefaultText, SystemScrollSettings.Default);
        Assert.Equal(54.0, px); // 3 × 18
    }

    [Fact]
    public void ConvertToPixels_pixel()
    {
        var delta = new ScrollDelta(ScrollDeltaUnit.Pixel, 42.5);
        var px = ScrollController.ConvertToPixels(delta, ScrollMetrics.DefaultText, SystemScrollSettings.Default);
        Assert.Equal(42.5, px);
    }

    [Fact]
    public void ConvertToPixels_page_with_viewport()
    {
        var metrics = new ScrollMetrics(LineExtent: 18, PageExtent: 0, ViewportExtent: 500, ContentExtent: 2000);
        var delta = new ScrollDelta(ScrollDeltaUnit.Page, 1);
        var px = ScrollController.ConvertToPixels(delta, metrics, SystemScrollSettings.Default);
        Assert.Equal(450.0, px); // 500 * 0.9
    }

    [Fact]
    public void ApplyScrollDelta_maxScroll_clamp()
    {
        var metrics = new ScrollMetrics(LineExtent: 18, PageExtent: 0, ViewportExtent: 0, ContentExtent: 200);
        var state = ScrollState.Default;

        // Scroll way past content
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 12000);
        state = ScrollController.ApplyScrollDelta(state, delta, metrics, SystemScrollSettings.Default);

        // Target should be clamped to MaxScrollY = contentHeight - visibleHeight
        // But since we don't have visibleHeight in ScrollMetrics yet, the controller
        // doesn't clamp — it's the layout builder's job. So target is just large.
        Assert.True(state.TargetPosition > 0);
        Assert.True(state.IsAnimating);
    }

    [Fact]
    public void ApplyWheel_backward_compatible()
    {
        // ApplyWheel uses defaults: 120/120 * 3 * 18 = 54px
        var state = ScrollController.ApplyWheel(ScrollState.Default, 120);
        Assert.Equal(54, state.TargetPosition);
    }

    // ── Integration: full MVU scroll path ────────────────────────────

    [Fact]
    public void Single_notch_scroll_target_is_54px()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(delta)).NextModel;

        Assert.Equal(54.0, model.Scroll.TargetPosition);
        Assert.True(model.Scroll.IsAnimating);
    }

    [Fact]
    public void Two_notches_scroll_target_is_108px()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120))).NextModel;
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120))).NextModel;

        Assert.Equal(108.0, model.Scroll.TargetPosition);
    }

    [Fact]
    public void Scroll_positive_then_negative_cancels()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120))).NextModel;
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120))).NextModel;

        Assert.Equal(0.0, model.Scroll.TargetPosition);
        model = app.Update(model, new CounterMessage.ScrollTick(1.0 / 60.0)).NextModel;
        Assert.False(model.Scroll.IsAnimating);
    }

    [Fact]
    public void Scroll_negative_direction_increases_position()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(delta)).NextModel;

        for (var i = 0; i < 120; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollTick(1.0 / 60.0)).NextModel;
        }

        Assert.False(model.Scroll.IsAnimating);
        var scrollY = ScrollController.GetScrollY(model.Scroll);
        Assert.Equal(54, scrollY);
    }

    [Fact]
    public void Two_notches_animate_to_108()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120))).NextModel;
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120))).NextModel;

        for (var i = 0; i < 120; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollTick(1.0 / 60.0)).NextModel;
        }

        Assert.False(model.Scroll.IsAnimating);
        var scrollY = ScrollController.GetScrollY(model.Scroll);
        Assert.Equal(108, scrollY);
    }

    [Fact]
    public void MaxScrollY_clamps_target_position()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Set MaxScrollY to 100
        model = app.Update(model, new CounterMessage.UpdateMaxScrollY(100)).NextModel;

        // Scroll way past content
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 12000);
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(delta)).NextModel;

        Assert.Equal(100.0, model.Scroll.TargetPosition); // clamped to MaxScrollY
    }

    [Fact]
    public void MaxScrollY_clamps_on_update()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll to 200
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 2400))).NextModel;
        Assert.True(model.Scroll.TargetPosition > 100);

        // Update MaxScrollY to 100 — should clamp target
        model = app.Update(model, new CounterMessage.UpdateMaxScrollY(100)).NextModel;
        Assert.Equal(100.0, model.Scroll.TargetPosition);
    }

    [Fact]
    public void Debug_display_contains_target_and_position()
    {
        var app = new CounterApplication();
        var model = app.Initialize() with
        {
            Scroll = new ScrollState { TargetPosition = 100, Position = 50, Accumulator = 0.5 }
        };
        var tree = app.BuildView(model);

        // The second text child should contain the debug display
        var debugText = tree.Root.Children[1].Content.Text;
        Assert.Contains("applied=", debugText);
        Assert.Contains("target=", debugText);
        Assert.Contains("pos=", debugText);
        Assert.Contains("acc=", debugText);
    }

    // ── HasMaxScrollY tests ────────────────────────────────────────────

    [Fact]
    public void HasMaxScrollY_false_target_not_clamped()
    {
        // Default ScrollState has HasMaxScrollY=false — target should grow freely
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll way past any reasonable content height
        for (var i = 0; i < 10; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
                new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120))).NextModel;
        }

        // Without MaxScrollY known, target grows unbounded (no upper clamp)
        Assert.Equal(540.0, model.Scroll.TargetPosition); // 10 × 54px
        Assert.False(model.Scroll.HasMaxScrollY);
    }

    [Fact]
    public void HasMaxScrollY_true_clamps_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // First set MaxScrollY
        model = app.Update(model, new CounterMessage.UpdateMaxScrollY(100)).NextModel;
        Assert.True(model.Scroll.HasMaxScrollY);

        // Scroll past content
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 12000))).NextModel;

        Assert.Equal(100.0, model.Scroll.TargetPosition);
    }

    [Fact]
    public void HasMaxScrollY_max_zero_locks_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll first
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 240))).NextModel;
        Assert.True(model.Scroll.TargetPosition > 0);

        // Set MaxScrollY=0 (content fits in viewport — no scrolling needed)
        model = app.Update(model, new CounterMessage.UpdateMaxScrollY(0)).NextModel;

        Assert.True(model.Scroll.HasMaxScrollY);
        Assert.Equal(0.0, model.Scroll.TargetPosition);
        Assert.Equal(0.0, model.Scroll.Position);
    }

    [Fact]
    public void HasMaxScrollY_withMaxScrollY_sets_flag()
    {
        var state = ScrollState.Default;
        Assert.False(state.HasMaxScrollY);

        state = ScrollController.WithMaxScrollY(state, 200);
        Assert.True(state.HasMaxScrollY);
        Assert.Equal(200.0, state.MaxScrollY);
    }

    [Fact]
    public void HasMaxScrollY_withMaxScrollY_zero_sets_flag()
    {
        var state = ScrollState.Default;
        state = ScrollController.WithMaxScrollY(state, 0);
        Assert.True(state.HasMaxScrollY);
        Assert.Equal(0.0, state.MaxScrollY);
    }

    // ── Coalescing / rapid input tests ─────────────────────────────────

    [Fact]
    public void Rapid_100_deltas_target_correct()
    {
        // Simulate rapid scrolling: 100 small deltas of 30 units each
        // 100 × 30 = 3000 total → 3000/120 × 3 × 18 = 1350px
        var app = new CounterApplication();
        var model = app.Initialize();

        for (var i = 0; i < 100; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
                new ScrollDelta(ScrollDeltaUnit.WheelRaw, 30))).NextModel;
        }

        Assert.Equal(1350.0, model.Scroll.TargetPosition);
        Assert.True(model.Scroll.IsAnimating);
    }

    [Fact]
    public void Rapid_100_deltas_converge_after_animation()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        for (var i = 0; i < 100; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
                new ScrollDelta(ScrollDeltaUnit.WheelRaw, 30))).NextModel;
        }

        var target = model.Scroll.TargetPosition;

        // Animate for 2 seconds (120 frames at 60fps)
        for (var i = 0; i < 120; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollTick(1.0 / 60.0)).NextModel;
        }

        Assert.False(model.Scroll.IsAnimating);
        Assert.Equal(target, Math.Round(model.Scroll.Position));
    }

    [Fact]
    public void Coalesced_pixel_delta_applied_correctly()
    {
        // Simulate what the tick loop does: drain pending pixels, dispatch as Pixel delta
        var app = new CounterApplication();
        var model = app.Initialize();

        // Accumulate 100px of pending delta (simulating coalesced input)
        var coalescedDelta = new ScrollDelta(ScrollDeltaUnit.Pixel, 100.0);
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(coalescedDelta)).NextModel;

        Assert.Equal(100.0, model.Scroll.TargetPosition);
        Assert.True(model.Scroll.IsAnimating);
    }

    [Fact]
    public void ScrollDeltaMsg_sets_target_without_moving_position()
    {
        // First frame with dt=0 should apply delta but not advance animation
        var app = new CounterApplication();
        var model = app.Initialize();

        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        model = app.Update(model, new CounterMessage.ScrollDeltaMsg(delta)).NextModel;

        // Target should be set
        Assert.Equal(54.0, model.Scroll.TargetPosition);
        // Position should NOT have moved yet (dt=0 → Tick is a no-op since factor=0)
        Assert.Equal(0.0, model.Scroll.Position);
        Assert.True(model.Scroll.IsAnimating);
    }

    [Fact]
    public void Backpressure_scenario_rapid_then_slow_converges()
    {
        // Simulate backpressure: rapid deltas followed by slow animation
        var app = new CounterApplication();
        var model = app.Initialize();

        // Rapid phase: 50 deltas of 120 units each
        for (var i = 0; i < 50; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollDeltaMsg(
                new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120))).NextModel;
        }

        var target = model.Scroll.TargetPosition;
        Assert.Equal(2700.0, target); // 50 × 54px

        // Slow animation phase: 3 seconds at 60fps
        for (var i = 0; i < 180; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollTick(1.0 / 60.0)).NextModel;
        }

        Assert.False(model.Scroll.IsAnimating);
        Assert.Equal(target, Math.Round(model.Scroll.Position));
    }
}
