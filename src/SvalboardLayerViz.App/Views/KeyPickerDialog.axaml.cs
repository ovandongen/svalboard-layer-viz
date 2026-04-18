using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using SvalboardLayerViz.App.ViewModels;
using SvalboardLayerViz.Core.Keymap;

namespace SvalboardLayerViz.App.Views;

public partial class KeyPickerDialog : Window
{
    private PickerSessionViewModel? _vm;

    public KeyPickerDialog()
    {
        InitializeComponent();

        CancelButton.Click += (_, _) => Close();
        ApplyButton.Click += (_, _) => Close();

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= OnVmPropertyChanged;

            _vm = DataContext as PickerSessionViewModel;
            if (_vm is not null)
                _vm.PropertyChanged += OnVmPropertyChanged;
        };

        RawHexInput.TextChanged += OnRawHexTextChanged;
        Opened += (_, _) => UpdateRecentlyUsed();
    }

    // Recently-used strip is refreshed whenever the user lands back on Preview
    // (after a confirm-cycle or a hydrated raw hex entry).
    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PickerSessionViewModel.IsStepPreview))
            UpdateRecentlyUsed();
    }

    private void OnRawHexTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_vm is null) return;
        var text = RawHexInput.Text?.Trim() ?? "";
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        if (ushort.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            _vm.SetRawKeycode(code);
    }

    private static readonly KeycodeService _keycodeService = new();

    private void UpdateRecentlyUsed()
    {
        RecentlyUsedPanel.ItemsSource = PickerSessionViewModel.RecentlyUsed
            .Take(10)
            .Select(code =>
            {
                var info = _keycodeService.Resolve(code);
                var label = string.IsNullOrEmpty(info.Label) ? $"0x{code:X4}" : info.Label;
                return new { Code = code, Label = label, Secondary = info.SecondaryLabel };
            })
            .ToList();

        RecentlyUsedPanel.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) =>
        {
            var t = item.GetType();
            var code = (ushort)t.GetProperty("Code")!.GetValue(item)!;
            var label = (string)t.GetProperty("Label")!.GetValue(item)!;
            var secondary = (string?)t.GetProperty("Secondary")!.GetValue(item);

            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (!string.IsNullOrEmpty(secondary))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = secondary,
                    FontSize = 8,
                    Foreground = Avalonia.Media.Brushes.LightGray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
            }
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            var btn = new Button
            {
                Content = stack,
                Width = 64,
                Height = 40,
                Margin = new Avalonia.Thickness(2),
                Padding = new Avalonia.Thickness(4, 2),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            btn.Click += (_, _) => _vm?.SetRawKeycode(code);
            return btn;
        });
    }
}
