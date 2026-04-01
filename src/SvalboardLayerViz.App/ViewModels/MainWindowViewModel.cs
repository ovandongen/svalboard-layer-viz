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
            Console.WriteLine("[DEBUG] Finding Vial devices...");
            var devices = _deviceService.FindVialDevices();
            Console.WriteLine($"[DEBUG] Found {devices.Count} device(s)");
            if (devices.Count == 0)
            {
                StatusMessage = "No Svalboard found. Connect your device via USB.";
                IsConnected = false;
                return;
            }

            // Use the first Vial device found
            var device = devices[0];
            Console.WriteLine($"[DEBUG] Connecting to {device.ProductName} (VID:{device.VendorId:X4} PID:{device.ProductId:X4})...");
            StatusMessage = $"Connecting to {device.ProductName}...";

            var loader = new KeymapLoader(_protocolService, _keycodeService);
            Console.WriteLine("[DEBUG] Loading keymap...");
            KeyboardConfig = loader.Load(device);
            Console.WriteLine($"[DEBUG] Loaded {KeyboardConfig.Layers.Count} layers, {KeyboardConfig.MatrixRows}x{KeyboardConfig.MatrixCols} matrix");

            // Build layer view models
            Layers.Clear();
            foreach (var layer in KeyboardConfig.Layers)
            {
                Layers.Add(new LayerViewModel(layer, i => SelectedLayerIndex = i));
            }
            Console.WriteLine($"[DEBUG] Built {Layers.Count} layer VMs, first layer has {Layers[0].Keys.Count} keys");

            SelectedLayerIndex = 0;
            IsConnected = true;
            StatusMessage = $"Connected: {device.ProductName} — {KeyboardConfig.Layers.Count} layers";
            Console.WriteLine($"[DEBUG] Success: {StatusMessage}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] ERROR: {ex}");
            StatusMessage = $"Connection error: {ex.Message}";
            IsConnected = false;
        }
    }

    [RelayCommand]
    private void SelectLayer(int index) => SelectedLayerIndex = index;

    [RelayCommand]
    private void Show()
    {
        // TODO: Show/focus the main window from tray
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
