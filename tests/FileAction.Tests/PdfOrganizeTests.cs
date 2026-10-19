using System.Text.Json;
using System.Text.RegularExpressions;
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

    // ---------------------------------------------------------------- up/down arrows

    [Fact]
    public void Merge_arrows_are_disabled_at_the_first_and_last_row()
    {
        var plan = new PdfMergePlan([Pdf("a.pdf"), Pdf("b.pdf"), Pdf("c.pdf")]);

        Assert.False(plan.CanMoveUp(0));
        Assert.True(plan.CanMoveUp(1));
        Assert.True(plan.CanMoveUp(2));

        Assert.True(plan.CanMoveDown(0));
        Assert.True(plan.CanMoveDown(1));
        Assert.False(plan.CanMoveDown(2));

        Assert.False(plan.CanMoveUp(-1));
        Assert.False(plan.CanMoveDown(3));
    }

    [Fact]
    public void Merge_a_single_file_has_both_arrows_disabled()
    {
        var plan = new PdfMergePlan([Pdf("only.pdf")]);

        Assert.False(plan.CanMoveUp(0));
        Assert.False(plan.CanMoveDown(0));
        Assert.False(plan.MoveUp(0));
        Assert.False(plan.MoveDown(0));
    }

    [Fact]
    public void Merge_up_and_down_move_one_position()
    {
        var plan = new PdfMergePlan([Pdf("a.pdf"), Pdf("b.pdf"), Pdf("c.pdf")]);

        Assert.True(plan.MoveUp(2));
        Assert.Equal(new[] { "a.pdf", "c.pdf", "b.pdf" }, Names(plan));

        Assert.True(plan.MoveDown(0));
        Assert.Equal(new[] { "c.pdf", "a.pdf", "b.pdf" }, Names(plan));

        Assert.False(plan.MoveUp(0));
        Assert.False(plan.MoveDown(2));
        Assert.Equal(new[] { "c.pdf", "a.pdf", "b.pdf" }, Names(plan));
    }

    // ---------------------------------------------------------------- same width

    [Theory]
    [InlineData(600, 800, 0, 600)]
    [InlineData(600, 800, 90, 800)]
    [InlineData(600, 800, 180, 600)]
    [InlineData(600, 800, 270, 800)]
    [InlineData(842, 595, 0, 842)]
    [InlineData(842, 595, 90, 595)]
    public void VisualWidth_follows_the_page_rotation(double width, double height, int rotation, double expected)
    {
        Assert.Equal(expected, PdfOrganize.VisualWidth(new PdfPageBox(width, height), rotation), 3);
    }

    [Fact]
    public void ScaleToWidth_scales_both_dimensions_by_the_same_factor()
    {
        var portrait = new PdfPageBox(600, 800);

        Assert.Equal(0.5, PdfOrganize.ScaleToWidth(portrait, 0, 300), 3);
        Assert.Equal(400, portrait.Height * 0.5, 3);

        var landscape = new PdfPageBox(800, 600);
        Assert.Equal(0.5, PdfOrganize.ScaleToWidth(landscape, 0, 400), 3);
        Assert.Equal(300, landscape.Height * 0.5, 3);
    }

    [Theory]
    [InlineData(0, 800)]
    [InlineData(-10, 800)]
    public void ScaleToWidth_leaves_a_page_without_a_width_alone(double width, double height)
    {
        Assert.Equal(1, PdfOrganize.ScaleToWidth(new PdfPageBox(width, height), 0, 300), 3);
    }

    // ---------------------------------------------------------------- strings

    /// <summary>Every key the two PDF tools read, in one list so en and ar are checked together.</summary>
    private static readonly string[] PdfToolKeys =
    [
        "tool.pdfmerge.label",
        "tool.pdfmerge.title",
        "tool.pdfmerge.subtitle",
        "tool.pdfmerge.suffix",
        "tool.pdfmerge.hint",
        "tool.pdfmerge.remove",
        "tool.pdfmerge.thumbFailed",
        "tool.pdfmerge.error.plan",
        "tool.pdfmerge.error.pdf",
        "tool.pdfmerge.error.image",
        "tool.pdfmerge.order",
        "tool.pdfmerge.moveUp",
        "tool.pdfmerge.moveDown",
        "tool.pdfmerge.merge",
        "tool.pdforganize.label",
        "tool.pdforganize.title",
        "tool.pdforganize.subtitle",
        "tool.pdforganize.suffix",
        "tool.pdforganize.hint",
        "tool.pdforganize.rotate",
        "tool.pdforganize.duplicate",
        "tool.pdforganize.delete",
        "tool.pdforganize.thumbFailed",
        "tool.pdforganize.error.open",
        "tool.pdforganize.error.plan",
        "tool.pdforganize.pages",
        "tool.pdforganize.resetOrder",
        "tool.pdforganize.sameWidth",
        "tool.pdforganize.sameWidthNote",
        "tool.pdforganize.save",
    ];

    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    [Fact]
    public void Every_pdf_tool_key_exists_in_english_and_arabic_with_the_same_placeholders()
    {
        var (english, arabic) = LoadStringTables();

        foreach (var key in PdfToolKeys)
        {
            Assert.True(english.ContainsKey(key), $"en.json is missing '{key}'.");
            Assert.True(arabic.ContainsKey(key), $"ar.json is missing '{key}'.");
            Assert.Equal(Placeholders(english[key]), Placeholders(arabic[key]));
        }
    }

    [Fact]
    public void The_pdf_tool_key_groups_contain_exactly_the_expected_keys()
    {
        var (english, _) = LoadStringTables();

        var actual = english.Keys
            .Where(key => key.StartsWith("tool.pdfmerge.", StringComparison.Ordinal)
                || key.StartsWith("tool.pdforganize.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(PdfToolKeys.Order(StringComparer.Ordinal).ToArray(), actual);
    }

    private static string[] Placeholders(string value) =>
        Placeholder.Matches(value)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static (Dictionary<string, string> English, Dictionary<string, string> Arabic) LoadStringTables()
    {
        var strings = Path.Combine(RepoRoot(), "src", "FileAction.Core", "Strings");
        return (Load(Path.Combine(strings, "en.json")), Load(Path.Combine(strings, "ar.json")));
    }

    private static Dictionary<string, string> Load(string path)
    {
        Assert.True(File.Exists(path), $"Missing string table: {path}");
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        Assert.NotNull(table);
        return table!;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
