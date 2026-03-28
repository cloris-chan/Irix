using Irix.Drawing;

namespace Irix.Rendering;

public sealed class ConsoleCompositor(TextWriter writer) : ICompositor
{
    public ValueTask RenderAsync(DrawCommandBatch drawCommandBatch, CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < drawCommandBatch.Count; index++)
        {
            var command = drawCommandBatch.Memory.Span[index];
            writer.WriteLine(
                $"[Compositor] Command={command.Kind} Rect=({command.Rect.X}, {command.Rect.Y}, {command.Rect.Width}, {command.Rect.Height}) Text={command.Text ?? "<none>"} Metadata={command.Metadata ?? "<none>"}");
        }

        return ValueTask.CompletedTask;
    }
}
