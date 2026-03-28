using Irix.Drawing;

namespace Irix.Rendering;

public interface IPatchBatchTranslator
{
    DrawCommandBatch Translate(PatchBatch patchBatch);
}
