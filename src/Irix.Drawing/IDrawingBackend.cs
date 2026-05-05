namespace Irix.Drawing;

public interface IDrawingBackend : IDisposable
{
    void BeginFrame(in FrameContext frameContext);

    void Execute(ReadOnlySpan<DrawCommand> commands, ReadOnlySpan<TextRunEntry> textRuns);

    void EndFrame();
}
