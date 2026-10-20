namespace FileAction.App.UI.Tools;

/// <summary>
/// A tool whose primary button is not always usable (for example a create dialog whose password pair is
/// still incomplete). The shared window disables the button until <see cref="CanApply"/> is true and
/// re-enables it when the tool raises <see cref="CanApplyChanged"/>.
/// </summary>
internal interface IToolActionGate
{
    bool CanApply { get; }

    event Action? CanApplyChanged;
}
