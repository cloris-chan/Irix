using Irix.Rendering;

namespace Irix.Poc;

internal interface IWheelInputDispatchSink
{
    void DispatchWheelPixels(double pixels);
}

internal static class ScrollInputDispatchAdapter
{
    public static bool TryDispatchWheelRaw<TDispatchSink>(
        CounterMessage.WheelRaw wheel,
        TDispatchSink dispatchSink)
        where TDispatchSink : struct, IWheelInputDispatchSink
    {
        ArgumentNullException.ThrowIfNull(wheel);

        var pixels = ScrollController.ConvertToPixels(
            new ScrollDelta(ScrollDeltaUnit.WheelRaw, wheel.RawDelta),
            ScrollMetrics.DefaultText,
            SystemScrollSettings.Default);
        dispatchSink.DispatchWheelPixels(pixels);
        return true;
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
