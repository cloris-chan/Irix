namespace Irix.Rendering;

internal interface IPatchBatchTranslator
{
    RenderFrameBatch Translate(PatchBatch patchBatch);
}
