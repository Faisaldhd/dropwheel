using System.Runtime.CompilerServices;
using FileAction.Core;

namespace FileAction.Tests;

internal static class TestSetup
{
    /// <summary>
    /// The suite runs in English whatever the machine's display language is. Individual localization tests
    /// switch the language themselves and restore it afterwards.
    /// </summary>
    [ModuleInitializer]
    internal static void Init() => Loc.Current = Language.English;
}

/// <summary>
/// Serializes tests that change the process-wide UI language (see <see cref="LocalizationTests"/>).
/// </summary>
[CollectionDefinition(nameof(LanguageSwitching), DisableParallelization = true)]
public class LanguageSwitching;
