namespace FileAction.App.UI.Tools;

/// <summary>
/// Lets a tool view choose the primary button's label at run time: the Lock tool says "Encrypt" for a
/// plain file and "Open" when the same window is showing a protected one.
/// </summary>
internal interface IToolActionLabel
{
    string PrimaryLabelKey { get; }
}
