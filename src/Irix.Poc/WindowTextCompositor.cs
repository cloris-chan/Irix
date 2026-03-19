using Irix.Platform;
using Irix.Rendering;
using System.Text;

namespace Irix.Poc;

internal sealed class WindowTextCompositor(INativeWindow window) : ICompositor
{
    public ValueTask RenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        if (patchBatch.Count == 0)
        {
            return ValueTask.CompletedTask;
        }

        var root = patchBatch.Memory.Span[patchBatch.Count - 1].Node;
        var builder = new StringBuilder();
        AppendText(root, builder);
        window.SetContentText(builder.ToString().Trim());
        return ValueTask.CompletedTask;
    }

    private static void AppendText(VirtualNode node, StringBuilder builder)
    {
        if (node.Kind == VirtualNodeKind.Text && !string.IsNullOrWhiteSpace(node.Content))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(node.Content);
        }

        foreach (var child in node.Children)
        {
            AppendText(child, builder);
        }
    }
}
