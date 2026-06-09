using Irix;
using Irix.Drawing;
using Irix.Platform;
using Irix.Poc;
using Irix.Rendering;
using Xunit;

namespace Irix.Core.Tests;

public sealed class CounterInputRouterTests
{
    private readonly VirtualTextArena _arena = new();

    [Fact]
    public void TryMapInput_maps_left_click_to_button_action()
    {
        var ownershipState = new InputOwnershipState();
        var pressMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);
        var releaseMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out var message);

        Assert.False(pressMapped);
        Assert.True(releaseMapped);
        Assert.IsType<CounterMessage.Increment>(message);
    }

    [Fact]
    public void TryMapInput_uses_hit_test_service_for_pointer_ownership()
    {
        var ownershipState = new InputOwnershipState();
        var hitTestService = new IncrementButtonHitTestService();
        var pressMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            hitTestService,
            out _);
        var releaseMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            hitTestService,
            out var message);

        Assert.False(pressMapped);
        Assert.True(releaseMapped);
        Assert.IsType<CounterMessage.Increment>(message);
    }

    [Fact]
    public void TryMapInput_uses_action_mapper_for_pointer_activation()
    {
        var ownershipState = new InputOwnershipState();
        var hitTestResolver = new DelegateActionHitTestResolver(HitIncrementAtButton);
        var actionMapper = new TimestampResetActionMapper();
        var pressMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            hitTestResolver,
            actionMapper,
            out _);

        hitTestResolver = new DelegateActionHitTestResolver(HitIncrementAtButton);
        var releaseMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 42,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            hitTestResolver,
            actionMapper,
            out var message);

        var reset = Assert.IsType<CounterMessage.Reset>(message);
        Assert.False(pressMapped);
        Assert.True(releaseMapped);
        Assert.Equal(42, reset.Value);
    }

    [Fact]
    public void TryMapInput_does_not_map_release_without_pressed_ownership()
    {
        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            new InputOwnershipState(),
            HitIncrementAtButton,
            out _);

        Assert.False(mapped);
    }

    [Fact]
    public void TryMapInput_does_not_map_left_click_without_hit_target()
    {
        var ownershipState = new InputOwnershipState();
        var pressMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);
        var releaseMapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 2,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.False(pressMapped);
        Assert.False(releaseMapped);
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
        Assert.Equal(new ActionId(1), ownershipState.HoveredTarget);
        Assert.Equal(new ActionId(1), ownershipState.LastHoverEnteredTarget);
        Assert.True(ownershipState.LastHoverLeftTarget.IsNone);
        Assert.Equal(1, ownershipState.HoverChangeCount);

        mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 2, X: 500, Y: 500),
            ownershipState,
            HitIncrementAtButton,
            out _);

        Assert.False(mapped);
        Assert.True(ownershipState.HoveredTarget.IsNone);
        Assert.True(ownershipState.LastHoverEnteredTarget.IsNone);
        Assert.Equal(new ActionId(1), ownershipState.LastHoverLeftTarget);
        Assert.Equal(2, ownershipState.HoverChangeCount);
    }

    [Fact]
    public void TryMapInput_press_updates_hover_target_from_current_hit()
    {
        var ownershipState = new InputOwnershipState();
        CounterInputRouter.TryMapInput(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 200),
            ownershipState,
            HitIncrementOrDecrement,
            out _);

        var mapped = CounterInputRouter.TryMapInput(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            HitIncrementOrDecrement,
            out _);

        var snapshot = ownershipState.Snapshot;
        Assert.False(mapped);
        Assert.Equal(new ActionId(1), snapshot.HoveredTarget);
        Assert.Equal(new ActionId(1), snapshot.PressedTarget);
        Assert.Equal(new ActionId(1), snapshot.CapturedTarget);
        Assert.Equal(new ActionId(1), snapshot.FocusedTarget);
        Assert.Equal(new ActionId(1), snapshot.LastHoverEnteredTarget);
        Assert.Equal(new ActionId(2), snapshot.LastHoverLeftTarget);
        Assert.Equal(2, snapshot.HoverChangeCount);
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
        Assert.Equal(new ActionId(1), ownershipState.PressedTarget);
        Assert.Equal(new ActionId(1), ownershipState.CapturedTarget);
        Assert.Equal(new ActionId(1), ownershipState.FocusedTarget);

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
        Assert.True(ownershipState.HoveredTarget.IsNone);
        Assert.True(ownershipState.PressedTarget.IsNone);
        Assert.True(ownershipState.CapturedTarget.IsNone);
        Assert.Equal(new ActionId(1), ownershipState.FocusedTarget);
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
        Assert.Equal(new ActionId(2), snapshot.HoveredTarget);
        Assert.Equal(new ActionId(1), snapshot.PressedTarget);
        Assert.Equal(new ActionId(1), snapshot.CapturedTarget);
        Assert.Equal(new ActionId(1), snapshot.FocusedTarget);

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
        Assert.Equal(new ActionId(2), snapshot.HoveredTarget);
        Assert.True(snapshot.PressedTarget.IsNone);
        Assert.True(snapshot.CapturedTarget.IsNone);
        Assert.Equal(new ActionId(1), snapshot.FocusedTarget);
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
                Assert.Equal(InputOwnershipEventKind.HoverChanged, diagnosticEvent.Kind);
                Assert.True(diagnosticEvent.PreviousTarget.IsNone);
                Assert.Equal(new ActionId(1), diagnosticEvent.CurrentTarget);
            },
            diagnosticEvent =>
            {
                Assert.Equal(InputOwnershipEventKind.FocusChanged, diagnosticEvent.Kind);
                Assert.True(diagnosticEvent.PreviousTarget.IsNone);
                Assert.Equal(new ActionId(1), diagnosticEvent.CurrentTarget);
            },
            diagnosticEvent =>
            {
                Assert.Equal(InputOwnershipEventKind.PressedChanged, diagnosticEvent.Kind);
                Assert.True(diagnosticEvent.PreviousPressedTarget.IsNone);
                Assert.Equal(new ActionId(1), diagnosticEvent.CurrentPressedTarget);
                Assert.True(diagnosticEvent.IsPointerPressed);
            },
            diagnosticEvent =>
            {
                Assert.Equal(InputOwnershipEventKind.HoverChanged, diagnosticEvent.Kind);
                Assert.Equal(new ActionId(1), diagnosticEvent.PreviousTarget);
                Assert.True(diagnosticEvent.CurrentTarget.IsNone);
            },
            diagnosticEvent =>
            {
                Assert.Equal(InputOwnershipEventKind.PressedChanged, diagnosticEvent.Kind);
                Assert.Equal(new ActionId(1), diagnosticEvent.PreviousPressedTarget);
                Assert.True(diagnosticEvent.CurrentPressedTarget.IsNone);
                Assert.False(diagnosticEvent.IsPointerPressed);
            });
    }

    [Fact]
    public void Ownership_diagnostic_events_are_bounded()
    {
        var ownershipState = new InputOwnershipState();

        for (var i = 0; i < 160; i++)
        {
            var x = i % 2 == 0 ? 32 : 500;
            CounterInputRouter.TryMapInput(
                new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: i + 1, X: x, Y: 140),
                ownershipState,
                HitIncrementAtButton,
                out _);
        }

        Assert.Equal(128, ownershipState.DiagnosticEvents.Count);
        Assert.All(ownershipState.DiagnosticEvents, diagnosticEvent => Assert.Equal(InputOwnershipEventKind.HoverChanged, diagnosticEvent.Kind));
    }

    [Fact]
    public void CounterApplication_derives_button_state_from_ownership_snapshot()
    {
        var snapshot = new OwnershipSnapshot(
            HoveredTarget: new ActionId(1),
            FocusedTarget: new ActionId(1),
            PressedTarget: new ActionId(1),
            CapturedTarget: new ActionId(1),
            LastHoverEnteredTarget: new ActionId(1),
            LastHoverLeftTarget: ActionId.None,
            HoverChangeCount: 1,
            IsPointerPressed: true);

        var incrementState = CounterApplication.DeriveButtonState(snapshot, new ActionId(1));
        var decrementState = CounterApplication.DeriveButtonState(snapshot, new ActionId(2));

        Assert.True(incrementState.IsHovered);
        Assert.True(incrementState.IsPressed);
        Assert.True(incrementState.IsFocused);
        Assert.False(decrementState.IsHovered);
        Assert.False(decrementState.IsPressed);
        Assert.False(decrementState.IsFocused);

        var releasedState = CounterApplication.DeriveButtonState(
            new OwnershipSnapshot(
                snapshot.HoveredTarget,
                snapshot.FocusedTarget,
                snapshot.PressedTarget,
                snapshot.CapturedTarget,
                snapshot.LastHoverEnteredTarget,
                snapshot.LastHoverLeftTarget,
                snapshot.HoverChangeCount,
                IsPointerPressed: false),
            new ActionId(1));
        Assert.False(releasedState.IsPressed);
    }

    [Fact]
    public void ControlVisualState_projection_and_adapter_preserve_existing_button_properties()
    {
        var targetId = new ActionId(1);
        var cases = new[]
        {
            (
                Name: "normal",
                Snapshot: default,
                Expected: default(ControlVisualState)),
            (
                Name: "hovered",
                Snapshot: new OwnershipSnapshot(
                    HoveredTarget: targetId,
                    FocusedTarget: ActionId.None,
                    PressedTarget: ActionId.None,
                    CapturedTarget: ActionId.None,
                    LastHoverEnteredTarget: targetId,
                    LastHoverLeftTarget: ActionId.None,
                    HoverChangeCount: 1,
                    IsPointerPressed: false),
                Expected: new ControlVisualState(IsHovered: true, IsPressed: false, IsFocused: false)),
            (
                Name: "pressed",
                Snapshot: new OwnershipSnapshot(
                    HoveredTarget: ActionId.None,
                    FocusedTarget: targetId,
                    PressedTarget: targetId,
                    CapturedTarget: targetId,
                    LastHoverEnteredTarget: ActionId.None,
                    LastHoverLeftTarget: ActionId.None,
                    HoverChangeCount: 0,
                    IsPointerPressed: true),
                Expected: new ControlVisualState(IsHovered: false, IsPressed: true, IsFocused: true)),
            (
                Name: "focused",
                Snapshot: new OwnershipSnapshot(
                    HoveredTarget: ActionId.None,
                    FocusedTarget: targetId,
                    PressedTarget: ActionId.None,
                    CapturedTarget: ActionId.None,
                    LastHoverEnteredTarget: ActionId.None,
                    LastHoverLeftTarget: ActionId.None,
                    HoverChangeCount: 0,
                    IsPointerPressed: false),
                Expected: new ControlVisualState(IsHovered: false, IsPressed: false, IsFocused: true)),
            (
                Name: "focusLost",
                Snapshot: new OwnershipSnapshot(
                    HoveredTarget: ActionId.None,
                    FocusedTarget: ActionId.None,
                    PressedTarget: ActionId.None,
                    CapturedTarget: ActionId.None,
                    LastHoverEnteredTarget: ActionId.None,
                    LastHoverLeftTarget: targetId,
                    HoverChangeCount: 2,
                    IsPointerPressed: false),
                Expected: default(ControlVisualState))
        };

        foreach (var caseItem in cases)
        {
            var state = ControlVisualStateProjection.Project(caseItem.Snapshot, targetId);
            var properties = ControlVisualStatePropertyAdapter.ToProperties(state);

            Assert.Equal(caseItem.Expected, state);
            Assert.Equal(caseItem.Expected.IsHovered, GetBooleanProperty(properties, VirtualPropertyKey.IsHovered));
            Assert.Equal(caseItem.Expected.IsPressed, GetBooleanProperty(properties, VirtualPropertyKey.IsPressed));
            Assert.Equal(caseItem.Expected.IsFocused, GetBooleanProperty(properties, VirtualPropertyKey.IsFocused));
            Assert.DoesNotContain(properties, property => VirtualPropertyDiagnostics.Format(property.Key) == "IsEnabled");
        }
    }

    [Fact]
    public void ButtonPropertyBundle_preserves_action_and_visual_state_wire_contract()
    {
        var actionId = new ActionId(1);
        var properties = ButtonPropertyBundle.Create(
            actionId,
            new ControlVisualState(IsHovered: true, IsPressed: false, IsFocused: true));

        Assert.Equal(4, properties.Length);
        Assert.Equal(actionId, GetActionId(properties));
        Assert.True(GetBooleanProperty(properties, VirtualPropertyKey.IsHovered));
        Assert.False(GetBooleanProperty(properties, VirtualPropertyKey.IsPressed));
        Assert.True(GetBooleanProperty(properties, VirtualPropertyKey.IsFocused));
        Assert.DoesNotContain(properties, property => VirtualPropertyDiagnostics.Format(property.Key) == "IsEnabled");
    }

    [Fact]
    public void CounterApplication_button_properties_match_derived_visual_state_for_each_input_state()
    {
        var app = new CounterApplication();
        var targetId = new ActionId(1);
        var cases = new[]
        {
            default,
            new OwnershipSnapshot(
                HoveredTarget: targetId,
                FocusedTarget: ActionId.None,
                PressedTarget: ActionId.None,
                CapturedTarget: ActionId.None,
                LastHoverEnteredTarget: targetId,
                LastHoverLeftTarget: ActionId.None,
                HoverChangeCount: 1,
                IsPointerPressed: false),
            new OwnershipSnapshot(
                HoveredTarget: ActionId.None,
                FocusedTarget: targetId,
                PressedTarget: targetId,
                CapturedTarget: targetId,
                LastHoverEnteredTarget: ActionId.None,
                LastHoverLeftTarget: ActionId.None,
                HoverChangeCount: 0,
                IsPointerPressed: true),
            new OwnershipSnapshot(
                HoveredTarget: ActionId.None,
                FocusedTarget: targetId,
                PressedTarget: ActionId.None,
                CapturedTarget: ActionId.None,
                LastHoverEnteredTarget: ActionId.None,
                LastHoverLeftTarget: ActionId.None,
                HoverChangeCount: 0,
                IsPointerPressed: false),
            new OwnershipSnapshot(
                HoveredTarget: ActionId.None,
                FocusedTarget: ActionId.None,
                PressedTarget: ActionId.None,
                CapturedTarget: ActionId.None,
                LastHoverEnteredTarget: ActionId.None,
                LastHoverLeftTarget: targetId,
                HoverChangeCount: 2,
                IsPointerPressed: false)
        };

        foreach (var snapshot in cases)
        {
            var model = app.Initialize() with { InputOwnership = snapshot };
            var expected = CounterApplication.DeriveButtonState(snapshot, targetId);
            var button = FindButton(app.BuildView(model).Root, targetId);

            Assert.Equal(expected.IsHovered, GetBooleanProperty(button, VirtualPropertyKey.IsHovered));
            Assert.Equal(expected.IsPressed, GetBooleanProperty(button, VirtualPropertyKey.IsPressed));
            Assert.Equal(expected.IsFocused, GetBooleanProperty(button, VirtualPropertyKey.IsFocused));
        }
    }

    [Fact]
    public void CounterApplication_build_view_button_includes_action_and_visual_state_bundle()
    {
        var app = new CounterApplication();
        var targetId = new ActionId(1);
        var model = app.Initialize() with
        {
            InputOwnership = new OwnershipSnapshot(
                HoveredTarget: targetId,
                FocusedTarget: targetId,
                PressedTarget: targetId,
                CapturedTarget: targetId,
                LastHoverEnteredTarget: targetId,
                LastHoverLeftTarget: ActionId.None,
                HoverChangeCount: 1,
                IsPointerPressed: true)
        };

        var button = FindButton(app.BuildView(model).Root, targetId);

        Assert.Equal(targetId, GetActionId(button));
        Assert.True(GetBooleanProperty(button, VirtualPropertyKey.IsHovered));
        Assert.True(GetBooleanProperty(button, VirtualPropertyKey.IsPressed));
        Assert.True(GetBooleanProperty(button, VirtualPropertyKey.IsFocused));
        Assert.False(ContainsProperty(button.Properties, property => VirtualPropertyDiagnostics.Format(property.Key) == "IsEnabled"));
    }

    [Fact]
    public void InputVisualStateChanged_updates_model_snapshot_without_changing_count_or_scroll()
    {
        var app = new CounterApplication();
        var model = app.Initialize();
        var snapshot = new OwnershipSnapshot(
            HoveredTarget: new ActionId(1),
            FocusedTarget: ActionId.None,
            PressedTarget: ActionId.None,
            CapturedTarget: ActionId.None,
            LastHoverEnteredTarget: new ActionId(1),
            LastHoverLeftTarget: ActionId.None,
            HoverChangeCount: 1,
            IsPointerPressed: false);

        var result = app.Update(model, new CounterMessage.InputVisualStateChanged(snapshot));

        Assert.Equal(model.Count, result.NextModel.Count);
        Assert.Equal(model.Scroll, result.NextModel.Scroll);
        Assert.Equal(snapshot, result.NextModel.InputOwnership);
    }

    [Fact]
    public void RoutedInput_applies_action_and_snapshot_in_one_update()
    {
        var app = new CounterApplication();
        var snapshot = new OwnershipSnapshot(
            HoveredTarget: new ActionId(2),
            FocusedTarget: new ActionId(1),
            PressedTarget: ActionId.None,
            CapturedTarget: ActionId.None,
            LastHoverEnteredTarget: new ActionId(2),
            LastHoverLeftTarget: new ActionId(1),
            HoverChangeCount: 2,
            IsPointerPressed: false);

        var result = app.Update(app.Initialize(), new CounterMessage.RoutedInput(new CounterMessage.Increment(), snapshot));

        Assert.Equal(1, result.NextModel.Count);
        Assert.Equal(snapshot, result.NextModel.InputOwnership);
    }

    [Fact]
    public void Program_input_mapping_returns_visual_refresh_for_hover_only_change()
    {
        var ownershipState = new InputOwnershipState();
        var hitTestResolver = new DelegateActionHitTestResolver(HitIncrementAtButton);

        var mapped = Program.TryMapInputForRuntime(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            hitTestResolver,
            out var message);

        Assert.True(mapped);
        var refresh = Assert.IsType<CounterMessage.InputVisualStateChanged>(message);
        Assert.Equal(ownershipState.Snapshot, refresh.Snapshot);
        Assert.Equal(new ActionId(1), ownershipState.HoveredTarget);
    }

    [Fact]
    public void Program_input_mapping_skips_visual_refresh_when_ownership_does_not_change()
    {
        var ownershipState = new InputOwnershipState();
        var hitTestResolver = new DelegateActionHitTestResolver(HitIncrementAtButton);
        Program.TryMapInputForRuntime(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140),
            ownershipState,
            hitTestResolver,
            out _);

        var mapped = Program.TryMapInputForRuntime(
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 2, X: 32, Y: 140),
            ownershipState,
            hitTestResolver,
            out var message);

        Assert.False(mapped);
        Assert.Null(message);
    }

    [Fact]
    public void Program_input_mapping_wraps_action_with_latest_snapshot()
    {
        var ownershipState = new InputOwnershipState();
        var hitTestResolver = new DelegateActionHitTestResolver(HitIncrementAtButton);
        Program.TryMapInputForRuntime(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            hitTestResolver,
            out _);

        var mapped = Program.TryMapInputForRuntime(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 2,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            hitTestResolver,
            out var message);

        Assert.True(mapped);
        var routed = Assert.IsType<CounterMessage.RoutedInput>(message);
        Assert.IsType<CounterMessage.Increment>(routed.Action);
        Assert.Equal(ownershipState.Snapshot, routed.Snapshot);
        Assert.True(ownershipState.PressedTarget.IsNone);
        Assert.Equal(new ActionId(1), ownershipState.FocusedTarget);
    }

    [Fact]
    public void Program_input_mapping_uses_app_message_dispatch_mapper()
    {
        var ownershipState = new InputOwnershipState();
        var hitTestResolver = new DelegateActionHitTestResolver(HitIncrementAtButton);
        var dispatchMapper = new FocusResetDispatchMapper();
        Program.TryMapInputForRuntime(
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left),
            ownershipState,
            hitTestResolver,
            dispatchMapper,
            out _);

        var mapped = Program.TryMapInputForRuntime(
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 2,
                X: 500,
                Y: 500,
                Button: PointerButton.Left),
            ownershipState,
            hitTestResolver,
            dispatchMapper,
            out var message);

        var reset = Assert.IsType<CounterMessage.Reset>(message);
        Assert.True(mapped);
        Assert.Equal(1, reset.Value);
    }

    [Fact]
    public void Counter_app_message_dispatch_mapper_maps_input_intent()
    {
        var mapper = new CounterAppMessageDispatchMapper();
        var snapshot = new OwnershipSnapshot(
            HoveredTarget: new ActionId(2),
            FocusedTarget: new ActionId(1),
            PressedTarget: ActionId.None,
            CapturedTarget: ActionId.None,
            LastHoverEnteredTarget: new ActionId(2),
            LastHoverLeftTarget: new ActionId(1),
            HoverChangeCount: 2,
            IsPointerPressed: false);
        var intent = AppDispatchIntent<CounterMessage>.Input(new CounterMessage.Increment(), in snapshot);

        var mapped = mapper.TryMapIntent(in intent, out var message);

        var routed = Assert.IsType<CounterMessage.RoutedInput>(message);
        Assert.True(mapped);
        Assert.IsType<CounterMessage.Increment>(routed.Action);
        Assert.Equal(snapshot, routed.Snapshot);
    }

    [Fact]
    public void Counter_app_message_dispatch_mapper_maps_ownership_intent()
    {
        var mapper = new CounterAppMessageDispatchMapper();
        var snapshot = new OwnershipSnapshot(
            HoveredTarget: new ActionId(2),
            FocusedTarget: ActionId.None,
            PressedTarget: ActionId.None,
            CapturedTarget: ActionId.None,
            LastHoverEnteredTarget: new ActionId(2),
            LastHoverLeftTarget: ActionId.None,
            HoverChangeCount: 1,
            IsPointerPressed: false);
        var intent = AppDispatchIntent<CounterMessage>.InputOwnershipChanged(in snapshot);

        var mapped = mapper.TryMapIntent(in intent, out var message);

        var refresh = Assert.IsType<CounterMessage.InputVisualStateChanged>(message);
        Assert.True(mapped);
        Assert.Equal(snapshot, refresh.Snapshot);
    }

    [Fact]
    public void Counter_app_message_dispatch_mapper_maps_max_scroll_feedback()
    {
        var mapper = new CounterAppMessageDispatchMapper();
        var intent = AppDispatchIntent<CounterMessage>.MaxScrollFeedback(42.5);

        var mapped = mapper.TryMapIntent(in intent, out var message);

        var update = Assert.IsType<CounterMessage.UpdateMaxScrollY>(message);
        Assert.True(mapped);
        Assert.Equal(42.5, update.MaxScrollY);
    }

    [Fact]
    public void Counter_app_message_dispatch_mapper_maps_scroll_presentation_interrupt_intent()
    {
        var mapper = new CounterAppMessageDispatchMapper();
        var decision = new ScrollPresentationInterruptDecision(
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget,
            ScrollState.Default with
            {
                Position = 18,
                TargetPosition = 54,
                IsAnimating = true,
            },
            PresentedScrollY: 12,
            LogicalTargetScrollY: 54,
            AppliedDeltaPixels: 42,
            DispatchesLayoutFrame: true);
        var intent = AppDispatchIntent<CounterMessage>.ScrollPresentationInterrupted(in decision);

        var mapped = mapper.TryMapIntent(in intent, out var message);

        var interrupted = Assert.IsType<CounterMessage.ScrollPresentationInterrupted>(message);
        Assert.True(mapped);
        Assert.Equal(decision, interrupted.Decision);
    }

    [Fact]
    public void Program_feedback_mapping_uses_control_feedback_dispatch_mapper()
    {
        var feedbackMapper = new MaxScrollResetDispatchMapper();

        var mapped = Program.TryMapMaxScrollFeedbackForRuntime(42.5, feedbackMapper, out var message);

        var reset = Assert.IsType<CounterMessage.Reset>(message);
        Assert.True(mapped);
        Assert.Equal(42, reset.Value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Program_feedback_mapping_skips_invalid_max_scroll_values(double maxScrollY)
    {
        var feedbackMapper = new MaxScrollResetDispatchMapper();

        var mapped = Program.TryMapMaxScrollFeedbackForRuntime(maxScrollY, feedbackMapper, out var message);

        Assert.False(mapped);
        Assert.Null(message);
    }

    [Fact]
    public void Program_max_scroll_feedback_dispatch_accepts_first_value()
    {
        var cancelRecorder = new FeedbackCancelRecorder();
        var dispatchRecorder = new AppRuntimeDispatchRecorder();
        var dispatchSink = new RecordingAppRuntimeDispatchSink(dispatchRecorder);

        var accepted = Program.TryDispatchMaxScrollFeedbackForRuntime(
            42.5,
            lastKnownMaxScrollY: null,
            out var nextKnownMaxScrollY,
            cancelRecorder.Cancel,
            new CounterAppMessageDispatchMapper(),
            dispatchSink);

        var update = Assert.IsType<CounterMessage.UpdateMaxScrollY>(dispatchRecorder.LastMessage);
        Assert.True(accepted);
        Assert.Equal(42.5, nextKnownMaxScrollY);
        Assert.Equal(42.5, update.MaxScrollY);
        Assert.Equal(1, cancelRecorder.CancelCount);
        Assert.Equal(1, dispatchRecorder.DispatchCount);
    }

    [Theory]
    [InlineData(100.5)]
    [InlineData(99.5)]
    public void Program_max_scroll_feedback_dispatch_ignores_values_within_half_pixel_threshold(double nextMaxScrollY)
    {
        var cancelRecorder = new FeedbackCancelRecorder();
        var dispatchRecorder = new AppRuntimeDispatchRecorder();
        var dispatchSink = new RecordingAppRuntimeDispatchSink(dispatchRecorder);

        var accepted = Program.TryDispatchMaxScrollFeedbackForRuntime(
            nextMaxScrollY,
            lastKnownMaxScrollY: 100,
            out var nextKnownMaxScrollY,
            cancelRecorder.Cancel,
            new CounterAppMessageDispatchMapper(),
            dispatchSink);

        Assert.False(accepted);
        Assert.Equal(100, nextKnownMaxScrollY);
        Assert.Equal(0, cancelRecorder.CancelCount);
        Assert.Equal(0, dispatchRecorder.DispatchCount);
        Assert.Null(dispatchRecorder.LastMessage);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Program_max_scroll_feedback_dispatch_skips_invalid_values(double maxScrollY)
    {
        var cancelRecorder = new FeedbackCancelRecorder();
        var dispatchRecorder = new AppRuntimeDispatchRecorder();
        var dispatchSink = new RecordingAppRuntimeDispatchSink(dispatchRecorder);

        var accepted = Program.TryDispatchMaxScrollFeedbackForRuntime(
            maxScrollY,
            lastKnownMaxScrollY: 100,
            out var nextKnownMaxScrollY,
            cancelRecorder.Cancel,
            new CounterAppMessageDispatchMapper(),
            dispatchSink);

        Assert.False(accepted);
        Assert.Equal(100, nextKnownMaxScrollY);
        Assert.Equal(0, cancelRecorder.CancelCount);
        Assert.Equal(0, dispatchRecorder.DispatchCount);
        Assert.Null(dispatchRecorder.LastMessage);
    }

    [Theory]
    [InlineData(100.51)]
    [InlineData(99.49)]
    public void Program_max_scroll_feedback_dispatch_accepts_values_beyond_half_pixel_threshold(double nextMaxScrollY)
    {
        var cancelRecorder = new FeedbackCancelRecorder();
        var dispatchRecorder = new AppRuntimeDispatchRecorder();
        var dispatchSink = new RecordingAppRuntimeDispatchSink(dispatchRecorder);

        var accepted = Program.TryDispatchMaxScrollFeedbackForRuntime(
            nextMaxScrollY,
            lastKnownMaxScrollY: 100,
            out var nextKnownMaxScrollY,
            cancelRecorder.Cancel,
            new CounterAppMessageDispatchMapper(),
            dispatchSink);

        var update = Assert.IsType<CounterMessage.UpdateMaxScrollY>(dispatchRecorder.LastMessage);
        Assert.True(accepted);
        Assert.Equal(nextMaxScrollY, nextKnownMaxScrollY);
        Assert.Equal(nextMaxScrollY, update.MaxScrollY);
        Assert.Equal(1, cancelRecorder.CancelCount);
        Assert.Equal(1, dispatchRecorder.DispatchCount);
    }

    [Fact]
    public void Program_wheel_dispatch_uses_scroll_presentation_sink()
    {
        var recorder = new WheelDispatchRecorder();
        var sink = new RecordingWheelDispatchSink(recorder);

        var dispatched = Program.TryDispatchWheelInputForRuntime(new CounterMessage.WheelRaw(120), sink);

        Assert.True(dispatched);
        Assert.Equal(-54.0, recorder.LastPixels);
        Assert.Equal(1, recorder.DispatchCount);
    }

    [Fact]
    public void Program_wheel_dispatch_skips_zero_raw_delta()
    {
        var recorder = new WheelDispatchRecorder();
        var sink = new RecordingWheelDispatchSink(recorder);

        var dispatched = Program.TryDispatchWheelInputForRuntime(new CounterMessage.WheelRaw(0), sink);

        Assert.False(dispatched);
        Assert.Equal(0, recorder.LastPixels);
        Assert.Equal(0, recorder.DispatchCount);
    }

    [Fact]
    public async Task Poc_input_event_pump_enqueue_does_not_wait_for_blocked_dispatch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pump = new PocInputEventPump(inputEvent =>
        {
            dispatchEntered.TrySetResult();
            releaseDispatch.Task.GetAwaiter().GetResult();
        });

        Assert.True(pump.TryEnqueue(new RawInputEvent(RawInputEventKind.PointerPressed, 1, 10, 10, Button: PointerButton.Left)));
        await dispatchEntered.Task.WaitAsync(cancellationToken);

        var enqueueWait = Task.Run(
            () => pump.TryEnqueue(new RawInputEvent(RawInputEventKind.PointerWheel, 2, 10, 10, Delta: -120)),
            cancellationToken);

        Assert.True(await enqueueWait.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken));
        Assert.Equal(2, pump.EnqueuedCount);

        releaseDispatch.TrySetResult();
        await WaitForConditionAsync(() => pump.DispatchedCount == 2, cancellationToken);
        Assert.Null(pump.LastError);
    }

    [Fact]
    public async Task Poc_input_event_pump_dispatches_enqueued_events_in_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatched = new List<RawInputEventKind>();
        await using var pump = new PocInputEventPump(inputEvent => dispatched.Add(inputEvent.Kind));

        Assert.True(pump.TryEnqueue(new RawInputEvent(RawInputEventKind.PointerPressed, 1, 10, 10, Button: PointerButton.Left)));
        Assert.True(pump.TryEnqueue(new RawInputEvent(RawInputEventKind.PointerWheel, 2, 10, 10, Delta: -120)));
        Assert.True(pump.TryEnqueue(new RawInputEvent(RawInputEventKind.PointerReleased, 3, 10, 10, Button: PointerButton.Left)));

        await WaitForConditionAsync(() => pump.DispatchedCount == 3, cancellationToken);

        Assert.Equal(
            [
                RawInputEventKind.PointerPressed,
                RawInputEventKind.PointerWheel,
                RawInputEventKind.PointerReleased
            ],
            dispatched);
        Assert.Null(pump.LastError);
    }

    [Fact]
    public async Task Poc_input_event_pump_waits_for_async_dispatch_before_next_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstDispatchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondDispatchEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDispatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dispatched = new List<RawInputEventKind>();
        await using var pump = new PocInputEventPump(async inputEvent =>
        {
            dispatched.Add(inputEvent.Kind);
            if (inputEvent.Kind == RawInputEventKind.PointerPressed)
            {
                firstDispatchEntered.TrySetResult();
                await releaseFirstDispatch.Task.WaitAsync(cancellationToken);
                return;
            }

            secondDispatchEntered.TrySetResult();
        });

        Assert.True(pump.TryEnqueue(new RawInputEvent(RawInputEventKind.PointerPressed, 1, 10, 10, Button: PointerButton.Left)));
        await firstDispatchEntered.Task.WaitAsync(cancellationToken);
        Assert.True(pump.TryEnqueue(new RawInputEvent(RawInputEventKind.PointerWheel, 2, 10, 10, Delta: -120)));

        Assert.False(secondDispatchEntered.Task.IsCompleted);
        Assert.Equal(2, pump.EnqueuedCount);
        Assert.Equal(0, pump.DispatchedCount);

        releaseFirstDispatch.TrySetResult();
        await secondDispatchEntered.Task.WaitAsync(cancellationToken);
        await WaitForConditionAsync(() => pump.DispatchedCount == 2, cancellationToken);

        Assert.Equal(
            [
                RawInputEventKind.PointerPressed,
                RawInputEventKind.PointerWheel
            ],
            dispatched);
        Assert.Null(pump.LastError);
    }

    [Fact]
    public void Scroll_input_dispatch_adapter_dispatches_wheel_intent_pixels()
    {
        var recorder = new WheelDispatchRecorder();
        var sink = new RecordingWheelDispatchSink(recorder);
        var intent = WheelInputDispatchIntent.FromRawDelta(240);

        var dispatched = ScrollInputDispatchAdapter.TryDispatchIntent(in intent, sink);

        Assert.True(dispatched);
        Assert.Equal(-108.0, recorder.LastPixels);
        Assert.Equal(1, recorder.DispatchCount);
    }

    [Fact]
    public void Scroll_input_dispatch_adapter_skips_zero_wheel_intent_pixels()
    {
        var recorder = new WheelDispatchRecorder();
        var sink = new RecordingWheelDispatchSink(recorder);
        var intent = WheelInputDispatchIntent.FromRawDelta(0);

        var dispatched = ScrollInputDispatchAdapter.TryDispatchIntent(in intent, sink);

        Assert.False(dispatched);
        Assert.Equal(0, recorder.LastPixels);
        Assert.Equal(0, recorder.DispatchCount);
    }

    [Fact]
    public void Program_runtime_dispatch_uses_app_runtime_dispatch_sink()
    {
        var recorder = new AppRuntimeDispatchRecorder();
        var sink = new RecordingAppRuntimeDispatchSink(recorder);
        var message = new CounterMessage.Reset(7);

        var dispatched = Program.TryDispatchAppMessageForRuntime(message, sink);

        Assert.True(dispatched);
        Assert.Same(message, recorder.LastMessage);
        Assert.Equal(1, recorder.DispatchCount);
    }

    [Fact]
    public void Program_runtime_dispatch_skips_null_message()
    {
        var recorder = new AppRuntimeDispatchRecorder();
        var sink = new RecordingAppRuntimeDispatchSink(recorder);

        var dispatched = Program.TryDispatchAppMessageForRuntime(null, sink);

        Assert.False(dispatched);
        Assert.Null(recorder.LastMessage);
        Assert.Equal(0, recorder.DispatchCount);
    }

    [Fact]
    public void Hover_only_input_updates_model_ownership_and_button_state()
    {
        var app = new CounterApplication();
        var ownershipState = new InputOwnershipState();

        var model = ApplyRuntimeInput(
            app,
            app.Initialize(),
            ownershipState,
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140));

        Assert.Equal(new ActionId(1), model.InputOwnership.HoveredTarget);
        AssertButtonState(app.BuildView(model), isHovered: true, isPressed: false, isFocused: false);
    }

    [Fact]
    public void CounterApplication_default_view_emits_normal_button_visual_state_properties()
    {
        var app = new CounterApplication();

        AssertButtonState(app.BuildView(app.Initialize()), isHovered: false, isPressed: false, isFocused: false);
    }

    [Fact]
    public void Press_input_updates_model_ownership_and_button_state()
    {
        var app = new CounterApplication();
        var ownershipState = new InputOwnershipState();

        var model = ApplyRuntimeInput(
            app,
            app.Initialize(),
            ownershipState,
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left));

        Assert.Equal(new ActionId(1), model.InputOwnership.PressedTarget);
        Assert.Equal(new ActionId(1), model.InputOwnership.CapturedTarget);
        Assert.Equal(new ActionId(1), model.InputOwnership.FocusedTarget);
        AssertButtonState(app.BuildView(model), isHovered: true, isPressed: true, isFocused: true);
    }

    [Fact]
    public void Release_outside_applies_action_and_clears_pressed_state_in_same_model_update()
    {
        var app = new CounterApplication();
        var ownershipState = new InputOwnershipState();
        var model = ApplyRuntimeInput(
            app,
            app.Initialize(),
            ownershipState,
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left));

        model = ApplyRuntimeInput(
            app,
            model,
            ownershipState,
            new RawInputEvent(
                RawInputEventKind.PointerReleased,
                Timestamp: 2,
                X: 500,
                Y: 500,
                Button: PointerButton.Left));

        Assert.Equal(1, model.Count);
        Assert.True(model.InputOwnership.PressedTarget.IsNone);
        Assert.True(model.InputOwnership.CapturedTarget.IsNone);
        Assert.Equal(new ActionId(1), model.InputOwnership.FocusedTarget);
        AssertButtonState(app.BuildView(model), isHovered: false, isPressed: false, isFocused: true);
        AssertButtonFillColor(app.BuildView(model), DrawColor.Opaque(84, 160, 255));
    }

    [Fact]
    public void Empty_press_clears_model_focus_and_button_state()
    {
        var app = new CounterApplication();
        var ownershipState = new InputOwnershipState();
        var model = ApplyRuntimeInput(
            app,
            app.Initialize(),
            ownershipState,
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 1,
                X: 32,
                Y: 140,
                Button: PointerButton.Left));

        model = ApplyRuntimeInput(
            app,
            model,
            ownershipState,
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 500,
                Y: 500,
                Button: PointerButton.Left));

        Assert.True(model.InputOwnership.FocusedTarget.IsNone);
        Assert.True(model.InputOwnership.PressedTarget.IsNone);
        Assert.True(model.InputOwnership.CapturedTarget.IsNone);
        Assert.True(model.InputOwnership.IsPointerPressed);
        AssertButtonState(app.BuildView(model), isHovered: false, isPressed: false, isFocused: false);
    }

    [Fact]
    public void Focus_lost_clears_model_ownership_and_button_state()
    {
        var app = new CounterApplication();
        var ownershipState = new InputOwnershipState();
        var model = ApplyRuntimeInput(
            app,
            app.Initialize(),
            ownershipState,
            new RawInputEvent(RawInputEventKind.PointerMoved, Timestamp: 1, X: 32, Y: 140));
        model = ApplyRuntimeInput(
            app,
            model,
            ownershipState,
            new RawInputEvent(
                RawInputEventKind.PointerPressed,
                Timestamp: 2,
                X: 32,
                Y: 140,
                Button: PointerButton.Left));

        model = ApplyRuntimeInput(
            app,
            model,
            ownershipState,
            new RawInputEvent(RawInputEventKind.FocusLost, Timestamp: 3, X: 0, Y: 0));

        Assert.True(model.InputOwnership.HoveredTarget.IsNone);
        Assert.True(model.InputOwnership.FocusedTarget.IsNone);
        Assert.True(model.InputOwnership.PressedTarget.IsNone);
        Assert.True(model.InputOwnership.CapturedTarget.IsNone);
        Assert.False(model.InputOwnership.IsPointerPressed);
        AssertButtonState(app.BuildView(model), isHovered: false, isPressed: false, isFocused: false);
    }

    [Fact]
    public void Counter_default_view_hides_scroll_and_input_diagnostics()
    {
        var app = new CounterApplication();
        var tree = app.BuildView(app.Initialize());

        Assert.True(ContainsTextStartingWith(app._arena, tree.Root, "Count: 0"));
        Assert.True(ContainsTextStartingWith(app._arena, tree.Root, "Click a button"));
        Assert.False(ContainsTextStartingWith(app._arena, tree.Root, "ScrollY:"));
        Assert.False(ContainsTextStartingWith(app._arena, tree.Root, "Input:"));
        Assert.False(ContainsTextStartingWith(app._arena, tree.Root, "ClipMode:"));
    }

    [Fact]
    public void Counter_debug_view_shows_scroll_and_input_diagnostics()
    {
        var app = new CounterApplication(showDiagnostics: true);
        var model = app.Initialize() with
        {
            Scroll = new ScrollState { TargetPosition = 54, Position = 54 },
            InputOwnership = new OwnershipSnapshot(
                HoveredTarget: new ActionId(1),
                FocusedTarget: new ActionId(1),
                PressedTarget: ActionId.None,
                CapturedTarget: ActionId.None,
                LastHoverEnteredTarget: new ActionId(1),
                LastHoverLeftTarget: ActionId.None,
                HoverChangeCount: 1,
                IsPointerPressed: false)
        };
        var tree = app.BuildView(model);

        Assert.True(ContainsTextStartingWith(app._arena, tree.Root, "ScrollY: applied=54"));
        Assert.True(ContainsTextStartingWith(app._arena, tree.Root, "Input: hover=Increment focus=Increment pressed=- capture=- hoverChanges=1"));
        Assert.True(ContainsTextStartingWith(app._arena, tree.Root, "ClipMode: Scissor"));
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
        Assert.True(ownershipState.FocusedTarget.IsNone);
        Assert.True(ownershipState.PressedTarget.IsNone);
        Assert.True(ownershipState.CapturedTarget.IsNone);

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
        Assert.True(ownershipState.HoveredTarget.IsNone);
        Assert.True(ownershipState.FocusedTarget.IsNone);
        Assert.True(ownershipState.PressedTarget.IsNone);
        Assert.True(ownershipState.CapturedTarget.IsNone);
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

        var mapped = CounterInputRouter.TryMapInput(inputEvent, new InputOwnershipState(), static (_, _) => ActionId.None, out var message);

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

        var mapped = CounterInputRouter.TryMapInput(inputEvent, new InputOwnershipState(), static (_, _) => ActionId.None, out var message);

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

        var mapped = CounterInputRouter.TryMapInput(inputEvent, new InputOwnershipState(), static (_, _) => ActionId.None, out var message);

        var reset = Assert.IsType<CounterMessage.Reset>(message);
        Assert.True(mapped);
        Assert.Equal(0, reset.Value);
    }

    [Fact]
    public void MapActionId_throws_for_unsupported_action_id()
    {
        Assert.Throws<NotSupportedException>(() => CounterInputRouter.MapActionId(new ActionId(999)));
    }

    private static ActionId HitIncrementAtButton(int x, int y)
    {
        return x == 32 && y == 140 ? new ActionId(1) : ActionId.None;
    }

    private readonly struct IncrementButtonHitTestService : IInputHitTestService
    {
        public bool TryHitTestPhysicalPixel(int x, int y, out ActionId actionId)
        {
            actionId = HitIncrementAtButton(x, y);
            return !actionId.IsNone;
        }
    }

    private readonly struct TimestampResetActionMapper : IInputActionMapper<CounterMessage>
    {
        public bool TryMapAction(ActionId actionId, in RawInputEvent inputEvent, out CounterMessage message)
        {
            if (actionId == new ActionId(1))
            {
                message = new CounterMessage.Reset((int)inputEvent.Timestamp);
                return true;
            }

            message = null!;
            return false;
        }
    }

    private readonly struct FocusResetDispatchMapper : IAppMessageDispatchMapper<CounterMessage, CounterMessage>
    {
        public bool TryMapInputMessage(
            CounterMessage inputMessage,
            in OwnershipSnapshot ownershipSnapshot,
            out CounterMessage appMessage)
        {
            appMessage = new CounterMessage.Reset((int)ownershipSnapshot.FocusedTarget.Value);
            return true;
        }

        public bool TryMapInputOwnershipChanged(
            in OwnershipSnapshot ownershipSnapshot,
            out CounterMessage appMessage)
        {
            appMessage = new CounterMessage.Reset((int)ownershipSnapshot.HoverChangeCount);
            return true;
        }
    }

    private readonly struct MaxScrollResetDispatchMapper : IControlFeedbackDispatchMapper<CounterMessage>
    {
        public bool TryMapMaxScrollY(double maxScrollY, out CounterMessage appMessage)
        {
            appMessage = new CounterMessage.Reset((int)maxScrollY);
            return true;
        }
    }

    private sealed class FeedbackCancelRecorder
    {
        public int CancelCount { get; private set; }

        public void Cancel()
        {
            CancelCount++;
        }
    }

    private sealed class WheelDispatchRecorder
    {
        public double LastPixels { get; set; }
        public int DispatchCount { get; set; }
    }

    private readonly struct RecordingWheelDispatchSink(WheelDispatchRecorder Recorder) : IWheelInputDispatchSink
    {
        public void DispatchWheelPixels(double pixels)
        {
            Recorder.LastPixels = pixels;
            Recorder.DispatchCount++;
        }
    }

    private sealed class AppRuntimeDispatchRecorder
    {
        public CounterMessage? LastMessage { get; set; }
        public int DispatchCount { get; set; }
    }

    private readonly struct RecordingAppRuntimeDispatchSink(
        AppRuntimeDispatchRecorder Recorder) : IAppRuntimeDispatchSink<CounterMessage>
    {
        public void Dispatch(CounterMessage message)
        {
            Recorder.LastMessage = message;
            Recorder.DispatchCount++;
        }
    }

    private static CounterModel ApplyRuntimeInput(
        CounterApplication app,
        CounterModel model,
        InputOwnershipState ownershipState,
        RawInputEvent inputEvent)
    {
        var hitTestResolver = new DelegateActionHitTestResolver(HitIncrementAtButton);
        return Program.TryMapInputForRuntime(inputEvent, ownershipState, hitTestResolver, out var message) && message is not null and not CounterMessage.WheelRaw
            ? app.Update(model, message).NextModel
            : model;
    }

    private static void AssertButtonState(VirtualNodeTree tree, bool isHovered, bool isPressed, bool isFocused)
    {
        var button = FindButton(tree.Root, new ActionId(1));
        Assert.Equal(isHovered, GetBooleanProperty(button, VirtualPropertyKey.IsHovered));
        Assert.Equal(isPressed, GetBooleanProperty(button, VirtualPropertyKey.IsPressed));
        Assert.Equal(isFocused, GetBooleanProperty(button, VirtualPropertyKey.IsFocused));
    }

    private static void AssertButtonFillColor(VirtualNodeTree tree, DrawColor expectedColor)
    {
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(tree.Root, new PixelRectangle(0, 0, 960, 540), textSnapshot: tree.TextSnapshot);
        var hitTarget = Assert.Single(frame.HitTargets, target => target.ActionId == new ActionId(1));
        var commands = frame.Commands.Memory.Span;

        for (var i = 0; i < frame.Commands.Count; i++)
        {
            var command = commands[i];
            if (command.Kind == DrawCommandKind.FillRect && command.Rect == new DrawRect(hitTarget.Bounds.X, hitTarget.Bounds.Y, hitTarget.Bounds.Width, hitTarget.Bounds.Height))
            {
                Assert.Equal(expectedColor, command.Color);
                return;
            }
        }

        Assert.Fail("Increment button fill command was not recorded.");
    }

    private static VirtualNode FindButton(VirtualNode node, ActionId actionId)
    {
        if (node.Kind == VirtualNodeKind.Button && GetActionId(node) == actionId)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindButton(child, actionId);
            if (found.Kind == VirtualNodeKind.Button)
            {
                return found;
            }
        }

        return default;
    }

    private static ActionId GetActionId(VirtualNode node)
    {
        foreach (var property in node.Properties)
        {
            if (property.Key == VirtualPropertyKey.ActionId)
            {
                return property.Value.GetRequiredActionId();
            }
        }

        return ActionId.None;
    }

    private static bool GetBooleanProperty(VirtualNode node, VirtualPropertyKey key)
    {
        foreach (var property in node.Properties)
        {
            if (property.Key == key)
            {
                return property.Value.GetRequiredBoolean();
            }
        }

        return false;
    }

    private static bool ContainsProperty(ReadOnlySpan<VirtualNodeProperty> properties, Func<VirtualNodeProperty, bool> predicate)
    {
        foreach (var property in properties)
        {
            if (predicate(property))
            {
                return true;
            }
        }

        return false;
    }

    private static bool GetBooleanProperty(VirtualNodeProperty[] properties, VirtualPropertyKey key)
    {
        foreach (var property in properties)
        {
            if (property.Key == key)
            {
                return property.Value.GetRequiredBoolean();
            }
        }

        return false;
    }

    private static ActionId GetActionId(VirtualNodeProperty[] properties)
    {
        foreach (var property in properties)
        {
            if (property.Key == VirtualPropertyKey.ActionId)
            {
                return property.Value.GetRequiredActionId();
            }
        }

        return ActionId.None;
    }

    private static bool ContainsTextStartingWith(VirtualTextArena arena, VirtualNode node, string prefix)
    {
        if (node.Kind == VirtualNodeKind.Text && ResolveNodeText(arena, node.Content)?.StartsWith(prefix, StringComparison.Ordinal) == true)
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (ContainsTextStartingWith(arena, child, prefix))
            {
                return true;
            }
        }

        return false;
    }

    private static ActionId HitIncrementOrDecrement(int x, int y)
    {
        return (x, y) switch
        {
            (32, 140) => new ActionId(1),
            (32, 200) => new ActionId(2),
            _ => ActionId.None
        };
    }

    [Fact]
    public void Scroll_message_updates_model_target()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // One full downward notch: -120 units �?120/120 * 3 lines * 18px = 54px
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

        // Scroll up when at 0 �?target stays at 0
        var delta = new ScrollDelta(ScrollDeltaUnit.WheelRaw, 120);
        var result = app.Update(model, new CounterMessage.ScrollFrame(delta, 0));
        Assert.Equal(0, result.NextModel.Scroll.TargetPosition);
    }

    [Fact]
    public void Scroll_small_deltas_accumulate()
    {
        var app = new CounterApplication();
        var model = app.Initialize();

        // 4 small downward deltas of -30 = -120 total �?54px (same as one notch)
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
    public void ScrollY_appears_in_view_as_property()
    {
        var app = new CounterApplication();
        var model = app.Initialize() with { Scroll = new ScrollState { TargetPosition = 80, Position = 80 } };
        var tree = app.BuildView(model);

        // The root ScrollContainer should have ScrollY=80 property
        Assert.Equal(VirtualNodeKind.ScrollContainer, tree.Root.Kind);
        var scrollYProperty = default(VirtualNodeProperty);
        foreach (var property in tree.Root.Properties)
        {
            if (property.Key == VirtualPropertyKey.ScrollY)
            {
                scrollYProperty = property;
                break;
            }
        }
        Assert.Equal(VirtualPropertyKey.ScrollY, scrollYProperty.Key);
        Assert.Equal(80.0, scrollYProperty.Value.GetRequiredNumber());
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
    public void ConvertToPixels_invalid_delta_values_return_zero()
    {
        Assert.Equal(0, ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.Line, double.NaN),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default));
        Assert.Equal(0, ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.Pixel, double.PositiveInfinity),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default));
        Assert.Equal(0, ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.Page, double.NegativeInfinity),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default));
        Assert.Equal(0, ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, double.NaN),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default));
    }

    [Fact]
    public void ConvertToPixels_invalid_metrics_and_settings_use_defaults()
    {
        var metrics = new ScrollMetrics(
            LineExtent: double.NaN,
            PageExtent: double.NaN,
            ViewportExtent: double.PositiveInfinity,
            ContentExtent: double.NaN);
        var settings = new SystemScrollSettings(LinesPerWheelNotch: 0, WheelUnitsPerNotch: 0);

        var wheelPixels = ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120),
            metrics,
            settings);
        var pagePixels = ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.Page, 1),
            metrics,
            settings);

        Assert.Equal(54, wheelPixels);
        Assert.Equal(180, pagePixels);
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
        // doesn't clamp �?it's the layout builder's job. So target is just large.
        Assert.True(state.TargetPosition > 0);
        Assert.True(state.IsAnimating);
    }

    [Fact]
    public void ApplyScrollDelta_recovers_from_non_finite_state_before_applying_delta()
    {
        var state = new ScrollState
        {
            Accumulator = double.NaN,
            TargetPosition = double.PositiveInfinity,
            Position = double.NaN,
            IsAnimating = true,
        };

        var next = ScrollController.ApplyScrollDelta(
            state,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 12),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);

        AssertFiniteScrollState(next);
        Assert.Equal(0, next.Accumulator);
        Assert.Equal(0, next.Position);
        Assert.Equal(12, next.TargetPosition);
        Assert.True(next.IsAnimating);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ApplyScrollDelta_ignores_non_finite_pixel_delta(double delta)
    {
        var state = new ScrollState { Position = 12, TargetPosition = 54, Accumulator = 0.5, IsAnimating = true };

        var next = ScrollController.ApplyScrollDelta(
            state,
            new ScrollDelta(ScrollDeltaUnit.Pixel, delta),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);

        AssertFiniteScrollState(next);
        Assert.Equal(12, next.Position);
        Assert.Equal(54, next.TargetPosition);
        Assert.Equal(0.5, next.Accumulator);
        Assert.True(next.IsAnimating);
    }

    [Fact]
    public void ApplyScrollDelta_wheel_raw_uses_default_settings()
    {
        var state = ScrollController.ApplyScrollDelta(
            ScrollState.Default,
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, -120),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);
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

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void ScrollController_tick_ignores_invalid_delta_time(double deltaTime)
    {
        var state = new ScrollState { Position = 12, TargetPosition = 54, IsAnimating = true };

        var next = ScrollController.Tick(state, deltaTime);

        AssertFiniteScrollState(next);
        Assert.Equal(12, next.Position);
        Assert.Equal(54, next.TargetPosition);
        Assert.True(next.IsAnimating);
    }

    [Fact]
    public void ScrollController_tick_normalizes_corrupt_state_before_advancing()
    {
        var state = new ScrollState
        {
            Position = double.NaN,
            TargetPosition = 54,
            Accumulator = double.PositiveInfinity,
            IsAnimating = true,
        };

        var next = ScrollController.Tick(state, 1.0 / 60.0);

        AssertFiniteScrollState(next);
        Assert.Equal(0, next.Accumulator);
        Assert.True(next.Position > 0);
        Assert.Equal(54, next.TargetPosition);
        Assert.True(next.IsAnimating);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.MaxValue, int.MaxValue)]
    public void GetScrollY_normalizes_invalid_or_large_positions(double position, int expected)
    {
        var state = new ScrollState { Position = position, TargetPosition = position };

        var scrollY = ScrollController.GetScrollY(state);

        Assert.Equal(expected, scrollY);
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

        // Update MaxScrollY to 100 �?should clamp target
        model = app.Update(model, new CounterMessage.UpdateMaxScrollY(100)).NextModel;
        Assert.Equal(100.0, model.Scroll.TargetPosition);
    }

    [Fact]
    public void Debug_display_contains_target_and_position()
    {
        var app = new CounterApplication(showDiagnostics: true);
        var model = app.Initialize() with
        {
            Scroll = new ScrollState { TargetPosition = 100, Position = 50, Accumulator = 0.5 }
        };
        var tree = app.BuildView(model);

        // The second text child should contain the debug display
        var debugText = ResolveNodeText(app._arena, tree.Root.Children[1].Content);
        Assert.Contains("applied=", debugText);
        Assert.Contains("target=", debugText);
        Assert.Contains("pos=", debugText);
        Assert.Contains("acc=", debugText);
    }

    [Fact]
    public void Debug_display_distinguishes_unknown_and_known_zero_max_scroll()
    {
        var app = new CounterApplication(showDiagnostics: true);
        var model = app.Initialize();

        var debugText = ResolveNodeText(app._arena, app.BuildView(model).Root.Children[1].Content);
        Assert.Contains("max=unknown", debugText);

        model = app.Update(model, new CounterMessage.UpdateMaxScrollY(0)).NextModel;
        debugText = ResolveNodeText(app._arena, app.BuildView(model).Root.Children[1].Content);

        Assert.Contains("max=0(known-zero)", debugText);
    }

    [Fact]
    public void ScrollPresentationInterrupt_commit_presented_value_to_runtime_state()
    {
        var state = new ScrollState { Position = 120, TargetPosition = 180, IsAnimating = true, MaxScrollY = 240, HasMaxScrollY = true };

        var next = ScrollController.CommitPresented(state, 132);

        Assert.Equal(132, next.Position);
        Assert.Equal(132, next.TargetPosition);
        Assert.False(next.IsAnimating);
        Assert.Equal(0, next.Accumulator);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ScrollPresentationInterrupt_commit_invalid_presented_value_normalizes_to_zero(double presentedScrollY)
    {
        var state = new ScrollState { Position = 120, TargetPosition = 180, IsAnimating = true, MaxScrollY = 240, HasMaxScrollY = true };

        var next = ScrollController.CommitPresented(state, presentedScrollY);

        AssertFiniteScrollState(next);
        Assert.Equal(0, next.Position);
        Assert.Equal(0, next.TargetPosition);
        Assert.False(next.IsAnimating);
        Assert.Equal(0, next.Accumulator);
    }

    [Fact]
    public void ScrollPresentationInterrupt_cancel_returns_to_logical_target()
    {
        var state = new ScrollState { Position = 120, TargetPosition = 180, IsAnimating = true, Accumulator = 0.5, MaxScrollY = 240, HasMaxScrollY = true };

        var next = ScrollController.CancelPresentation(state);

        Assert.Equal(180, next.Position);
        Assert.Equal(180, next.TargetPosition);
        Assert.False(next.IsAnimating);
        Assert.Equal(0, next.Accumulator);
    }

    [Fact]
    public void ScrollPresentationInterrupt_retarget_uses_presented_value_as_visual_origin_and_preserves_logical_target()
    {
        var state = new ScrollState { Position = 120, TargetPosition = 180, IsAnimating = true, MaxScrollY = 240, HasMaxScrollY = true };

        var next = ScrollController.RetargetFromPresentedToLogicalTarget(
            state,
            132,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 54),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);

        Assert.Equal(132, next.Position);
        Assert.Equal(234, next.TargetPosition);
        Assert.True(next.IsAnimating);
    }

    [Fact]
    public void ScrollPresentationInterrupt_retarget_accumulates_repeated_deltas_against_logical_target()
    {
        var state = new ScrollState { Position = 54, TargetPosition = 54, MaxScrollY = 240, HasMaxScrollY = true };

        var next = ScrollController.RetargetFromPresentedToLogicalTarget(
            state,
            18,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 54),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);

        Assert.Equal(18, next.Position);
        Assert.Equal(108, next.TargetPosition);
        Assert.True(next.IsAnimating);
    }

    [Fact]
    public void ScrollPresentationInterrupt_retarget_clamps_to_known_max()
    {
        var state = new ScrollState { Position = 120, TargetPosition = 180, IsAnimating = true, MaxScrollY = 160, HasMaxScrollY = true };

        var next = ScrollController.RetargetFromPresentedToLogicalTarget(
            state,
            150,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 54),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);

        Assert.Equal(150, next.Position);
        Assert.Equal(160, next.TargetPosition);
        Assert.True(next.IsAnimating);
    }

    [Fact]
    public void ScrollPresentationInterrupt_retarget_ignores_invalid_delta_without_poisoning_state()
    {
        var state = new ScrollState { Position = 120, TargetPosition = 180, IsAnimating = true, MaxScrollY = 240, HasMaxScrollY = true };

        var next = ScrollController.RetargetFromPresentedToLogicalTarget(
            state,
            double.NaN,
            new ScrollDelta(ScrollDeltaUnit.Pixel, double.NaN),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);

        AssertFiniteScrollState(next);
        Assert.Equal(0, next.Position);
        Assert.Equal(180, next.TargetPosition);
        Assert.True(next.IsAnimating);
    }

    [Fact]
    public void ScrollPresentationCoordinator_boundary_delta_does_not_restart_same_target_segment()
    {
        var state = new ScrollState { Position = 240, TargetPosition = 240, MaxScrollY = 240, HasMaxScrollY = true };
        var decision = ScrollController.ResolvePresentationInterrupt(
            state,
            210,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 54),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);

        Assert.Equal(210, decision.NextState.Position);
        Assert.Equal(240, decision.NextState.TargetPosition);
        Assert.True(decision.NextState.IsAnimating);
        Assert.False(ScrollPresentationCoordinator.ShouldStartRetargetSegment(state, decision));
    }

    [Fact]
    public void ScrollPresentationCoordinator_first_delta_to_boundary_starts_target_segment()
    {
        var state = new ScrollState { Position = 186, TargetPosition = 186, MaxScrollY = 240, HasMaxScrollY = true };
        var decision = ScrollController.ResolvePresentationInterrupt(
            state,
            186,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 54),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);

        Assert.Equal(240, decision.NextState.TargetPosition);
        Assert.True(ScrollPresentationCoordinator.ShouldStartRetargetSegment(state, decision));
    }

    [Fact]
    public void ScrollPresentationCoordinator_top_boundary_delta_does_not_restart_same_target_segment()
    {
        var state = new ScrollState { Position = 0, TargetPosition = 0, MaxScrollY = 240, HasMaxScrollY = true };
        var decision = ScrollController.ResolvePresentationInterrupt(
            state,
            30,
            new ScrollDelta(ScrollDeltaUnit.Pixel, -54),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);

        Assert.Equal(30, decision.NextState.Position);
        Assert.Equal(0, decision.NextState.TargetPosition);
        Assert.True(decision.NextState.IsAnimating);
        Assert.False(ScrollPresentationCoordinator.ShouldStartRetargetSegment(state, decision));
    }

    [Fact]
    public void ScrollPresentationCoordinator_first_delta_to_top_boundary_starts_target_segment()
    {
        var state = new ScrollState { Position = 54, TargetPosition = 54, MaxScrollY = 240, HasMaxScrollY = true };
        var decision = ScrollController.ResolvePresentationInterrupt(
            state,
            54,
            new ScrollDelta(ScrollDeltaUnit.Pixel, -54),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);

        Assert.Equal(0, decision.NextState.TargetPosition);
        Assert.True(ScrollPresentationCoordinator.ShouldStartRetargetSegment(state, decision));
    }

    [Theory]
    [InlineData(240, 54)]
    [InlineData(0, -54)]
    public async Task ScrollPresentationCoordinator_boundary_delta_does_not_sample_or_restart_same_target_segment(
        double boundaryPosition,
        double pendingPixels)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = new ScrollPresentationCoordinator();
        var runtime = new RecordingScrollPresentationRuntimeAdapter(
            new ScrollState
            {
                Position = boundaryPosition,
                TargetPosition = boundaryPosition,
                MaxScrollY = 240,
                HasMaxScrollY = true
            });
        var compositor = new RecordingScrollPresentationCompositorAdapter();
        var snapshotProvider = new FixedScrollPresentationSnapshotProvider(BuildScrollSnapshot());

        coordinator.AddPendingPixels(pendingPixels);
        await coordinator.RunUntilIdleAsync(runtime, compositor, snapshotProvider, new NodeKey(1), cancellationToken);

        Assert.Empty(runtime.Decisions);
        Assert.Equal(0, compositor.SampleForRetargetCount);
        Assert.Empty(compositor.Declarations);
        Assert.Equal(0, coordinator.RetargetCount);
        Assert.Equal(0, coordinator.PendingPixels);
        Assert.Equal(boundaryPosition, runtime.CurrentScroll.Position);
        Assert.Equal(boundaryPosition, runtime.CurrentScroll.TargetPosition);
    }

    [Fact]
    public async Task ScrollPresentationCoordinator_ignores_zero_and_non_finite_pending_pixels()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = new ScrollPresentationCoordinator();
        var runtime = new RecordingScrollPresentationRuntimeAdapter(
            new ScrollState
            {
                Position = 0,
                TargetPosition = 0,
                MaxScrollY = 240,
                HasMaxScrollY = true
            });
        var compositor = new RecordingScrollPresentationCompositorAdapter();
        var snapshotProvider = new FixedScrollPresentationSnapshotProvider(BuildScrollSnapshot());

        coordinator.AddPendingPixels(0);
        coordinator.AddPendingPixels(double.NaN);
        coordinator.AddPendingPixels(double.PositiveInfinity);
        coordinator.AddPendingPixels(double.NegativeInfinity);
        await coordinator.RunUntilIdleAsync(runtime, compositor, snapshotProvider, new NodeKey(1), cancellationToken);

        Assert.Empty(runtime.Decisions);
        Assert.Equal(0, compositor.SampleForRetargetCount);
        Assert.Empty(compositor.Declarations);
        Assert.Equal(0, coordinator.RetargetCount);
        Assert.Equal(0, coordinator.PendingPixels);
        Assert.Equal(0, runtime.CurrentScroll.Position);
        Assert.Equal(0, runtime.CurrentScroll.TargetPosition);
    }

    [Fact]
    public async Task ScrollPresentationCoordinator_coalesces_counteracting_pending_pixels_before_sampling()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = new ScrollPresentationCoordinator();
        var runtime = new RecordingScrollPresentationRuntimeAdapter(
            new ScrollState
            {
                Position = 24,
                TargetPosition = 24,
                MaxScrollY = 240,
                HasMaxScrollY = true
            });
        var compositor = new RecordingScrollPresentationCompositorAdapter(new ScrollPresentationSample(true, 12));
        var snapshotProvider = new FixedScrollPresentationSnapshotProvider(BuildScrollSnapshot());

        coordinator.AddPendingPixels(54);
        coordinator.AddPendingPixels(-54);
        await coordinator.RunUntilIdleAsync(runtime, compositor, snapshotProvider, new NodeKey(1), cancellationToken);

        Assert.Empty(runtime.Decisions);
        Assert.Equal(0, compositor.SampleForRetargetCount);
        Assert.Empty(compositor.Declarations);
        Assert.Equal(0, coordinator.RetargetCount);
        Assert.Equal(0, coordinator.PendingPixels);
        Assert.Equal(24, runtime.CurrentScroll.Position);
        Assert.Equal(24, runtime.CurrentScroll.TargetPosition);
    }

    [Fact]
    public async Task ScrollPresentationCoordinator_replacement_segment_starts_from_sampled_presented_value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = new ScrollPresentationCoordinator();
        var runtime = new RecordingScrollPresentationRuntimeAdapter(
            new ScrollState { Position = 0, TargetPosition = 0, MaxScrollY = 240, HasMaxScrollY = true });
        var compositor = new RecordingScrollPresentationCompositorAdapter(
            new ScrollPresentationSample(false, 0),
            new ScrollPresentationSample(true, 18));
        var snapshotProvider = new FixedScrollPresentationSnapshotProvider(BuildScrollSnapshot());
        var scrollTargetKey = new NodeKey(1);

        coordinator.AddPendingPixels(54);
        await coordinator.RunUntilIdleAsync(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);
        coordinator.AddPendingPixels(54);
        await coordinator.RunUntilIdleAsync(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);

        Assert.Equal(2, compositor.SampleForRetargetCount);
        Assert.Equal(2, compositor.Declarations.Count);
        Assert.Equal(0, compositor.Declarations[0].PresentedScrollY.From);
        Assert.Equal(54, compositor.Declarations[0].PresentedScrollY.To);
        Assert.Equal(18, compositor.Declarations[1].PresentedScrollY.From);
        Assert.Equal(108, compositor.Declarations[1].PresentedScrollY.To);
        Assert.Equal(108, runtime.CurrentScroll.Position);
        Assert.Equal(108, runtime.CurrentScroll.TargetPosition);
        Assert.Equal(2, coordinator.RetargetCount);
    }

    [Fact]
    public async Task ScrollPresentationCoordinator_retarget_segment_samples_injected_composition_clock_source()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var segmentStart = CompositionTimestamp.FromStopwatchTicks(777);
        var coordinator = new ScrollPresentationCoordinator(new FixedCompositionClockSource(segmentStart));
        var runtime = new RecordingScrollPresentationRuntimeAdapter(
            new ScrollState { Position = 0, TargetPosition = 0, MaxScrollY = 240, HasMaxScrollY = true });
        var compositor = new RecordingScrollPresentationCompositorAdapter(new ScrollPresentationSample(false, 0));
        var snapshotProvider = new FixedScrollPresentationSnapshotProvider(BuildScrollSnapshot());
        var scrollTargetKey = new NodeKey(1);

        coordinator.AddPendingPixels(54);
        await coordinator.RunUntilIdleAsync(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);

        Assert.Single(compositor.Declarations);
        Assert.Equal(segmentStart, compositor.Declarations[0].Timeline.StartTimestamp);
        Assert.Equal(0, compositor.Declarations[0].PresentedScrollY.From);
        Assert.Equal(54, compositor.Declarations[0].PresentedScrollY.To);
        Assert.Equal(54, runtime.CurrentScroll.Position);
        Assert.Equal(54, runtime.CurrentScroll.TargetPosition);
        Assert.Equal(1, coordinator.RetargetCount);
    }

    [Fact]
    public async Task ScrollPresentationCoordinator_active_loop_drains_pending_pixels_after_ensure_running_returns_false()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = new ScrollPresentationCoordinator();
        var runtime = new RecordingScrollPresentationRuntimeAdapter(
            new ScrollState { Position = 0, TargetPosition = 0, MaxScrollY = 240, HasMaxScrollY = true });
        var compositor = new GatedStartScrollPresentationCompositorAdapter(
            new ScrollPresentationSample(false, 0),
            new ScrollPresentationSample(true, 18));
        var snapshotProvider = new FixedScrollPresentationSnapshotProvider(BuildScrollSnapshot());
        var scrollTargetKey = new NodeKey(1);

        coordinator.AddPendingPixels(54);
        var firstStarted = coordinator.EnsureRunning(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);
        await compositor.WaitForFirstStartAsync(cancellationToken);

        coordinator.AddPendingPixels(54);
        var secondStarted = coordinator.EnsureRunning(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);
        compositor.AllowFirstStart();
        await WaitForConditionAsync(() => !coordinator.IsLoopRunning, cancellationToken);

        Assert.True(firstStarted);
        Assert.False(secondStarted);
        Assert.Equal(0, coordinator.PendingPixels);
        Assert.Equal(2, compositor.SampleForRetargetCount);
        Assert.Equal(2, compositor.Declarations.Count);
        Assert.Equal(0, compositor.Declarations[0].PresentedScrollY.From);
        Assert.Equal(54, compositor.Declarations[0].PresentedScrollY.To);
        Assert.Equal(18, compositor.Declarations[1].PresentedScrollY.From);
        Assert.Equal(108, compositor.Declarations[1].PresentedScrollY.To);
        Assert.Equal(108, runtime.CurrentScroll.Position);
        Assert.Equal(108, runtime.CurrentScroll.TargetPosition);
        Assert.Equal(2, coordinator.RetargetCount);
    }

    [Fact]
    public async Task ScrollPresentationCoordinator_active_loop_retargets_reverse_input_from_sampled_presented_value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = new ScrollPresentationCoordinator();
        var runtime = new RecordingScrollPresentationRuntimeAdapter(
            new ScrollState { Position = 0, TargetPosition = 0, MaxScrollY = 240, HasMaxScrollY = true });
        var compositor = new GatedStartScrollPresentationCompositorAdapter(
            new ScrollPresentationSample(false, 0),
            new ScrollPresentationSample(true, 42));
        var snapshotProvider = new FixedScrollPresentationSnapshotProvider(BuildScrollSnapshot());
        var scrollTargetKey = new NodeKey(1);

        coordinator.AddPendingPixels(108);
        var firstStarted = coordinator.EnsureRunning(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);
        await compositor.WaitForFirstStartAsync(cancellationToken);

        coordinator.AddPendingPixels(-54);
        var secondStarted = coordinator.EnsureRunning(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);
        compositor.AllowFirstStart();
        await WaitForConditionAsync(() => !coordinator.IsLoopRunning, cancellationToken);

        Assert.True(firstStarted);
        Assert.False(secondStarted);
        Assert.Equal(0, coordinator.PendingPixels);
        Assert.Equal(2, compositor.SampleForRetargetCount);
        Assert.Equal(2, compositor.Declarations.Count);
        Assert.Equal(0, compositor.Declarations[0].PresentedScrollY.From);
        Assert.Equal(108, compositor.Declarations[0].PresentedScrollY.To);
        Assert.Equal(42, compositor.Declarations[1].PresentedScrollY.From);
        Assert.Equal(54, compositor.Declarations[1].PresentedScrollY.To);
        Assert.Equal(54, runtime.CurrentScroll.Position);
        Assert.Equal(54, runtime.CurrentScroll.TargetPosition);
        Assert.Equal(2, coordinator.RetargetCount);
    }

    [Fact]
    public async Task ScrollPresentationCoordinator_active_loop_does_not_restart_boundary_overscroll_after_ensure_running_returns_false()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var coordinator = new ScrollPresentationCoordinator();
        var runtime = new RecordingScrollPresentationRuntimeAdapter(
            new ScrollState { Position = 186, TargetPosition = 186, MaxScrollY = 240, HasMaxScrollY = true });
        var compositor = new GatedStartScrollPresentationCompositorAdapter(new ScrollPresentationSample(false, 0));
        var snapshotProvider = new FixedScrollPresentationSnapshotProvider(BuildScrollSnapshot());
        var scrollTargetKey = new NodeKey(1);

        coordinator.AddPendingPixels(54);
        var firstStarted = coordinator.EnsureRunning(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);
        await compositor.WaitForFirstStartAsync(cancellationToken);

        coordinator.AddPendingPixels(54);
        var secondStarted = coordinator.EnsureRunning(runtime, compositor, snapshotProvider, scrollTargetKey, cancellationToken);
        compositor.AllowFirstStart();
        await WaitForConditionAsync(() => !coordinator.IsLoopRunning, cancellationToken);

        Assert.True(firstStarted);
        Assert.False(secondStarted);
        Assert.Equal(0, coordinator.PendingPixels);
        Assert.Equal(1, compositor.SampleForRetargetCount);
        Assert.Single(compositor.Declarations);
        Assert.Equal(186, compositor.Declarations[0].PresentedScrollY.From);
        Assert.Equal(240, compositor.Declarations[0].PresentedScrollY.To);
        Assert.Equal(240, runtime.CurrentScroll.Position);
        Assert.Equal(240, runtime.CurrentScroll.TargetPosition);
        Assert.Equal(1, coordinator.RetargetCount);
    }

    [Fact]
    public void ScrollPresentationInterrupted_message_applies_policy_state()
    {
        var app = new CounterApplication();
        var model = app.Initialize() with
        {
            Scroll = new ScrollState { Position = 120, TargetPosition = 180, IsAnimating = true, MaxScrollY = 240, HasMaxScrollY = true }
        };
        var decision = ScrollController.ResolvePresentationInterrupt(
            model.Scroll,
            132,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 54),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default,
            ScrollPresentationInterruptPolicy.RetargetFromPresentedToLogicalTarget);

        model = app.Update(model, new CounterMessage.ScrollPresentationInterrupted(decision)).NextModel;

        Assert.Equal(132, model.Scroll.Position);
        Assert.Equal(234, model.Scroll.TargetPosition);
        Assert.True(model.Scroll.IsAnimating);
    }

    // ── HasMaxScrollY tests ────────────────────────────────────────────

    [Fact]
    public void HasMaxScrollY_false_target_not_clamped()
    {
        // Default ScrollState has HasMaxScrollY=false �?target should grow freely
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
    public void ApplyScrollDelta_at_known_bottom_discards_same_direction_boundary_delta()
    {
        var state = new ScrollState { Position = 100, TargetPosition = 100, MaxScrollY = 100, HasMaxScrollY = true };

        var next = ScrollController.ApplyScrollDelta(
            state,
            new ScrollDelta(ScrollDeltaUnit.Pixel, 54.5),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);

        Assert.Equal(100, next.Position);
        Assert.Equal(100, next.TargetPosition);
        Assert.Equal(0, next.Accumulator);
        Assert.False(next.IsAnimating);
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

        // Set MaxScrollY=0 (content fits in viewport �?no scrolling needed)
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

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void HasMaxScrollY_withMaxScrollY_invalid_values_normalize_to_zero(double maxScrollY)
    {
        var state = new ScrollState { Position = 10, TargetPosition = 12, IsAnimating = true };

        state = ScrollController.WithMaxScrollY(state, maxScrollY);

        Assert.True(state.HasMaxScrollY);
        Assert.Equal(0, state.MaxScrollY);
        Assert.Equal(0, state.Position);
        Assert.Equal(0, state.TargetPosition);
        Assert.False(state.IsAnimating);
    }

    [Fact]
    public void HasMaxScrollY_withMaxScrollY_uses_new_valid_max_when_previous_max_is_invalid()
    {
        var state = new ScrollState
        {
            Position = 50,
            TargetPosition = 80,
            MaxScrollY = double.NaN,
            HasMaxScrollY = true,
            IsAnimating = true,
        };

        state = ScrollController.WithMaxScrollY(state, 100);

        Assert.True(state.HasMaxScrollY);
        Assert.Equal(100, state.MaxScrollY);
        Assert.Equal(50, state.Position);
        Assert.Equal(80, state.TargetPosition);
        Assert.True(state.IsAnimating);
    }

    // ── Coalescing / rapid input tests ─────────────────────────────────

    [Fact]
    public void Rapid_100_deltas_target_correct()
    {
        // Simulate rapid downward scrolling: 100 small deltas of -30 units each
        // 100 × -30 = -3000 total �?3000/120 × 3 × 18 = 1350px
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
        // Position should NOT have moved yet (dt=0 �?Tick is a no-op since factor=0)
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

    private RenderPipelineRetainedInputSnapshot BuildScrollSnapshot()
    {
        var pipeline = new RenderPipeline();
        using var frame = pipeline.Build(
            new VirtualNode(
                VirtualNodeKind.ScrollContainer,
                key: 1,
                properties: [VirtualNodeProperty.Height(60), VirtualNodeProperty.ScrollY(0)],
                children: [VirtualNodeBuilder.Text(_arena, "Item", new NodeKey(2))]),
            new PixelRectangle(0, 0, 240, 120),
            _arena.GetOrCreateSnapshot());

        return pipeline.LastRetainedInputSnapshot!;
    }

    private sealed class RecordingScrollPresentationRuntimeAdapter(ScrollState currentScroll) : IScrollPresentationRuntimeAdapter
    {
        private ScrollState _currentScroll = currentScroll;

        public ScrollState CurrentScroll => _currentScroll;

        public List<ScrollPresentationInterruptDecision> Decisions { get; } = [];

        public Task DispatchScrollPresentationInterruptedAsync(
            ScrollPresentationInterruptDecision decision,
            CancellationToken cancellationToken = default)
        {
            Decisions.Add(decision);
            _currentScroll = decision.NextState;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingScrollPresentationCompositorAdapter : IScrollPresentationCompositorAdapter
    {
        private readonly Queue<ScrollPresentationSample> _samples;
        private bool _hasPresentedScrollY;
        private double _presentedScrollY;

        public RecordingScrollPresentationCompositorAdapter(params ScrollPresentationSample[] samples)
        {
            _samples = new Queue<ScrollPresentationSample>(samples);
        }

        public int SampleForRetargetCount { get; private set; }

        public List<CompositionScrollPresentationDeclaration> Declarations { get; } = [];

        public ValueTask<ScrollPresentationSample> SampleForRetargetAsync(
            NodeKey targetKey,
            CancellationToken cancellationToken = default)
        {
            SampleForRetargetCount++;
            return ValueTask.FromResult(_samples.Count > 0
                ? _samples.Dequeue()
                : new ScrollPresentationSample(false, 0));
        }

        public ValueTask StartAsync(
            in CompositionScrollPresentationDeclaration declaration,
            RenderPipelineRetainedInputSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            Declarations.Add(declaration);
            _hasPresentedScrollY = true;
            _presentedScrollY = declaration.PresentedScrollY.From;
            return ValueTask.CompletedTask;
        }

        public bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
        {
            presentedScrollY = _presentedScrollY;
            return _hasPresentedScrollY;
        }
    }

    private sealed class GatedStartScrollPresentationCompositorAdapter : IScrollPresentationCompositorAdapter
    {
        private readonly Queue<ScrollPresentationSample> _samples;
        private readonly TaskCompletionSource _firstStartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allowFirstStart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _hasPresentedScrollY;
        private double _presentedScrollY;

        public GatedStartScrollPresentationCompositorAdapter(params ScrollPresentationSample[] samples)
        {
            _samples = new Queue<ScrollPresentationSample>(samples);
        }

        public int SampleForRetargetCount { get; private set; }

        public List<CompositionScrollPresentationDeclaration> Declarations { get; } = [];

        public Task WaitForFirstStartAsync(CancellationToken cancellationToken)
        {
            return _firstStartEntered.Task.WaitAsync(cancellationToken);
        }

        public void AllowFirstStart()
        {
            _allowFirstStart.TrySetResult();
        }

        public ValueTask<ScrollPresentationSample> SampleForRetargetAsync(
            NodeKey targetKey,
            CancellationToken cancellationToken = default)
        {
            SampleForRetargetCount++;
            return ValueTask.FromResult(_samples.Count > 0
                ? _samples.Dequeue()
                : new ScrollPresentationSample(false, 0));
        }

        public ValueTask StartAsync(
            in CompositionScrollPresentationDeclaration declaration,
            RenderPipelineRetainedInputSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            return StartCoreAsync(declaration, cancellationToken);
        }

        private async ValueTask StartCoreAsync(
            CompositionScrollPresentationDeclaration declaration,
            CancellationToken cancellationToken)
        {
            Declarations.Add(declaration);
            _hasPresentedScrollY = true;
            _presentedScrollY = declaration.PresentedScrollY.From;

            if (Declarations.Count == 1)
            {
                _firstStartEntered.TrySetResult();
                await _allowFirstStart.Task.WaitAsync(cancellationToken);
            }
        }

        public bool TryGetPresentedScrollY(NodeKey targetKey, out double presentedScrollY)
        {
            presentedScrollY = _presentedScrollY;
            return _hasPresentedScrollY;
        }
    }

    private sealed class FixedCompositionClockSource(CompositionTimestamp timestamp) : ICompositionClockSource
    {
        public CompositionTimestamp TimestampNow() => timestamp;
    }

    private sealed class FixedScrollPresentationSnapshotProvider(RenderPipelineRetainedInputSnapshot snapshot) : IScrollPresentationRetainedSnapshotProvider
    {
        public RenderPipelineRetainedInputSnapshot? LastRetainedInputSnapshot => snapshot;
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private static void AssertFiniteScrollState(ScrollState state)
    {
        Assert.True(double.IsFinite(state.Accumulator));
        Assert.True(double.IsFinite(state.TargetPosition));
        Assert.True(double.IsFinite(state.Position));
        Assert.True(double.IsFinite(state.MaxScrollY));
    }

    private static string ResolveNodeText(VirtualTextArena arena, NodeContent content) =>
        content.TryGetText(out var tc) ? arena.ResolveRequired(tc).ToString() : "";
}
