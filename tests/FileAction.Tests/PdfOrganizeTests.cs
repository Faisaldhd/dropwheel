using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The page and merge plans behind the 2.4.0 PDF tools. Pure model only: no PDF, image or temp file is
/// created, so these tests are fast and never depend on PDFsharp or a renderer.
/// </summary>
public class PdfOrganizeTests
{
    private static FileItem Item(string name) => new(Path.Combine(@"C:\DropWheelTests", name));

    private static PdfMergeSource Pdf(string name) =>
        new(Path.Combine(@"C:\DropWheelTests", name), PdfPlanKind.Pdf);

    private static PdfMergeSource Image(string name) =>
        new(Path.Combine(@"C:\DropWheelTests", name), PdfPlanKind.Image);

    private static string[] Names(PdfMergePlan plan) => plan.Sources.Select(source => source.FileName).ToArray();

    private static int[] Indexes(PdfPagePlan plan) => plan.Pages.Select(page => page.SourceIndex).ToArray();

    private static int[] Rotations(PdfPagePlan plan) => plan.Pages.Select(page => page.Rotation).ToArray();

    // ---------------------------------------------------------------- selecting files

    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("scan.PDF", true)]
    [InlineData("photo.jpg", true)]
    [InlineData("logo.png", true)]
    [InlineData("notes.txt", false)]
    [InlineData("clip.mp4", false)]
    public void CanMerge_accepts_pdfs_and_images_only(string name, bool expected)
    {
        Assert.Equal(expected, PdfOrganize.CanMerge(Item(name)));
    }

    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("notes.txt", false)]
    public void CanOrganize_accepts_a_single_pdf_only(string name, bool expected)
    {
        Assert.Equal(expected, PdfOrganize.CanOrganize(Item(name)));
    }

    [Fact]
    public void KindOf_tells_a_pdf_from_an_image()
    {
        Assert.Equal(PdfPlanKind.Pdf, PdfOrganize.KindOf(Item("report.pdf")));
        Assert.Equal(PdfPlanKind.Image, PdfOrganize.KindOf(Item("photo.jpg")));
    }

    // ---------------------------------------------------------------- merge plan

    [Fact]
    public void Merge_keeps_the_selection_order_and_mixes_pdfs_with_images()
    {
        var plan = new PdfMergePlan([Pdf("first.pdf"), Image("second.png"), Pdf("third.pdf")]);

        Assert.Equal(new[] { "first.pdf", "second.png", "third.pdf" }, Names(plan));
        Assert.Equal(3, plan.Count);
        Assert.False(plan.IsEmpty);
        Assert.True(plan.Validate().IsValid);
    }

    [Fact]
    public void Merge_move_forward_places_the_file_at_the_target_index()
    {
        var plan = new PdfMergePlan([Pdf("a.pdf"), Pdf("b.pdf"), Pdf("c.pdf")]);

        Assert.True(plan.Move(0, 2));

        Assert.Equal(new[] { "b.pdf", "c.pdf", "a.pdf" }, Names(plan));
    }

    [Fact]
    public void Merge_move_backward_places_the_file_at_the_target_index()
    {
        var plan = new PdfMergePlan([Pdf("a.pdf"), Pdf("b.pdf"), Pdf("c.pdf")]);

        Assert.True(plan.Move(2, 0));

        Assert.Equal(new[] { "c.pdf", "a.pdf", "b.pdf" }, Names(plan));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(1, 1)]
    public void Merge_move_rejects_out_of_range_or_no_op_indexes(int from, int to)
    {
        var plan = new PdfMergePlan([Pdf("a.pdf"), Pdf("b.pdf"), Pdf("c.pdf")]);

        Assert.False(plan.Move(from, to));
        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, Names(plan));
    }

    [Fact]
    public void Merge_remove_keeps_the_other_files_in_order()
    {
        var plan = new PdfMergePlan([Pdf("a.pdf"), Image("b.png"), Pdf("c.pdf")]);

        Assert.True(plan.RemoveAt(1));

        Assert.Equal(new[] { "a.pdf", "c.pdf" }, Names(plan));
        Assert.False(plan.RemoveAt(5));
    }

    [Fact]
    public void Merge_reset_restores_the_selection_order()
    {
        var plan = new PdfMergePlan([Pdf("a.pdf"), Image("b.png"), Pdf("c.pdf")]);
        plan.Move(0, 2);
        plan.RemoveAt(0);

        plan.Reset();

        Assert.Equal(new[] { "a.pdf", "b.png", "c.pdf" }, Names(plan));
    }

    [Fact]
    public void Merge_validate_flags_an_empty_plan()
    {
        var plan = new PdfMergePlan([]);

        var validation = plan.Validate();

        Assert.False(validation.IsValid);
        Assert.Equal(new[] { PdfPlanProblem.NoSources }, validation.Problems);
    }

    [Fact]
    public void Merge_validate_flags_a_missing_path()
    {
        var plan = new PdfMergePlan([Pdf("a.pdf"), new PdfMergeSource("  ", PdfPlanKind.Pdf)]);

        Assert.Contains(PdfPlanProblem.MissingPath, plan.Validate().Problems);
    }

    [Fact]
    public void Merge_validation_problems_are_distinct_and_ordered()
    {
        var plan = new PdfMergePlan([new PdfMergeSource("", PdfPlanKind.Pdf), new PdfMergeSource("", PdfPlanKind.Image)]);

        Assert.Equal(new[] { PdfPlanProblem.MissingPath }, plan.Validate().Problems);
    }

    // ---------------------------------------------------------------- page plan

    [Fact]
    public void Organize_seeds_one_page_per_source_page_in_order()
    {
        var plan = new PdfPagePlan(@"C:\DropWheelTests\report.pdf", 3);

        Assert.Equal(3, plan.Count);
        Assert.Equal(new[] { 0, 1, 2 }, Indexes(plan));
        Assert.Equal(new[] { 0, 0, 0 }, Rotations(plan));
        Assert.True(plan.CanDelete);
        Assert.True(plan.Validate().IsValid);
        Assert.All(plan.Pages, page => Assert.Equal(@"C:\DropWheelTests\report.pdf", page.SourcePath));
    }

    [Fact]
    public void Organize_rotate_adds_degrees_and_wraps()
    {
        var plan = new PdfPagePlan(@"C:\DropWheelTests\report.pdf", 2);

        Assert.True(plan.Rotate(0, 90));
        Assert.True(plan.Rotate(1, -90));

        Assert.Equal(new[] { 90, 270 }, Rotations(plan));

        Assert.True(plan.Rotate(1, 180));
        Assert.Equal(90, plan.Pages[1].Rotation);

        Assert.True(plan.Rotate(0, 270));
        Assert.Equal(0, plan.Pages[0].Rotation);
        Assert.False(plan.Rotate(9, 90));
    }

    [Fact]
    public void Organize_duplicate_inserts_a_copy_right_after_the_page()
    {
        var plan = new PdfPagePlan(@"C:\DropWheelTests\report.pdf", 3);
        plan.Rotate(1, 90);

        Assert.True(plan.Duplicate(1));

        Assert.Equal(new[] { 0, 1, 1, 2 }, Indexes(plan));
        Assert.Equal(new[] { 0, 90, 90, 0 }, Rotations(plan));
        Assert.Equal(4, plan.Count);
        Assert.False(plan.Duplicate(99));
    }

    [Fact]
    public void Organize_delete_removes_a_page_but_keeps_at_least_one()
    {
        var plan = new PdfPagePlan(@"C:\DropWheelTests\report.pdf", 2);

        Assert.True(plan.Delete(0));
        Assert.Equal(new[] { 1 }, Indexes(plan));
        Assert.False(plan.CanDelete);
        Assert.False(plan.Delete(0));
        Assert.False(plan.Delete(7));
        Assert.Single(plan.Pages);
    }

    [Fact]
    public void Organize_move_reorders_pages_and_keeps_duplicates()
    {
        var plan = new PdfPagePlan(@"C:\DropWheelTests\report.pdf", 3);
        plan.Duplicate(0);
        // 0, 0, 1, 2

        Assert.True(plan.Move(3, 0));
        Assert.Equal(new[] { 2, 0, 0, 1 }, Indexes(plan));

        Assert.True(plan.Move(0, 3));
        Assert.Equal(new[] { 0, 0, 1, 2 }, Indexes(plan));
        Assert.False(plan.Move(0, 0));
        Assert.False(plan.Move(0, 9));
    }

    [Fact]
    public void Organize_reset_restores_the_original_page_list()
    {
        var plan = new PdfPagePlan(@"C:\DropWheelTests\report.pdf", 3);
        plan.Rotate(0, 90);
        plan.Duplicate(2);
        plan.Delete(1);
        plan.Move(2, 0);

        plan.Reset();

        Assert.Equal(new[] { 0, 1, 2 }, Indexes(plan));
        Assert.Equal(new[] { 0, 0, 0 }, Rotations(plan));
    }

    [Fact]
    public void Organize_validate_accepts_the_original_pages()
    {
        var plan = new PdfPagePlan(@"C:\DropWheelTests\report.pdf", 4);

        Assert.True(plan.Validate().IsValid);
        Assert.Empty(plan.Validate().Problems);
    }

    [Fact]
    public void Organize_validate_flags_a_page_index_outside_the_source()
    {
        var path = @"C:\DropWheelTests\report.pdf";
        var plan = PdfPagePlan.FromPages(path, 2,
            [new PdfPageRef(path, 0, 0), new PdfPageRef(path, 2, 0), new PdfPageRef(path, -1, 0)]);

        Assert.Equal(new[] { PdfPlanProblem.PageIndexOutOfRange }, plan.Validate().Problems);
    }

    [Fact]
    public void Organize_validate_flags_a_rotation_that_is_not_a_quarter_turn()
    {
        var path = @"C:\DropWheelTests\report.pdf";
        var plan = PdfPagePlan.FromPages(path, 3,
            [new PdfPageRef(path, 0, 0), new PdfPageRef(path, 1, 45)]);

        Assert.Equal(new[] { PdfPlanProblem.RotationNotAQuarterTurn }, plan.Validate().Problems);
    }

    [Fact]
    public void Organize_validate_flags_an_empty_page_list()
    {
        var plan = PdfPagePlan.FromPages(@"C:\DropWheelTests\report.pdf", 2, []);

        Assert.Equal(new[] { PdfPlanProblem.EmptyPlan }, plan.Validate().Problems);
        Assert.False(plan.CanDelete);
    }

    [Fact]
    public void Page_ref_rotated_by_normalizes_to_a_quarter_turn()
    {
        var page = new PdfPageRef(@"C:\DropWheelTests\report.pdf", 0, 270);

        Assert.Equal(0, page.RotatedBy(90).Rotation);
        Assert.Equal(180, page.RotatedBy(270).Rotation);
        Assert.Equal(270, page.RotatedBy(0).Rotation);
        Assert.Equal(270, page.RotatedBy(-360).Rotation);
        Assert.Equal(0, page.SourceIndex);
    }

    [Fact]
    public void Rotation_helpers_treat_negative_and_full_turns_as_zero()
    {
        Assert.Equal(0, PdfOrganize.NormalizeRotation(360));
        Assert.Equal(90, PdfOrganize.NormalizeRotation(-270));
        Assert.Equal(270, PdfOrganize.NormalizeRotation(-90));
        Assert.True(PdfOrganize.IsQuarterTurn(-90));
        Assert.False(PdfOrganize.IsQuarterTurn(45));
    }
}
