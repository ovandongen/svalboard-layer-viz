using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using SvalboardLayerViz.App.Localization;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.App.Views;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Keymap.Builders;
using SvalboardLayerViz.Core.Macros;

namespace SvalboardLayerViz.App.Services;

/// <summary>
/// Centralises construction of child dialogs owned by MainWindow: key picker,
/// macro/combo/tap-dance editors, and the two raw text-prompt dialogs. Keeps
/// dialog plumbing out of App.axaml.cs so Topmost/IsAlwaysOnTop, key-picker
/// re-entry, and localisation choices live in one place.
/// </summary>
internal static class EditorDialogFactory
{
    /// <summary>
    /// Shows a key-picker dialog owned by <paramref name="owner"/>, filtered to
    /// the given category set. Data is pulled from <paramref name="vm"/> so
    /// callers don't have to reach into <c>KeyboardConfig</c>.
    /// </summary>
    public static Task ShowKeyPickerAsync(
        MainWindowViewModel vm,
        Window owner,
        Action<ushort> onApply,
        IReadOnlySet<PickerCategory>? allowedCategories)
    {
        var pickerVm = new PickerSessionViewModel(
            BuilderRegistry.CreateAll(),
            new KeycodeService(),
            vm.KeyboardConfig?.CustomKeycodes ?? [],
            [],
            macroCount: vm.KeyboardConfig?.Macros?.Macros.Count ?? 0,
            tapDanceCount: vm.KeyboardConfig?.TapDances.Count ?? 0)
        {
            AllowedCategories = allowedCategories,
        };
        var pickerDialog = new KeyPickerDialog
        {
            DataContext = pickerVm,
            Topmost = vm.IsAlwaysOnTop,
        };
        pickerVm.Applied = code => { onApply(code); pickerDialog.Close(); };
        pickerVm.Cancelled = () => pickerDialog.Close();
        return pickerDialog.ShowDialog(owner);
    }

    public static Task ShowMacroEditorAsync(MainWindowViewModel vm, Window owner)
    {
        var macroBuffer = vm.GetCurrentMacros();
        var macroEditorVm = new MacroEditorViewModel(
            macroBuffer,
            vm.IsEditMode,
            vm.IsEditMode ? vm.ApplyMacroEdit : null);

        var dialog = new MacroEditorDialog
        {
            DataContext = macroEditorVm,
            Topmost = vm.IsAlwaysOnTop,
        };

        Action<MacroBuffer> onMacrosLoaded = buffer => macroEditorVm.UpdateFromBuffer(buffer);
        vm.MacrosLoaded += onMacrosLoaded;
        dialog.Closed += (_, _) => vm.MacrosLoaded -= onMacrosLoaded;

        macroEditorVm.Saved = () => dialog.Close();
        macroEditorVm.Cancelled = () => dialog.Close();
        macroEditorVm.RequestKeyPick = (onApply, allowed) =>
            ShowKeyPickerAsync(vm, dialog, onApply, allowed);

        return dialog.ShowDialog(owner);
    }

    public static Task ShowComboEditorAsync(MainWindowViewModel vm, Window owner)
    {
        var comboVm = new ComboEditorViewModel(
            vm.GetCurrentCombos(),
            new KeycodeService(),
            vm.IsEditMode,
            vm.IsEditMode ? vm.ApplyComboEdit : null);

        var dialog = new ComboEditorDialog
        {
            DataContext = comboVm,
            Topmost = vm.IsAlwaysOnTop,
        };
        comboVm.Closed = () => dialog.Close();
        comboVm.RequestKeyPick = (onApply, allowed) =>
            ShowKeyPickerAsync(vm, dialog, onApply, allowed);

        return dialog.ShowDialog(owner);
    }

    public static Task ShowTapDanceEditorAsync(MainWindowViewModel vm, Window owner)
    {
        var tdVm = new TapDanceEditorViewModel(
            vm.GetCurrentTapDances(),
            new KeycodeService(),
            vm.IsEditMode,
            vm.IsEditMode ? vm.ApplyTapDanceEdit : null);

        var dialog = new TapDanceEditorDialog
        {
            DataContext = tdVm,
            Topmost = vm.IsAlwaysOnTop,
        };
        tdVm.Closed = () => dialog.Close();
        tdVm.RequestKeyPick = (onApply, allowed) =>
            ShowKeyPickerAsync(vm, dialog, onApply, allowed);

        return dialog.ShowDialog(owner);
    }

    /// <summary>Prompts for a snapshot label. Null return = user cancelled.</summary>
    public static async Task<string?> PromptSnapshotLabelAsync(
        MainWindowViewModel vm, Window owner)
    {
        var dialog = new Window
        {
            Title = Loc.Instance["Edit_ManualSnapshot"],
            Width = 350,
            Height = 140,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Topmost = vm.IsAlwaysOnTop,
        };
        string? result = null;
        var textBox = new TextBox
        {
            Watermark = Loc.Instance["Edit_ManualSnapshotPrompt"],
            Margin = new Thickness(0, 0, 0, 8),
        };
        var cancelBtn = new Button
        {
            Content = Loc.Instance["Common_Cancel"],
            Margin = new Thickness(0, 0, 8, 0),
        };
        var okBtn = new Button
        {
            Content = Loc.Instance["Common_Save"],
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        cancelBtn.Click += (_, _) => dialog.Close();
        okBtn.Click += (_, _) =>
        {
            result = textBox.Text ?? "";
            dialog.Close();
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(cancelBtn);
        buttons.Children.Add(okBtn);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(textBox);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        await dialog.ShowDialog(owner);
        return result;
    }

    /// <summary>
    /// Prompts for a custom label on a raw keycode. Writes through
    /// <see cref="MainWindowViewModel.SaveCustomKeyLabel"/> on Save.
    /// </summary>
    public static async Task PromptKeyLabelAsync(
        MainWindowViewModel vm, Window owner, KeyViewModel keyVm)
    {
        var dialog = new Window
        {
            Title = Loc.Instance.Format("Title_SetLabelFormat", keyVm.HexKeycode),
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Topmost = vm.IsAlwaysOnTop,
        };

        var textBox = new TextBox
        {
            Watermark = Loc.Instance["Key_LabelWatermark"],
            Text = keyVm.Key.IsUnknown ? "" : keyVm.DisplayLabel,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var saveBtn = new Button
        {
            Content = Loc.Instance["Common_Save"],
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var info = new TextBlock
        {
            Text = Loc.Instance.Format("Key_LabelInfoFormat", keyVm.HexKeycode, keyVm.DisplayLabel),
            Foreground = Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
        };

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(info);
        panel.Children.Add(textBox);
        panel.Children.Add(saveBtn);
        dialog.Content = panel;

        saveBtn.Click += (_, _) =>
        {
            vm.SaveCustomKeyLabel(keyVm.HexKeycode, textBox.Text ?? "");
            dialog.Close();
        };

        await dialog.ShowDialog(owner);
    }
}
