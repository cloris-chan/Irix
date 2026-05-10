using Irix.Drawing;
using Irix.Rendering;

namespace Irix.Poc;

internal static class CounterStylePreset
{
    public static RenderStylePreset Default { get; } = RenderStylePreset.Default with
    {
        Drawing = RenderStylePreset.Default.Drawing with
        {
            TextColor = DrawColor.Opaque(32, 32, 32)
        }
    };
}