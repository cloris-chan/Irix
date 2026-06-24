using Irix.Poc;

namespace Irix.Core.Tests;

internal static class VirtualNodeTestBuilder
{
    public static VirtualNode Button(VirtualTextArena arena, string label, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        ControlNodeBuilder.Button(arena, label, key, properties);

    public static VirtualNode Button(TextContentResource label, NodeKey key = default, params scoped ReadOnlySpan<VirtualNodeProperty> properties) =>
        ControlNodeBuilder.Button(label, key, properties);
}
