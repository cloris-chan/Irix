using Irix.Drawing;

namespace Irix.Rendering;

public sealed class ConsoleCompositor(TextWriter writer) : ICompositor
{
    public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
    {
        if (renderFrameBatch.HitTargets.Count > 0)
        {
            writer.WriteLine($"[Compositor] HitTargets={renderFrameBatch.HitTargets.Count}");
        }

        for (var index = 0; index < renderFrameBatch.Commands.Count; index++)
        {
            var command = renderFrameBatch.Commands.Memory.Span[index];
            var text = LookUpText(renderFrameBatch.TextRuns, command.Resource);
            writer.WriteLine(
                $"[Compositor] Command={command.Kind} Rect=({command.Rect.X}, {command.Rect.Y}, {command.Rect.Width}, {command.Rect.Height}) Text={text ?? "<none>"}");
        }

        return ValueTask.CompletedTask;
    }

    private static string? LookUpText(IReadOnlyList<TextRunEntry> textRuns, ResourceHandle resource)
    {
        if (resource.Kind != DrawingResourceKind.TextStyle || resource.Id < 0 || textRuns.Count == 0)
        {
            return null;
        }

        foreach (var entry in textRuns)
        {
            if (entry.Id == resource.Id)
            {
                return entry.Text;
            }
        }

        return null;
    }
}
