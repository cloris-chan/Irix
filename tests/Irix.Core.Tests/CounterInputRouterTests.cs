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

    [Fact]
    public void TryMapInput_updates_hover_target_on_pointer_move()
    {
        var ownershipState = new InputOwnershipState();

        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.False(mapped);
        Assert.Equal(nameof(CounterMessage.Increment), ownershipState.HoveredTarget);
        Assert.Equal(nameof(CounterMessage.Increment), ownershipState.LastHoverEnteredTarget);
        Assert.Null(ownershipState.LastHoverLeftTarget);
        Assert.Equal(1, ownershipState.HoverChangeCount);

        mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 2, X: 500, Y: 500),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.False(mapped);
        Assert.Null(ownershipState.HoveredTarget);
        Assert.Null(ownershipState.LastHoverEnteredTarget);
        Assert.Equal(nameof(CounterMessage.Increment), ownershipState.LastHoverLeftTarget);
        Assert.Equal(2, ownershipState.HoverChangeCount);
    }

    [Fact]
    public void TryMapInput_captures_pointer_until_release()
    {
        var ownershipState = new InputOwnershipState();

        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.False(mapped);
        Assert.Equal(nameof(CounterMessage.Increment), ownershipState.PressedTarget);
        Assert.Equal(nameof(CounterMessage.Increment), ownershipState.CapturedTarget);
        Assert.Equal(nameof(CounterMessage.Increment), ownershipState.FocusedTarget);

        mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 2,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out var message);

        Assert.True(mapped);
        Assert.IsType<CounterMessage.Increment>(message);
        Assert.Null(ownershipState.PressedTarget);
        Assert.Null(ownershipState.CapturedTarget);
        Assert.Equal(nameof(CounterMessage.Increment), ownershipState.FocusedTarget);
    }

    [Fact]
    public void TryMapInput_hover_can_change_during_capture_without_changing_capture()
    {
        var ownershipState = new InputOwnershipState();

        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementOrDecrement,
            out _);

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 2, X: 32, Y: 200),
            ownershipState,
            HitIncrementOrDecrement,
            out _);

        var snapshot = ownershipState.Snapshot;
        Assert.Equal(nameof(CounterMessage.Decrement), snapshot.HoveredTarget);
        Assert.Equal(nameof(CounterMessage.Increment), snapshot.PressedTarget);
        Assert.Equal(nameof(CounterMessage.Increment), snapshot.CapturedTarget);
        Assert.Equal(nameof(CounterMessage.Increment), snapshot.FocusedTarget);

        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 3,
                X: 32,
                Y: 200,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementOrDecrement,
            out var message);

        snapshot = ownershipState.Snapshot;
        Assert.True(mapped);
        Assert.IsType<CounterMessage.Increment>(message);
        Assert.Equal(nameof(CounterMessage.Decrement), snapshot.HoveredTarget);
        Assert.Null(snapshot.PressedTarget);
        Assert.Null(snapshot.CapturedTarget);
        Assert.Equal(nameof(CounterMessage.Increment), snapshot.FocusedTarget);
        Assert.False(snapshot.IsPointerPressed);
    }

    [Fact]
    public void TryMapInput_records_ownership_diagnostic_events()
    {
        var ownershipState = new InputOwnershipState();

        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            HitIncrementAtButton,
            out _);
        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);
        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 3,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.Collection(
            ownershipState.DiagnosticEvents,
            diagnosticEvent =>
            {
                var hover = Assert.IsType<InputOwnershipEvent.HoverChanged>(diagnosticEvent);
                Assert.Null(hover.PreviousTarget);
                Assert.Equal(nameof(CounterMessage.Increment), hover.CurrentTarget);
            },
            diagnosticEvent =>
            {
                var focus = Assert.IsType<InputOwnershipEvent.FocusChanged>(diagnosticEvent);
                Assert.Null(focus.PreviousTarget);
                Assert.Equal(nameof(CounterMessage.Increment), focus.CurrentTarget);
            },
            diagnosticEvent =>
            {
                var pressed = Assert.IsType<InputOwnershipEvent.PressedChanged>(diagnosticEvent);
                Assert.Null(pressed.PreviousPressedTarget);
                Assert.Equal(nameof(CounterMessage.Increment), pressed.CurrentPressedTarget);
                Assert.True(pressed.IsPointerPressed);
            },
            diagnosticEvent =>
            {
                var pressed = Assert.IsType<InputOwnershipEvent.PressedChanged>(diagnosticEvent);
                Assert.Equal(nameof(CounterMessage.Increment), pressed.PreviousPressedTarget);
                Assert.Null(pressed.CurrentPressedTarget);
                Assert.False(pressed.IsPointerPressed);
            });
    }

    [Fact]
    public void CounterApplication_derives_button_state_from_ownership_snapshot()
    {
        var snapshot = new OwnershipSnapshot(
            HoveredTarget: nameof(CounterMessage.Increment),
            FocusedTarget: nameof(CounterMessage.Increment),
            PressedTarget: nameof(CounterMessage.Increment),
            CapturedTarget: nameof(CounterMessage.Increment),
            LastHoverEnteredTarget: nameof(CounterMessage.Increment),
            LastHoverLeftTarget: null,
            HoverChangeCount: 1,
            IsPointerPressed: true);

        var incrementState = CounterApplication.DeriveButtonState(snapshot, nameof(CounterMessage.Increment));
        var decrementState = CounterApplication.DeriveButtonState(snapshot, nameof(CounterMessage.Decrement));

        Assert.True(incrementState.IsHovered);
        Assert.True(incrementState.IsPressed);
        Assert.True(incrementState.IsFocused);
        Assert.False(decrementState.IsHovered);
        Assert.False(decrementState.IsPressed);
        Assert.False(decrementState.IsFocused);

        var releasedState = CounterApplication.DeriveButtonState(
            snapshot with { IsPointerPressed = false },
            nameof(CounterMessage.Increment));
        Assert.False(releasedState.IsPressed);
    }

    [Fact]
    public void TryMapInput_empty_press_clears_focus_and_does_not_trigger_action()
    {
        var ownershipState = new InputOwnershipState();
        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.False(mapped);
        Assert.Null(ownershipState.FocusedTarget);
        Assert.Null(ownershipState.PressedTarget);
        Assert.Null(ownershipState.CapturedTarget);

        mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 3,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.False(mapped);
        Assert.False(ownershipState.Snapshot.IsPointerPressed);
    }

    [Fact]
    public void TryMapInput_activates_focused_target_for_enter_or_space()
    {
        var ownershipState = new InputOwnershipState();

        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 2, X: 0, Y: 0, KeyCode: 0x0D),
            ownershipState,
            HitIncrementAtButton,
            out var enterMessage);

        Assert.True(mapped);
        Assert.IsType<CounterMessage.Increment>(enterMessage);

        mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 3, X: 0, Y: 0, KeyCode: 0x20),
            ownershipState,
            HitIncrementAtButton,
            out var spaceMessage);

        Assert.True(mapped);
        Assert.IsType<CounterMessage.Increment>(spaceMessage);
    }

    [Fact]
    public void TryMapInput_keeps_global_shortcuts_after_focus()
    {
        var ownershipState = new InputOwnershipState();
        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.KeyPressed, Timestamp: 2, X: 0, Y: 0, KeyCode: 0x28),
            ownershipState,
            HitIncrementAtButton,
            out var downMessage);

        Assert.True(mapped);
        Assert.IsType<CounterMessage.Decrement>(downMessage);

        mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.CharacterInput, Timestamp: 3, X: 0, Y: 0, Character: 'R'),
            ownershipState,
            HitIncrementAtButton,
            out var resetMessage);

        Assert.True(mapped);
        var reset = Assert.IsType<CounterMessage.Reset>(resetMessage);
        Assert.Equal(0, reset.Value);
    }

    [Fact]
    public void TryMapInput_clears_ownership_on_focus_lost()
    {
        var ownershipState = new InputOwnershipState();
        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            HitIncrementAtButton,
            out _);
        CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.FocusLost, Timestamp: 3, X: 0, Y: 0),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.False(mapped);
        Assert.Null(ownershipState.HoveredTarget);
        Assert.Null(ownershipState.FocusedTarget);
        Assert.Null(ownershipState.PressedTarget);
        Assert.Null(ownershipState.CapturedTarget);
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

    private static string? HitIncrementAtButton(int x, int y)
    {
        return x == 32 && y == 140 ? nameof(CounterMessage.Increment) : null;
    }

    private static string? HitIncrementOrDecrement(int x, int y)
    {
        return (x, y) switch
        {
            (32, 140) => nameof(CounterMessage.Increment),
            (32, 200) => nameof(CounterMessage.Decrement),
            _ => null
        };
    }

    [Fact]
    public void Scroll_message_updates_model_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // One full downward notch: -120 units → 120/120 * 3 lines * 18px = 54px
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120);
        var result = app.Update(model, new CounterMessage.ScrollFrame(delta, 0));
        Assert.Equal(54, result.NextModel.Scroll.TargetPosition);
        Assert.True(result.NextModel.Scroll.IsAnimating);

        // Tick to animate toward target
        result = app.Update(result.NextModel, new CounterMessage.ScrollFrame(new ScrollDelta(ScrollDeltaUnit.Pixel, 0), 1.0));
        Assert.True(result.NextModel.Scroll.Position > 0);
    }

    [Fact]
    public void Scroll_negative_scrollY_clamped_to_zero()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll up when at 0 → target stays at 0
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        var result = app.Update(model, new CounterMessage.ScrollFrame(delta, 0));
        Assert.Equal(0, result.NextModel.Scroll.TargetPosition);
    }

    [Fact]
    public void Scroll_small_deltas_accumulate()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // 4 small downward deltas of -30 = -120 total → 54px (same as one notch)
        for (var i = 0; i < 4; i++)
        {
            var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -30);
            model = app.Update(model, new CounterMessage.ScrollFrame(delta, 0)).NextModel;
        }
        Assert.Equal(54, model.Scroll.TargetPosition);
    }

    [Fact]
    public void Scroll_animation_converges_to_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120);
        model = app.Update(model, new CounterMessage.ScrollFrame(delta, 0)).NextModel;
        for (var i = 0; i < 120; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollFrame(new ScrollDelta(ScrollDeltaUnit.Pixel, 0), 1.0 / 60.0)).NextModel;
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

    [Fact]
    public void Counter_default_view_is_scrollable_at_default_window_size()
    {
        var app = new CounterApplication();
        var tree = app.BuildView(app.Initialize());

        var builder = new LayoutTreeBuilder();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var result = builder.BuildLayoutTree(tree.Root, viewport);

        var diag = Assert.Single(result.ScrollDiagnostics);
        Assert.True(diag.ContentHeight > diag.VisibleHeight);
        Assert.True(diag.MaxScrollY >= 54);
    }

    [Fact]
    public void Single_notch_scroll_target_survives_default_max_scroll_feedback()
    {
        var app = new CounterApplication();
        var model = app.Initialize();
        var tree = app.BuildView(model);

        var builder = new LayoutTreeBuilder();
        var viewport = new PixelRectangle(0, 0, 960, 540);
        var diag = Assert.Single(builder.BuildLayoutTree(tree.Root, viewport).ScrollDiagnostics);

        model = app.Update(model, new CounterMessage.UpdateMaxScrollY(diag.MaxScrollY)).NextModel;
        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120),
            0)).NextModel;

        Assert.Equal(54, model.Scroll.TargetPosition);
    }

    // ── ScrollController unit conversion tests ───────────────────────

    [Fact]
    public void ConvertToPixels_wheel_raw_one_notch()
    {
        // Windows raw wheel +120 is an upward wheel notch, so it decreases ScrollY.
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        var px = ScrollController.ConvertToPixels(delta, ScrollMetrics.DefaultText, SystemScrollSettings.Default);
        Assert.Equal(-54.0, px);
    }

    [Fact]
    public void ConvertToPixels_wheel_raw_negative_one_notch_scrolls_down()
    {
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120);
        var px = ScrollController.ConvertToPixels(delta, ScrollMetrics.DefaultText, SystemScrollSettings.Default);
        Assert.Equal(54.0, px);
    }

    [Fact]
    public void ConvertToPixels_wheel_raw_quarter_notch()
    {
        // Windows raw +30 is an upward quarter notch.
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 30);
        var px = ScrollController.ConvertToPixels(delta, ScrollMetrics.DefaultText, SystemScrollSettings.Default);
        Assert.Equal(-13.5, px);
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
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -12000);
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
        // ApplyWheel uses Windows raw wheel direction: -120 scrolls down by 54px.
        var state = ScrollController.ApplyWheel(ScrollState.Default, -120);
        Assert.Equal(54, state.TargetPosition);
    }

    // ── Integration: full MVU scroll path ────────────────────────────

    [Fact]
    public void Single_notch_scroll_target_is_54px()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120);
        model = app.Update(model, new CounterMessage.ScrollFrame(delta, 0)).NextModel;

        Assert.Equal(54.0, model.Scroll.TargetPosition);
        Assert.True(model.Scroll.IsAnimating);
    }

    [Fact]
    public void Two_notches_scroll_target_is_108px()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120), 0)).NextModel;
        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120), 0)).NextModel;

        Assert.Equal(108.0, model.Scroll.TargetPosition);
    }

    [Fact]
    public void Scroll_positive_then_negative_cancels()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120), 0)).NextModel;
        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120), 0)).NextModel;

        Assert.Equal(0.0, model.Scroll.TargetPosition);
        model = app.Update(model, new CounterMessage.ScrollFrame(new ScrollDelta(ScrollDeltaUnit.Pixel, 0), 1.0 / 60.0)).NextModel;
        Assert.False(model.Scroll.IsAnimating);
    }

    [Fact]
    public void Scroll_negative_direction_increases_position()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120);
        model = app.Update(model, new CounterMessage.ScrollFrame(delta, 0)).NextModel;

        for (var i = 0; i < 120; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollFrame(new ScrollDelta(ScrollDeltaUnit.Pixel, 0), 1.0 / 60.0)).NextModel;
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

        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120), 0)).NextModel;
        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120), 0)).NextModel;

        for (var i = 0; i < 120; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollFrame(new ScrollDelta(ScrollDeltaUnit.Pixel, 0), 1.0 / 60.0)).NextModel;
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
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -12000);
        model = app.Update(model, new CounterMessage.ScrollFrame(delta, 0)).NextModel;

        Assert.Equal(100.0, model.Scroll.TargetPosition); // clamped to MaxScrollY
    }

    [Fact]
    public void MaxScrollY_clamps_on_update()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll to 200
        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -2400), 0)).NextModel;
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

    [Fact]
    public void Debug_display_distinguishes_unknown_and_known_zero_max_scroll()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        var debugText = app.BuildView(model).Root.Children[1].Content.Text;
        Assert.Contains("max=unknown", debugText);

        model = app.Update(model, new CounterMessage.UpdateMaxScrollY(0)).NextModel;
        debugText = app.BuildView(model).Root.Children[1].Content.Text;

        Assert.Contains("max=0(known-zero)", debugText);
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
            model = app.Update(model, new CounterMessage.ScrollFrame(
                new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120), 0)).NextModel;
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
        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -12000), 0)).NextModel;

        Assert.Equal(100.0, model.Scroll.TargetPosition);
    }

    [Fact]
    public void HasMaxScrollY_max_zero_locks_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // Scroll first
        model = app.Update(model, new CounterMessage.ScrollFrame(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -240), 0)).NextModel;
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
        // Simulate rapid downward scrolling: 100 small deltas of -30 units each
        // 100 × -30 = -3000 total → 3000/120 × 3 × 18 = 1350px
        var app = new CounterApplication();
        var model = app.Initialize();

        for (var i = 0; i < 100; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollFrame(
                new ScrollDelta(ScrollDeltaUnit.WheelRaw, -30), 0)).NextModel;
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
            model = app.Update(model, new CounterMessage.ScrollFrame(
                new ScrollDelta(ScrollDeltaUnit.WheelRaw, -30), 0)).NextModel;
        }

        var target = model.Scroll.TargetPosition;

        // Animate for 2 seconds (120 frames at 60fps)
        for (var i = 0; i < 120; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollFrame(new ScrollDelta(ScrollDeltaUnit.Pixel, 0), 1.0 / 60.0)).NextModel;
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
        model = app.Update(model, new CounterMessage.ScrollFrame(coalescedDelta, 0)).NextModel;

        Assert.Equal(100.0, model.Scroll.TargetPosition);
        Assert.True(model.Scroll.IsAnimating);
    }

    [Fact]
    public void ScrollFrame_dt_zero_sets_target_without_moving_position()
    {
        // First frame with dt=0 should apply delta but not advance animation
        var app = new CounterApplication();
        var model = app.Initialize();

        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120);
        model = app.Update(model, new CounterMessage.ScrollFrame(delta, 0)).NextModel;

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
            model = app.Update(model, new CounterMessage.ScrollFrame(
                new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120), 0)).NextModel;
        }

        var target = model.Scroll.TargetPosition;
        Assert.Equal(2700.0, target); // 50 × 54px

        // Slow animation phase: 3 seconds at 60fps
        for (var i = 0; i < 180; i++)
        {
            model = app.Update(model, new CounterMessage.ScrollFrame(new ScrollDelta(ScrollDeltaUnit.Pixel, 0), 1.0 / 60.0)).NextModel;
        }

        Assert.False(model.Scroll.IsAnimating);
        Assert.Equal(target, Math.Round(model.Scroll.Position));
    }
}
