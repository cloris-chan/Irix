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
            writer.Write($"[Compositor] Command={command.Kind} Rect=({command.Rect.X}, {command.Rect.Y}, {command.Rect.Width}, {command.Rect.Height}) Text=");
            if (command.Kind == DrawCommandKind.DrawTextRun)
            {
                writer.Write(renderFrameBatch.Resources.Resolve(command.Text));
            }
            else
            {
                writer.Write("<none>");
            }

            writer.WriteLine();
        }

        return ValueTask.CompletedTask;
    }
}
