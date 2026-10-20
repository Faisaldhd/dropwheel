using System.Text.RegularExpressions;

namespace FileAction.Tests;

/// <summary>
/// Static guards for the two motion laws a CI runner cannot observe from a rendered frame:
/// <list type="number">
/// <item><b>Only <c>RenderTransform</c> and <c>Opacity</c> are animated.</b> The one documented
/// exception is the 180 ms media-orientation resize, which lives in <c>Motion.Resize</c> and is called
/// from exactly one place (<c>ToolWindow.PrepareAsync</c>).</item>
/// <item><b>Every duration is a token.</b> No file outside <c>UI/Motion/Motion.cs</c> may construct an
/// animation with a literal millisecond duration, so a new surface cannot quietly invent a timing.</item>
/// </list>
/// Two weaker structural checks pin the reduced-motion discipline (<c>Motion.Enabled</c> is consulted in
/// every file that owns an animation) and the one-place tool-window hook. These are source scans, not a
/// C# parser: they read the animation calls and their balanced argument lists, so a construct spread over
/// more than one statement is not followed. The runtime counterpart is <c>MotionSelfTest</c>.
/// </summary>
public class MotionSourceTests
{
    /// <summary>A call that starts or targets an animation clock.</summary>
    private static readonly Regex AnimationCall = new(@"\b(?:BeginAnimation|SetTargetProperty)\s*\(", RegexOptions.Compiled);

    /// <summary>A dependency-property field reference such as <c>UIElement.OpacityProperty</c>.</summary>
    private static readonly Regex PropertyToken = new(@"\b[A-Za-z_]\w*Property\b", RegexOptions.Compiled);

    /// <summary>An animation object constructed with a duration.</summary>
    private static readonly Regex AnimationCtor = new(
        @"\bnew\s+(?:System\.Windows\.Media\.Animation\.)?(?:Double|Thickness|Color|Point|Vector|Int32)Animation(?:UsingKeyFrames)?\s*\(",
        RegexOptions.Compiled);

    /// <summary>A literal millisecond duration, i.e. a hard-coded timing.</summary>
    private static readonly Regex HardCodedMilliseconds = new(@"FromMilliseconds\(\s*\d", RegexOptions.Compiled);

    /// <summary>The properties an animation may touch: compositor-friendly RenderTransform and opacity only.</summary>
    private static readonly HashSet<string> AnimatableProperties = new(StringComparer.Ordinal)
    {
        "OpacityProperty",
        "ScaleXProperty",
        "ScaleYProperty",
        "XProperty",
        "YProperty",
        "AngleProperty",
    };

    [Fact]
    public void Only_render_transform_and_opacity_are_animated()
    {
        var offenders = new List<string>();
        foreach (var file in AppSources())
        {
            var text = File.ReadAllText(file);
            foreach (var arguments in Arguments(AnimationCall, text))
            {
                foreach (Match token in PropertyToken.Matches(arguments.Text))
                {
                    if (AnimatableProperties.Contains(token.Value))
                        continue;
                    offenders.Add(
                        $"{Relative(file)}:{LineOf(text, arguments.Start + token.Index)} animates {token.Value}; " +
                        "only RenderTransform and Opacity may be animated (docs/MOTION.md)");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "animation of a layout or paint property:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void The_media_resize_is_the_only_layout_animation()
    {
        var layoutExceptions = new[]
        {
            "Window.WidthProperty", "Window.HeightProperty", "Window.LeftProperty", "Window.TopProperty",
        };
        var owners = AppSources()
            .Where(file => layoutExceptions.Any(name => File.ReadAllText(file).Contains(name, StringComparison.Ordinal)))
            .Select(Relative)
            .ToArray();
        Assert.Equal(new[] { Path.Combine("src", "FileAction.App", "UI", "Motion", "Motion.cs") }, owners);

        var resizeCallers = AppSources()
            .Where(file => File.ReadAllText(file).Contains("Motion.Resize(", StringComparison.Ordinal))
            .Select(Relative)
            .ToArray();
        Assert.Equal(new[] { Path.Combine("src", "FileAction.App", "UI", "Tools", "ToolWindow.cs") }, resizeCallers);
    }

    [Fact]
    public void No_animation_duration_is_hard_coded_outside_the_motion_tokens()
    {
        var offenders = new List<string>();
        foreach (var file in AppSources())
        {
            if (IsMotionVocabulary(file))
                continue;
            var text = File.ReadAllText(file);
            foreach (var arguments in Arguments(AnimationCtor, text))
            {
                var literal = HardCodedMilliseconds.Match(arguments.Text);
                if (!literal.Success)
                    continue;
                offenders.Add(
                    $"{Relative(file)}:{LineOf(text, arguments.Start + literal.Index)} has a hard-coded animation " +
                    "duration; add a token to Motion.cs and use it (docs/MOTION.md)");
            }
        }

        Assert.True(offenders.Count == 0,
            "hard-coded animation duration:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Every_file_that_owns_an_animation_consults_Motion_Enabled()
    {
        var offenders = AppSources()
            .Where(file => !IsMotionVocabulary(file))
            .Where(file => Arguments(AnimationCtor, File.ReadAllText(file)).Count > 0)
            .Where(file => !File.ReadAllText(file).Contains("Motion.Enabled", StringComparison.Ordinal))
            .Select(Relative)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "a file constructs animations without consulting Motion.Enabled (reduced motion would be ignored):" +
            Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void Tool_windows_route_all_motion_through_the_shared_hook()
    {
        var toolWindow = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FileAction.App", "UI", "Tools", "ToolWindow.cs"));
        foreach (var call in new[] { "Motion.Prepare(", "Motion.Open(", "Motion.Close(", "Motion.Resize(" })
            Assert.True(toolWindow.Contains(call, StringComparison.Ordinal), $"ToolWindow must route through {call}Motion.cs");
        Assert.DoesNotContain("new DoubleAnimation", toolWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("FromMilliseconds(", toolWindow, StringComparison.Ordinal);

        var harness = File.ReadAllText(Path.Combine(RepoRoot(), "src", "FileAction.App", "UI", "Motion", "MotionSelfTest.cs"));
        Assert.Contains("foreach (var spec in ToolRegistry.All)", harness, StringComparison.Ordinal);
        Assert.Contains("Motion.Override = true", harness, StringComparison.Ordinal);
        Assert.Contains("Motion.Override = false", harness, StringComparison.Ordinal);
    }

    /// <summary>The balanced argument list of every call matching <paramref name="call"/>.</summary>
    private static List<(int Start, string Text)> Arguments(Regex call, string text)
    {
        var found = new List<(int, string)>();
        foreach (Match match in call.Matches(text))
        {
            if (IsCommentLine(text, match.Index))
                continue;
            var open = match.Index + match.Length - 1;
            var depth = 0;
            for (var i = open; i < text.Length; i++)
            {
                if (text[i] == '(')
                    depth++;
                else if (text[i] == ')' && --depth == 0)
                {
                    found.Add((open + 1, text[(open + 1)..i]));
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>True when the match sits on a line that is entirely a comment (documentation examples).</summary>
    private static bool IsCommentLine(string text, int index)
    {
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var line = text[lineStart..Math.Min(text.Length, index + 1)].TrimStart();
        return line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal) ||
               line.StartsWith("/*", StringComparison.Ordinal);
    }

    private static bool IsMotionVocabulary(string file) => Relative(file) == Path.Combine(
        "src", "FileAction.App", "UI", "Motion", "Motion.cs");

    private static IEnumerable<string> AppSources()
    {
        var app = Path.Combine(RepoRoot(), "src", "FileAction.App");
        return Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories).OrderBy(file => file, StringComparer.Ordinal);
    }

    private static string Relative(string file) => Path.GetRelativePath(RepoRoot(), file);

    private static int LineOf(string text, int index) => text[..index].Count(character => character == '\n') + 1;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FileAction.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
