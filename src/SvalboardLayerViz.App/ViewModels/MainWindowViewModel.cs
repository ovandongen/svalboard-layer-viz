using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SvalboardLayerViz.Core.Device;
using SvalboardLayerViz.Core.Keymap;
using SvalboardLayerViz.Core.Models;
using SvalboardLayerViz.Core.Protocol;

namespace SvalboardLayerViz.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly DeviceConnectionService _deviceService = new();
    private readonly VialProtocolService _protocolService = new();
    private readonly KeycodeService _keycodeService = new();
    private IDisposable? _deviceSubscription;

    [ObservableProperty]
    private KeyboardConfig? _keyboardConfig;

    [ObservableProperty]
    private int _selectedLayerIndex;

    public LayerViewModel? SelectedLayer => Layers.ElementAtOrDefault(SelectedLayerIndex);

    partial void OnSelectedLayerIndexChanged(int value) => OnPropertyChanged(nameof(SelectedLayer));

    [ObservableProperty]
    private string _statusMessage = "Looking for Svalboard...";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private ObservableCollection<LayerViewModel> _layers = [];

    public IReadOnlyList<ClusterViewModel> Clusters { get; } = ClusterViewModel.BuildFromLayout();

    public MainWindowViewModel()
    {
        // Try to connect on startup
        TryConnect();

        // Monitor for device changes (store subscription to prevent GC)
        _deviceSubscription = _deviceService.OnDeviceListChanged(TryConnect);
    }

    private void TryConnect()
    {
        try
        {
            var devices = _deviceService.FindVialDevices();
            if (devices.Count == 0)
            {
                StatusMessage = "No Svalboard found. Connect your device via USB.";
                IsConnected = false;
                return;
            }

            var device = devices[0];
            StatusMessage = $"Connecting to {device.ProductName}...";

            var loader = new KeymapLoader(_protocolService, _keycodeService);
            KeyboardConfig = loader.Load(device);

            Layers.Clear();
            var totalLayers = KeyboardConfig.Layers.Count;
            foreach (var layer in KeyboardConfig.Layers)
            {
                Layers.Add(new LayerViewModel(layer, i => SelectedLayerIndex = i, totalLayers));
            }

            SelectedLayerIndex = 0;
            IsConnected = true;
            StatusMessage = $"Connected: {device.ProductName} — {KeyboardConfig.Layers.Count} layers";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection error: {ex.Message}";
            IsConnected = false;
        }
    }

    [RelayCommand]
    private void SelectLayer(int index) => SelectedLayerIndex = index;

    /// <summary>Callback to show/focus the main window. Wired up by App.axaml.cs.</summary>
    public Action? ShowWindowRequested { get; set; }

    [RelayCommand]
    private void Show()
    {
        ShowWindowRequested?.Invoke();
    }

    [RelayCommand]
    private void Refresh()
    {
        TryConnect();
    }

    [RelayCommand]
    private void Quit()
    {
        _deviceSubscription?.Dispose();
        _protocolService.Dispose();
        Environment.Exit(0);
    }
}
