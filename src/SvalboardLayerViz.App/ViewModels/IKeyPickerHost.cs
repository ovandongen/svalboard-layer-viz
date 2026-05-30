namespace SvalboardLayerViz.App.ViewModels;

/// <summary>
/// A child-editor view-model that can request a key-picker dialog. Lets
/// <see cref="Services.EditorDialogFactory"/> wire the picker callback once,
/// generically, across the macro / combo / tap-dance editors.
/// </summary>
internal interface IKeyPickerHost
{
    Func<Action<ushort>, IReadOnlySet<PickerCategory>?, Task>? RequestKeyPick { get; set; }
}
