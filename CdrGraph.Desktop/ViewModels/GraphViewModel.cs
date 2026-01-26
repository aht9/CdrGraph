using System.Collections.ObjectModel;
using CdrGraph.Core.Common;
using CdrGraph.Core.Domain.Models;
using CdrGraph.Infrastructure.Services;
using SkiaSharp;

namespace CdrGraph.Desktop.ViewModels;

public class GraphViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    private readonly CdrDataService _dataService;

    public List<GraphNode> Nodes { get; }
    public List<GraphEdge> Edges { get; }

    private string _statusText;

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    private GraphNode _selectedNode;

    public GraphNode SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                // فراخوانی متد آسنکرون برای دریافت جزئیات
                _ = UpdateDetailViewAsync();
            }
        }
    }

    // لیستی از داده‌ها برای نمایش در پنل کناری
    public ObservableCollection<RecordDisplayModel> SelectedNodeRecords { get; } =
        new ObservableCollection<RecordDisplayModel>();

    // دستور بازگشت به صفحه اصلی (Reset)
    public RelayCommand ResetCommand { get; }


    // --- ویژگی‌های جدید UI ---

    // 1. Zoom Levels
    public List<string> ZoomLevels { get; } = new List<string> { "50%", "75%", "100%", "125%", "150%", "200%", "300%" };

    private string _selectedZoom = "100%";

    public string SelectedZoom
    {
        get => _selectedZoom;
        set => SetProperty(ref _selectedZoom, value); // View به این گوش می‌دهد
    }

    // 2. Themes
    public List<string> Themes { get; } = new List<string> { "Dark Mode", "Light Mode" };

    private string _selectedTheme = "Dark Mode";

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
                // اطلاع‌رسانی وضعیت تم (View این را هندل می‌کند)
                IsLightTheme = value == "Light Mode";
            }
        }
    }

    private bool _isLightTheme;

    public bool IsLightTheme
    {
        get => _isLightTheme;
        set => SetProperty(ref _isLightTheme, value);
    }

    // -------------------------

    // سازنده: دریافت MainViewModel برای قابلیت ریست
    public GraphViewModel(List<GraphNode> nodes, List<GraphEdge> edges, MainViewModel mainViewModel,
        CdrDataService dataService)
    {
        Nodes = nodes;
        Edges = edges;
        _mainViewModel = mainViewModel;
        _dataService = dataService;
        StatusText = $"{nodes.Count} Nodes, {edges.Count} Edges";
        ResetCommand = new RelayCommand(_ => _mainViewModel.ResetApplication());
    }

    private async Task UpdateDetailViewAsync()
    {
        SelectedNodeRecords.Clear();
        if (SelectedNode == null) return;

        // دریافت داده‌ها از دیتابیس (نه از رم)
        var records = await _dataService.GetRecordsForNodeAsync(SelectedNode.Id);

        foreach (var record in records)
        {
            SelectedNodeRecords.Add(new RecordDisplayModel
            {
                Source = record.SourceNumber,
                Target = record.TargetNumber,
                Duration = record.DurationSeconds,
                File = record.OriginFileName,
                Date = record.DateStr,
                Time = record.TimeStr
            });
        }
    }
}

// مدل ساده برای نمایش در DataGrid
public class RecordDisplayModel
{
    public string Source { get; set; }
    public string Target { get; set; }
    public double Duration { get; set; }
    public string File { get; set; }
    public string Date { get; set; }
    public string Time { get; set; }
}