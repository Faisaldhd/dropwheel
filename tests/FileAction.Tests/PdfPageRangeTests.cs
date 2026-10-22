using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The typed page range behind the merge window. Pure parsing and resolution only: no PDF, no UI, so a
/// typo is covered without depending on a renderer or a real document.
/// </summary>
public class PdfPageRangeTests
{
    private static int[] Pages(string? text)
    {
        Assert.True(PdfPageSelection.TryParse(text, out var selection));
        return [.. selection.Pages];
    }

    [Fact]
    public void An_empty_range_means_every_page()
    {
        foreach (var text in new string?[] { null, "", "   " })
        {
            Assert.True(PdfPageSelection.TryParse(text, out var selection));
            Assert.True(selection.IsAll);
            Assert.Empty(selection.Pages);
        }
    }

    [Fact]
    public void A_single_number_selects_that_page()
    {
        Assert.Equal(new[] { 0 }, Pages("1"));
        Assert.Equal(new[] { 4 }, Pages("5"));
        Assert.Equal(new[] { 0 }, Pages("01"));
    }

    [Fact]
    public void A_range_expands_to_every_page_between_its_ends()
    {
        Assert.Equal(new[] { 1, 2, 3 }, Pages("2-4"));
    }

    [Fact]
    public void Lists_are_resolved_in_document_order_without_duplicates()
    {
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, Pages("3, 1, 3, 2, 5-7, 4"));
    }

    [Fact]
    public void Commas_spaces_and_arabic_punctuation_separate_entries()
    {
        Assert.Equal(new[] { 0, 1, 2, 4 }, Pages(" 1 - 3 , 5 "));
        Assert.Equal(new[] { 0, 2 }, Pages("1;3"));
        Assert.Equal(new[] { 0, 2 }, Pages("1، 3"));
        Assert.Equal(new[] { 0, 1 }, Pages("1 2"));
    }

    [Fact]
    public void En_dashes_and_minus_signs_are_treated_as_hyphens()
    {
        Assert.Equal(new[] { 0, 1, 2 }, Pages("1–3"));
        Assert.Equal(new[] { 0, 1, 2 }, Pages("1—3"));
        Assert.Equal(new[] { 0, 1, 2 }, Pages("1−3"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3-1")]
    [InlineData("a")]
    [InlineData("1-")]
    [InlineData("-2")]
    [InlineData("1--2")]
    [InlineData("1-2-3")]
    [InlineData("1.5")]
    [InlineData("1-200000")]
    public void Malformed_or_impossible_ranges_are_rejected(string text)
    {
        Assert.False(PdfPageSelection.TryParse(text, out _));
    }

    [Fact]
    public void Resolve_maps_the_range_to_zero_based_pages()
    {
        Assert.True(PdfPageSelection.TryParse("2-3", out var selection));

        Assert.True(selection.TryResolve(3, out var pages));
        Assert.Equal(new[] { 1, 2 }, pages);

        Assert.False(selection.TryResolve(2, out var none));
        Assert.Empty(none);
    }

    [Fact]
    public void Fits_checks_a_range_without_building_the_page_list()
    {
        Assert.True(PdfPageSelection.TryParse("3", out var selection));
        Assert.True(selection.Fits(3));
        Assert.False(selection.Fits(2));
        Assert.False(selection.Fits(-1));

        Assert.True(PdfPageSelection.TryParse("", out var all));
        Assert.True(all.Fits(0));
        Assert.False(all.Fits(-1));
    }

    [Fact]
    public void All_resolves_to_the_whole_document()
    {
        Assert.True(PdfPageSelection.TryParse("", out var selection));

        Assert.True(selection.TryResolve(4, out var pages));
        Assert.Equal(new[] { 0, 1, 2, 3 }, pages);

        Assert.True(selection.TryResolve(0, out var empty));
        Assert.Empty(empty);
    }
}
