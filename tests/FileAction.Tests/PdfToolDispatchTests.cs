using FileAction.App.UI.Tools.Pdf;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// F-02 regression: the two PDF wheel operations must open their own tool, never the first registry
/// match for the items. A drop of one PDF or two images used to be able to open Lock/Protect/Collage
/// because <c>ToolRegistry.For(items)</c> is first-match.
/// </summary>
public sealed class PdfToolDispatchTests
{
    [Fact]
    public void Merge_opens_the_merge_window_not_the_first_registry_match()
    {
        Assert.Equal("tools.pdfmerge", new PdfMergeOperation().Tool.Id);
    }

    [Fact]
    public void Organize_opens_the_organize_window_not_the_first_registry_match()
    {
        Assert.Equal("tools.pdforganize", new PdfOrganizeOperation().Tool.Id);
    }
}
