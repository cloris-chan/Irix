using Irix.Rendering;

namespace Irix.Poc;

internal interface IWheelInputDispatchSink
{
    void DispatchWheelPixels(double pixels);
}

internal readonly struct WheelInputDispatchIntent
{
    private WheelInputDispatchIntent(double pixels)
    {
        Pixels = pixels;
    }

    public double Pixels { get; }

    public static WheelInputDispatchIntent FromRawDelta(int rawDelta)
    {
        var pixels = ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, rawDelta),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);
        return new WheelInputDispatchIntent(pixels);
    }
}

internal static class ScrollInputDispatchAdapter
{
    public static bool TryDispatchIntent<TDispatchSink>(
        in WheelInputDispatchIntent intent,
        TDispatchSink dispatchSink)
        where TDispatchSink : struct, IWheelInputDispatchSink
    {
        dispatchSink.DispatchWheelPixels(intent.Pixels);
        return true;
    }

    public static bool TryDispatchWheelRaw<TDispatchSink>(
        CounterMessage.WheelRaw wheel,
        TDispatchSink dispatchSink)
        where TDispatchSink : struct, IWheelInputDispatchSink
    {
        ArgumentNullException.ThrowIfNull(wheel);

        var intent = WheelInputDispatchIntent.FromRawDelta(wheel.RawDelta);
        return TryDispatchIntent(in intent, dispatchSink);
    }
}

internal readonly struct ScrollPresentationWheelDispatchSink(
    ScrollPresentationCoordinator Coordinator,
    Runtime<CounterModel, CounterMessage> Runtime,
    CompositorLoop CompositorLoop,
    WindowDrawCommandTranslator Translator,
    NodeKey ScrollTargetKey) : IWheelInputDispatchSink
{
    public void DispatchWheelPixels(double pixels)
    {
        Coordinator.AddPendingPixels(pixels);
        Coordinator.EnsureRunning(Runtime, CompositorLoop, Translator, ScrollTargetKey);
    }
}
