namespace FileAction.Core;

/// <summary>
/// The pure model behind the 2.4.0 PDF tool windows: an ordered merge plan of whole files (each image
/// contributes one page) and an ordered page plan with rotate / duplicate / delete / move on top of a
/// single PDF. Nothing here opens a PDF or touches the disk, so every rule below is unit tested without a
/// real document; the writers that turn these plans into a PDF live in the app project.
/// </summary>
public static class PdfOrganize
{
    /// <summary>True when the file can join a merge: a PDF or an image (one page per image).</summary>
    public static bool CanMerge(FileItem item) =>
        !item.IsDirectory && (item.Format == "pdf" || Formats.Images.Contains(item.Format));

    /// <summary>True when the single file can be opened in the page organizer.</summary>
    public static bool CanOrganize(FileItem item) => !item.IsDirectory && item.Format == "pdf";

    /// <summary>What a merge source contributes: the whole PDF, or one image page.</summary>
    public static PdfPlanKind KindOf(FileItem item) =>
        item.Format == "pdf" ? PdfPlanKind.Pdf : PdfPlanKind.Image;

    /// <summary>A rotation in 0..359; negative values wrap the same way screens do.</summary>
    public static int NormalizeRotation(int degrees) => ((degrees % 360) + 360) % 360;

    /// <summary>PDF pages only rotate in quarter turns.</summary>
    public static bool IsQuarterTurn(int degrees) => NormalizeRotation(degrees) % 90 == 0;
}

/// <summary>What one dropped file contributes to a merge.</summary>
public enum PdfPlanKind
{
    /// <summary>Every page of the PDF is added, in its own order.</summary>
    Pdf,

    /// <summary>The image becomes exactly one page, sized from its pixels.</summary>
    Image,
}

/// <summary>One file in the merge order. The kind is decided from the format when the window opens.</summary>
public sealed record PdfMergeSource(string Path, PdfPlanKind Kind)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public bool IsImage => Kind == PdfPlanKind.Image;
}

/// <summary>
/// One page of a result document: the source file, the 0-based page index inside it, and the rotation
/// added on top of the page's own /Rotate entry.
/// </summary>
public readonly record struct PdfPageRef(string SourcePath, int SourceIndex, int Rotation)
{
    /// <summary>A copy of this page with <paramref name="degrees"/> added, normalized to 0..359.</summary>
    public PdfPageRef RotatedBy(int degrees) =>
        this with { Rotation = PdfOrganize.NormalizeRotation(Rotation + degrees) };
}

/// <summary>Why a plan cannot be written. The UI decides whether to explain or simply disable Apply.</summary>
public enum PdfPlanProblem
{
    /// <summary>The merge plan has no files left.</summary>
    NoSources,

    /// <summary>A source has no usable path.</summary>
    MissingPath,

    /// <summary>The page plan has no pages left.</summary>
    EmptyPlan,

    /// <summary>A page index does not exist in the source document.</summary>
    PageIndexOutOfRange,

    /// <summary>A page rotation is not a multiple of 90 degrees.</summary>
    RotationNotAQuarterTurn,
}

/// <summary>The problems found in a plan; empty means the plan can be written.</summary>
public sealed record PdfPlanValidation(IReadOnlyList<PdfPlanProblem> Problems)
{
    /// <summary>A plan with no problems.</summary>
    public static readonly PdfPlanValidation Ok = new(Array.Empty<PdfPlanProblem>());

    public bool IsValid => Problems.Count == 0;

    internal static PdfPlanValidation Of(List<PdfPlanProblem> problems) =>
        problems.Count == 0 ? Ok : new PdfPlanValidation(problems);
}

/// <summary>
/// The ordered files of a merge, seeded from the selection order. Reordering is drag-and-drop in the
/// window; this type only owns the order and the validation. Reset restores the selection order.
/// </summary>
public sealed class PdfMergePlan
{
    private readonly List<PdfMergeSource> _sources;
    private readonly List<PdfMergeSource> _initial;

    public PdfMergePlan(IEnumerable<PdfMergeSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _initial = [.. sources];
        _sources = [.. _initial];
    }

    public IReadOnlyList<PdfMergeSource> Sources => _sources;

    public int Count => _sources.Count;

    public bool IsEmpty => _sources.Count == 0;

    public PdfMergeSource this[int index] => _sources[index];

    /// <summary>Moves the file at <paramref name="from"/> so it ends up at <paramref name="to"/>.</summary>
    public bool Move(int from, int to)
    {
        if (from < 0 || from >= _sources.Count || to < 0 || to >= _sources.Count || from == to)
            return false;
        var source = _sources[from];
        _sources.RemoveAt(from);
        _sources.Insert(to, source);
        return true;
    }

    /// <summary>Removes one file; the other files keep their order.</summary>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _sources.Count)
            return false;
        _sources.RemoveAt(index);
        return true;
    }

    /// <summary>Restores the order the files were dropped in.</summary>
    public void Reset()
    {
        _sources.Clear();
        _sources.AddRange(_initial);
    }

    public PdfPlanValidation Validate()
    {
        if (_sources.Count == 0)
            return PdfPlanValidation.Of([PdfPlanProblem.NoSources]);

        var problems = new List<PdfPlanProblem>();
        if (_sources.Any(source => string.IsNullOrWhiteSpace(source.Path)))
            problems.Add(PdfPlanProblem.MissingPath);
        return PdfPlanValidation.Of(problems);
    }
}

/// <summary>
/// The ordered pages of one PDF, seeded as one page per source page in document order. Duplicate inserts
/// a copy right after the page it copies, delete never removes the last page, and rotate adds quarter
/// turns to the page's own rotation. Reset restores the original page list.
/// </summary>
public sealed class PdfPagePlan
{
    private readonly List<PdfPageRef> _pages;
    private readonly List<PdfPageRef> _initial;

    /// <summary>Seeds the plan with page 0..<paramref name="pageCount"/>-1, no added rotation.</summary>
    public PdfPagePlan(string sourcePath, int pageCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (pageCount < 1)
            throw new ArgumentOutOfRangeException(nameof(pageCount), "A PDF has at least one page.");

        SourcePath = sourcePath;
        SourcePageCount = pageCount;
        _initial = Enumerable.Range(0, pageCount)
            .Select(index => new PdfPageRef(sourcePath, index, 0))
            .ToList();
        _pages = [.. _initial];
    }

    /// <summary>Copies an explicit page list, including entries that <see cref="Validate"/> can reject.</summary>
    public static PdfPagePlan FromPages(string sourcePath, int sourcePageCount, IEnumerable<PdfPageRef> pages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(pages);
        if (sourcePageCount < 1)
            throw new ArgumentOutOfRangeException(nameof(sourcePageCount), "A PDF has at least one page.");

        var plan = new PdfPagePlan(sourcePath, sourcePageCount);
        plan._initial.Clear();
        plan._pages.Clear();
        foreach (var page in pages)
        {
            plan._initial.Add(page);
            plan._pages.Add(page);
        }
        return plan;
    }

    public string SourcePath { get; }

    public int SourcePageCount { get; }

    public IReadOnlyList<PdfPageRef> Pages => _pages;

    public int Count => _pages.Count;

    /// <summary>False when only one page is left: a document cannot become empty.</summary>
    public bool CanDelete => _pages.Count > 1;

    /// <summary>Adds <paramref name="degrees"/> (typically ±90) to one page's rotation.</summary>
    public bool Rotate(int index, int degrees)
    {
        if (index < 0 || index >= _pages.Count)
            return false;
        _pages[index] = _pages[index].RotatedBy(degrees);
        return true;
    }

    /// <summary>Inserts a copy of the page right after it; the copy keeps the page's rotation.</summary>
    public bool Duplicate(int index)
    {
        if (index < 0 || index >= _pages.Count)
            return false;
        _pages.Insert(index + 1, _pages[index]);
        return true;
    }

    /// <summary>Removes one page unless it is the last one.</summary>
    public bool Delete(int index)
    {
        if (index < 0 || index >= _pages.Count || _pages.Count == 1)
            return false;
        _pages.RemoveAt(index);
        return true;
    }

    /// <summary>Moves the page at <paramref name="from"/> so it ends up at <paramref name="to"/>.</summary>
    public bool Move(int from, int to)
    {
        if (from < 0 || from >= _pages.Count || to < 0 || to >= _pages.Count || from == to)
            return false;
        var page = _pages[from];
        _pages.RemoveAt(from);
        _pages.Insert(to, page);
        return true;
    }

    /// <summary>Restores the page list the document had when the window opened.</summary>
    public void Reset()
    {
        _pages.Clear();
        _pages.AddRange(_initial);
    }

    /// <summary>
    /// Checks what would make the writer fail: no pages, a page that does not exist in the source, or a
    /// rotation that is not a quarter turn. Returns every problem, not just the first one.
    /// </summary>
    public PdfPlanValidation Validate()
    {
        if (_pages.Count == 0)
            return PdfPlanValidation.Of([PdfPlanProblem.EmptyPlan]);

        var problems = new List<PdfPlanProblem>();
        foreach (var page in _pages)
        {
            if (page.SourceIndex < 0 || page.SourceIndex >= SourcePageCount)
                problems.Add(PdfPlanProblem.PageIndexOutOfRange);
            if (!PdfOrganize.IsQuarterTurn(page.Rotation))
                problems.Add(PdfPlanProblem.RotationNotAQuarterTurn);
        }
        return PdfPlanValidation.Of(problems.Distinct().Order().ToList());
    }
}
