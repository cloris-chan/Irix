using System.Globalization;
using Irix.Drawing;
using Irix.Platform;
using Irix.Rendering;

namespace Irix.Poc;

internal static class PackageSmokeRunner
{
    public static void Run(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var application = new CounterApplication();
        var model = application.Initialize();
        model = application.Update(model, new CounterMessage.Increment()).NextModel;

        var tree = application.BuildView(model);
        using var batch = new RenderPipeline().Build(
            tree.Root,
            new PixelRectangle(0, 0, 320, 240),
            tree.TextSnapshot);

        writer.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "package-smoke count={0} commands={1} hitTargets={2} resources={3}",
                model.Count,
                batch.Commands.Count,
                batch.HitTargets.Count,
                batch.Resources.GetType().Name));
    }
}
