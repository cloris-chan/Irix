using Xunit;

namespace Irix.Core.Tests;

public sealed partial class ProgramDiagnosticsTests
{
    [Fact]
    [Trait("Category", "DocGuard")]
    public void Glyph_atlas_regression_lane_is_manual_ci_validation()
    {
        var root = FindRepoRoot();
        var workflow = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml")));
        var buildProps = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "Directory.Build.props")));

        Assert.Contains("workflow_dispatch:", workflow);
        Assert.Contains("glyph-atlas-smoke", workflow);
        Assert.Contains("Glyph atlas regression lane", workflow);
        Assert.Contains(".\\scripts\\glyph-atlas-regression.ps1 -Mode Smoke", workflow);
        Assert.Contains("Category!=D3D12&Category!=Performance&Category!=Guard", workflow);
        Assert.Contains("CI_WINDOWS_SDK_MIN_VERSION: 10.0.26100.0", workflow);
        Assert.Contains("Windows SDK 26100+", workflow);
        Assert.DoesNotContain("IrixWindowsRequiredSdkVersion", workflow);
        Assert.Contains("dotnet restore -p:IrixDiagnostics=true", workflow);
        Assert.Contains("<IrixWindowsTargetFramework>net10.0-windows10.0.26100.0</IrixWindowsTargetFramework>", buildProps);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", buildProps);
        Assert.DoesNotContain("IrixWindowsRequiredSdkVersion", buildProps);
        Assert.Contains("windows-2025", workflow);
    }

    [Fact]
    [Trait("Category", "DocGuard")]
    public void Local_validate_script_documents_quick_focused_glyph_and_full_lanes()
    {
        var root = FindRepoRoot();
        var validate = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "scripts", "validate.ps1")));
        var status = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Project_Status_and_Todo.md")));
        var worklist = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Active-Worklist.md")));

        Assert.Contains("[ValidateSet(\"Quick\", \"Focused\", \"GlyphSmoke\", \"Full\")]", validate);
        Assert.Contains("validation.guard status=Passed mode=$Mode", validate);
        Assert.Contains("validation.guard status=Failed mode=$Mode", validate);
        Assert.Contains("--maxcpucount:1", validate);
        Assert.Contains("Release build failed; retrying one serialized rebuild to recover generated outputs.", validate);
        Assert.Contains("\"-t:Rebuild\"", validate);
        Assert.Contains("Invoke-ReleaseBuild -Diagnostics", validate);
        Assert.Contains("\"--no-build\"", validate);
        Assert.Contains("Category!=D3D12&Category!=Performance&Category!=Guard", validate);
        Assert.Contains("Category=Guard&Category!=DocGuard", validate);
        Assert.Contains("Category!=DocGuard&(FullyQualifiedName~PartialApply|FullyQualifiedName~DrawingBackendCompositor)", validate);
        Assert.Contains("Category!=DocGuard&(FullyQualifiedName~Composition|FullyQualifiedName~Scroll|FullyQualifiedName~CounterInputRouter|FullyQualifiedName~WindowLayoutPipeline)", validate);
        Assert.Contains("FullyQualifiedName~PartialApply|FullyQualifiedName~DrawingBackendCompositor", validate);
        Assert.Contains(".\\scripts\\validate.ps1 -Mode Quick", status);
        Assert.Contains(".\\scripts\\validate.ps1 -Mode Focused", status);
        Assert.Contains(".\\scripts\\validate.ps1 -Mode GlyphSmoke", status);
        Assert.Contains("`Focused` validates high-signal source/architecture `Guard` checks while skipping lower-frequency `DocGuard` wording and source-shape audits across the category and name-filter lanes", worklist);
        Assert.Contains("`Full` runs the Release test suite", worklist);
    }

    [Fact]
    [Trait("Category", "DocGuard")]
    public void Glyph_atlas_status_documents_remote_and_local_guard_sources()
    {
        var root = FindRepoRoot();
        var status = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Project_Status_and_Todo.md")));
        var design = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Glyph-Atlas-Design.md")));
        var worklist = NormalizeLineEndings(File.ReadAllText(Path.Combine(root, "docs", "Active-Worklist.md")));

        Assert.Contains("Latest remote quick CI completed successfully on 2026-06-09 for commit `a19bb30`", status);
        Assert.Contains("current CI/source-of-truth status lives in [Project_Status_and_Todo.md]", design);
        Assert.Contains("TestResults\\glyph-atlas-regression-*-*.guard.summary.txt", status);
        Assert.Contains("Remote CI evidence is tracked only in [Project_Status_and_Todo.md]", worklist);
        Assert.Contains("Run `Quick` for routine changes and `Focused` after high-signal source/architecture guard", worklist);
        Assert.Contains("Run `Full` when lower-frequency `DocGuard` wording or source-shape audits matter", worklist);
        Assert.Contains("Run `Smoke` before/after broad rendering changes", worklist);
        Assert.Contains("Add artifact-upload work only for a concrete retention or debugging requirement", worklist);
        Assert.Contains("`Nightly` after page-policy, eviction, or shaping overhauls", worklist);
        Assert.DoesNotContain("quota is", status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quota is", worklist, StringComparison.OrdinalIgnoreCase);
    }
}
