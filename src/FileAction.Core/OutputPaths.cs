using static FileAction.Core.Loc;

namespace FileAction.Core;

public static class OutputPaths
{
    /// <summary>Windows' maximum length for one file or folder name, in UTF-16 code units.</summary>
    public const int MaxComponentLength = 255;

    /// <summary>
    /// "dir/name.ext", or "dir/name (2).ext" … if that already exists. A stem that cannot fit beside its
    /// extension and numbering is refused here with a readable message, instead of returning a path that
    /// only fails after the expensive work is done.
    /// </summary>
    public static string Unique(string dir, string baseName, string ext)
    {
        ext = ext.TrimStart('.');
        var suffix = ext.Length == 0 ? "" : "." + ext;
        var candidate = Path.Combine(dir, baseName + suffix);
        for (var i = 2; File.Exists(candidate) || Directory.Exists(candidate); i++)
            candidate = Path.Combine(dir, $"{baseName} ({i}){suffix}");
        if (Path.GetFileName(candidate).Length > MaxComponentLength)
            throw new ConversionException(T("error.output.nameTooLong", MaxComponentLength));
        return candidate;
    }

    public static string UniqueDirectory(string dir, string name) => Unique(dir, name, "");

    /// <summary>
    /// Runs <paramref name="produce"/> against a hidden temporary path in <paramref name="dir"/> and,
    /// when it succeeds, moves the file to a unique final name. Partial output is removed on failure.
    /// </summary>
    public static async Task<string> ProduceAsync(string dir, string baseName, string ext, Func<string, Task> produce)
    {
        Directory.CreateDirectory(dir);
        // Fail before producing anything when no final name can exist. The temporary name is internal and
        // is shortened when needed, so only the final component decides the limit.
        _ = Unique(dir, baseName, ext);
        var extension = ext.TrimStart('.');
        var tempStem = Fit(baseName, MaxComponentLength - extension.Length - ".part.".Length - 12);
        var temp = Path.Combine(dir, $".{tempStem}.{Guid.NewGuid().ToString("N")[..8]}.part.{extension}");
        try
        {
            await produce(temp).ConfigureAwait(false);
            if (!File.Exists(temp))
                throw new ConversionException(T("error.conversion.noOutput"));
            var final = Unique(dir, baseName, ext);
            File.Move(temp, final);
            return final;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static string Fit(string name, int maximum)
    {
        if (maximum <= 0)
            return string.Empty;
        return name.Length <= maximum ? name : name[..maximum];
    }

    public static string TempDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DropWheel", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            else if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public sealed class ConversionException(string message, Exception? inner = null) : Exception(message, inner);
