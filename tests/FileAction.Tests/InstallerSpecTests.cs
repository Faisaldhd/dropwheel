using System.Text.RegularExpressions;

namespace FileAction.Tests;

/// <summary>
/// 2.2.0 installer acceptance. Reads the Inno Setup script and the two packaging scripts from the repository
/// and pins the values the release process depends on (identity, per-user privileges, languages, checked
/// tasks, upgrade cleanup, silent install and the versioned output name). The files are owned by the
/// installer agent; if one is missing the test fails with the exact path instead of silently passing.
/// </summary>
public class InstallerSpecTests
{
    private const string IssPath = "installer/DropWheel.iss";
    private const string TestScriptPath = "scripts/test-installer.ps1";
    private const string BuildScriptPath = "scripts/build-installer.ps1";
    private const string AppId = "1AB9CDBE-C50D-484B-96ED-5D575D0806D2";

    [Fact]
    public void Installer_inputs_exist()
    {
        var root = RepoRoot();

        _ = ReadRequired(root, IssPath);
        _ = ReadRequired(root, TestScriptPath);
        _ = ReadRequired(root, BuildScriptPath);
    }

    [Fact]
    public void Iss_keeps_the_release_identity_and_per_user_privileges()
    {
        var iss = ReadRequired(RepoRoot(), IssPath);

        Assert.True(
            iss.Contains(AppId, StringComparison.OrdinalIgnoreCase),
            $"installer/DropWheel.iss must keep AppId {AppId} so a new setup upgrades the previous install.");
        Assert.Matches(@"(?im)^\s*PrivilegesRequired\s*=\s*lowest\s*$", iss);
        Assert.Matches(@"(?im)^\s*PrivilegesRequiredOverridesAllowed\s*=\s*dialog(\s+commandline)?\s*$", iss);
    }

    [Fact]
    public void Iss_offers_english_and_arabic_and_two_checked_tasks()
    {
        var iss = ReadRequired(RepoRoot(), IssPath);

        var languages = Section(iss, "Languages");
        Assert.Matches(@"(?im)^\s*Name:\s*""english""", languages);
        Assert.Matches(@"(?im)^\s*Name:\s*""arabic""", languages);

        var tasks = Section(iss, "Tasks");
        var checkedTasks = tasks
            .Split('\n')
            .Select(line => line.Trim())
            .Count(line =>
                line.Contains("Flags:", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("checked", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, checkedTasks);
    }

    [Fact]
    public void Iss_closes_apps_cleans_the_run_value_and_versions_the_output_name()
    {
        var iss = ReadRequired(RepoRoot(), IssPath);

        Assert.Matches(@"(?im)^\s*CloseApplications\s*=\s*yes\s*$", iss);
        Assert.True(
            iss.Contains("RemoveShellIntegration", StringComparison.OrdinalIgnoreCase) &&
            iss.Contains("RegDeleteValue", StringComparison.OrdinalIgnoreCase),
            "The uninstaller must remove the per-user Run value in [Code] (RemoveShellIntegration/RegDeleteValue).");
        Assert.Matches(
            @"(?im)^\s*OutputBaseFilename\s*=\s*" +
            Regex.Escape("DropWheel-{#AppVersion}-Setup") +
            @"\s*$",
            iss);
    }

    [Fact]
    public void Silent_install_and_the_watch_flag_are_wired()
    {
        var root = RepoRoot();
        var iss = ReadRequired(root, IssPath);
        var testScript = ReadRequired(root, TestScriptPath);
        var buildScript = ReadRequired(root, BuildScriptPath);

        Assert.True(
            iss.Contains("--set-watch", StringComparison.OrdinalIgnoreCase),
            "The installer must pass --set-watch for the checked keyboard/mouse monitoring task.");
        Assert.True(
            testScript.Contains("/VERYSILENT", StringComparison.OrdinalIgnoreCase),
            "scripts/test-installer.ps1 must exercise the documented /VERYSILENT install path.");
        Assert.True(
            buildScript.Contains("DropWheel.iss", StringComparison.OrdinalIgnoreCase),
            "scripts/build-installer.ps1 must compile installer/DropWheel.iss.");
    }

    /// <summary>Returns one <c>[Section]</c> body from the Inno script (without the next section header).</summary>
    private static string Section(string iss, string name)
    {
        var match = Regex.Match(
            iss,
            $@"(?ims)^[ \t]*\[{Regex.Escape(name)}\][ \t]*\r?$\r?\n(.*?)(?=^[ \t]*\[|\z)");
        Assert.True(match.Success, $"The installer has no [{name}] section.");
        return match.Groups[1].Value;
    }

    private static string ReadRequired(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(
            File.Exists(path),
            $"Required installer file '{relativePath}' is missing at {path}; the installer agent must commit it.");
        return File.ReadAllText(path);
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
