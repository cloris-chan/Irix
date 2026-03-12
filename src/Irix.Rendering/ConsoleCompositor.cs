namespace Irix.Rendering;

public sealed class ConsoleCompositor(TextWriter writer) : ICompositor
{
    public ValueTask RenderAsync(PatchBatch patchBatch, CancellationToken cancellationToken = default)
    {
        for (var index = 0; index < patchBatch.Count; index++)
        {
            var patch = patchBatch.Memory.Span[index];
            writer.WriteLine($"[Compositor] Screen={patch.ScreenId} Operation={patch.Operation} Node={patch.Node.Kind} Key={patch.Node.Key}");
        }

        return ValueTask.CompletedTask;
    }
}
