using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Tiling;

namespace EncDotNet.S100.Viewer;

public partial class MainWindow : Window
{
    private static readonly string[] SupportedSpecs = ["S-101", "S-102"];

    private readonly ViewerSettings _settings;
    private readonly PortrayalCatalogueManager _catalogueManager = new();
    private readonly DatasetPipelineFactory _pipelineFactory;

    public MainWindow()
    {
        InitializeComponent();

        _settings = ViewerSettings.Load();

        // Seed catalogue manager from persisted settings
        foreach (var (spec, path) in _settings.CataloguePaths)
        {
            if (Directory.Exists(path))
            {
                _catalogueManager.SetPath(spec, path);
            }
        }

        _pipelineFactory = new DatasetPipelineFactory(
            _catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            spec => _settings.FeatureCataloguePaths.TryGetValue(spec, out var p) ? p : null);

        UpdatePortrayalButtonText();
        MapControl.Map?.Layers.Add(OpenStreetMap.CreateTileLayer());

        // Auto-load dataset from command-line argument if provided
        var cliArgs = Environment.GetCommandLineArgs();
        if (cliArgs.Length > 1 && File.Exists(cliArgs[1]))
        {
            Opened += async (_, _) => await LoadDatasetAsync(cliArgs[1]);
        }
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open S-100 Dataset",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("S-101 Files (ISO 8211)") { Patterns = ["*.000"] },
                new FilePickerFileType("S-102 Files (HDF5)") { Patterns = ["*.h5", "*.H5", "*.hdf5"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] },
            ],
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (path is null)
            return;

        await LoadDatasetAsync(path);
    }

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        RemoveDatasetLayers();
        ClearButton.IsEnabled = false;
        SetStatus(null);
    }

    private async void OnPortrayalClick(object? sender, RoutedEventArgs e)
    {
        // Let the user pick which product spec to configure
        var specOptions = SupportedSpecs.Select(s =>
        {
            var current = _catalogueManager.GetPath(s);
            var label = current is not null
                ? $"{s} — {Path.GetFileName(current.TrimEnd(Path.DirectorySeparatorChar))}"
                : $"{s} — (not set)";
            return label;
        }).ToArray();

        // Simple approach: cycle through specs or use a sub-window.
        // For now, ask user to pick a folder and detect which spec it belongs to,
        // or let user assign it to a specific spec via folder name convention.
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Portrayal Catalogue Folder",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
            return;

        var folderPath = folders[0].TryGetLocalPath();
        if (folderPath is null)
            return;

        // Detect the product spec from the catalogue XML
        string? detectedSpec = DetectCatalogueSpec(folderPath);

        if (detectedSpec is null)
        {
            SetStatus($"Could not detect product spec from catalogue at {folderPath}");
            return;
        }

        _catalogueManager.SetPath(detectedSpec, folderPath);
        _settings.CataloguePaths[detectedSpec] = folderPath;
        _settings.Save();
        UpdatePortrayalButtonText();
        SetStatus($"{detectedSpec} portrayal catalogue: {folderPath}");
    }

    private static string? DetectCatalogueSpec(string folderPath)
    {
        // Try to read the portrayal_catalogue.xml to get the productId
        var cataloguePath = Path.Combine(folderPath, "portrayal_catalogue.xml");
        if (!File.Exists(cataloguePath))
            return null;

        try
        {
            using var stream = File.OpenRead(cataloguePath);
            var catalogue = PortrayalCatalogueReader.Read(stream);
            return string.IsNullOrEmpty(catalogue.ProductId) ? null : catalogue.ProductId;
        }
        catch
        {
            return null;
        }
    }

    private void UpdatePortrayalButtonText()
    {
        var configured = _catalogueManager.RegisteredCatalogues;
        if (configured.Count == 0)
        {
            PortrayalButton.Content = "Portrayal Catalogues...";
        }
        else
        {
            var labels = configured.Keys.OrderBy(k => k).ToArray();
            PortrayalButton.Content = $"Portrayal: {string.Join(", ", labels)}";
        }
    }

    private async Task LoadDatasetAsync(string path)
    {
        var spec = DatasetPipelineFactory.DetectProductSpec(path);
        if (spec is null)
        {
            SetStatus($"Unrecognized file type: {Path.GetExtension(path)}");
            return;
        }

        if (!_catalogueManager.HasCatalogue(spec))
        {
            SetStatus($"Please select a portrayal catalogue for {spec} first.");
            return;
        }

        SetStatus($"Loading {Path.GetFileName(path)}...");
        OpenButton.IsEnabled = false;

        try
        {
            var result = await Task.Run(() => _pipelineFactory.Process(path));

            RemoveDatasetLayers();
            MapControl.Map?.Layers.Add(result.Layer);

            if (MapControl.Map?.Navigator is { } nav)
            {
                nav.ZoomToBox(result.Extent.Grow(result.Extent.Width * 0.1, result.Extent.Height * 0.1));
            }

            ClearButton.IsEnabled = true;
            SetStatus(result.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            Console.Error.WriteLine($"Failed to load {path}:\n{ex}");
        }
        finally
        {
            OpenButton.IsEnabled = true;
        }
    }

    private void RemoveDatasetLayers()
    {
        if (MapControl.Map is not { } map)
            return;

        // Remove any layers added by dataset processing (named "S-1xx: ...")
        var toRemove = map.Layers
            .Where(l => l.Name?.StartsWith("S-10", StringComparison.Ordinal) == true)
            .ToList();

        foreach (var layer in toRemove)
        {
            map.Layers.Remove(layer);
        }
    }

    private void SetStatus(string? message)
    {
        if (message is null)
        {
            StatusBorder.IsVisible = false;
        }
        else
        {
            StatusText.Text = message;
            StatusBorder.IsVisible = true;
        }
    }
}
