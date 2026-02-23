using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;
using CdrGraph.Core.Common;
using CdrGraph.Core.Domain.Models;
using CdrGraph.Infrastructure.Services;
using SkiaSharp;

namespace CdrGraph.Desktop.ViewModels;

public class GraphViewModel : ObservableObject
{
    private readonly MainViewModel _mainViewModel;
    private readonly CdrDataService _dataService;
    private readonly PdfReportService _reportService;

    public List<GraphNode> Nodes { get; }
    public List<GraphEdge> Edges { get; }

    // لیست انتخاب چندگانه (Multi-Selection)
    public ObservableCollection<GraphNode> SelectedNodes { get; } = new ObservableCollection<GraphNode>();

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
                _ = UpdateDetailViewAsync();
            }
        }
    }

    public ObservableCollection<RecordDisplayModel> SelectedNodeRecords { get; } =
        new ObservableCollection<RecordDisplayModel>();

    // --- پراپرتی‌های رابط کاربری (UI) ---
    public List<string> ZoomLevels { get; } = new List<string> { "50%", "75%", "100%", "125%", "150%", "200%", "300%" };

    private string _selectedZoom = "100%";

    public string SelectedZoom
    {
        get => _selectedZoom;
        set => SetProperty(ref _selectedZoom, value);
    }

    public List<string> Themes { get; } = new List<string> { "Dark Mode", "Light Mode" };

    private string _selectedTheme = "Dark Mode";

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
            {
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

    private bool _reportByDuration;

    public bool ReportByDuration
    {
        get => _reportByDuration;
        set => SetProperty(ref _reportByDuration, value);
    }

    private string _searchText;

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    // Delegate برای درخواست تولید تصویر از View (بدون وابستگی مستقیم به UI)
    public Func<List<GraphNode>, List<GraphEdge>, bool, byte[]> SubGraphImageGenerator { get; set; }
    public Action<GraphNode> RequestFocusOnNode { get; set; } // درخواست زوم روی نود از View

    // --- دستورات (Commands) ---
    public RelayCommand ResetCommand { get; }
    public RelayCommand GenerateCommonReportCommand { get; }
    public RelayCommand SearchCommand { get; }


    public GraphViewModel(List<GraphNode> nodes, List<GraphEdge> edges, MainViewModel mainViewModel,
        CdrDataService dataService)
    {
        Nodes = nodes;
        Edges = edges;
        _mainViewModel = mainViewModel;
        _dataService = dataService;
        _reportService = new PdfReportService();

        StatusText = $"{nodes.Count} Nodes, {edges.Count} Edges";

        ResetCommand = new RelayCommand(_ => _mainViewModel.ResetApplication());

        // گزارش مشترکات فقط وقتی فعال است که حداقل ۲ نود انتخاب شده باشد
        GenerateCommonReportCommand = new RelayCommand(GenerateCommonReport, _ => SelectedNodes.Count > 1);

        // Search Command
        SearchCommand = new RelayCommand(PerformSearch);
    }
    
    private void PerformSearch(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return;

        // جستجوی ساده (حاوی متن)
        var targetNode = Nodes.FirstOrDefault(n => n.Id.Contains(SearchText));

        if (targetNode != null)
        {
            // 1. انتخاب نود
            ToggleNodeSelection(targetNode, false); // false = single selection
                
            // 2. درخواست از View برای زوم و پن کردن روی نود
            RequestFocusOnNode?.Invoke(targetNode);
        }
        else
        {
            MessageBox.Show($"Node '{SearchText}' not found.", "Search Result");
        }
    }

    // متد مدیریت انتخاب (توسط View صدا زده می‌شود)
    public void ToggleNodeSelection(GraphNode node, bool multiSelect)
    {
        if (!multiSelect)
        {
            SelectedNodes.Clear();
            SelectedNodes.Add(node);
        }
        else
        {
            if (SelectedNodes.Contains(node)) SelectedNodes.Remove(node);
            else SelectedNodes.Add(node);
        }

        SelectedNode = SelectedNodes.LastOrDefault();
        GenerateCommonReportCommand.RaiseCanExecuteChanged();
    }

    // گزارش مشترکات (Sub-Graph Report)
    private void GenerateCommonReport(object param)
    {
        if (SelectedNodes.Count < 2) return;

        // 1. منطق پیدا کردن نودهای مشترک بین انتخاب‌شده‌ها
        var firstNodeNeighbors = GetNeighbors(SelectedNodes[0].Id);
        var intersection = new HashSet<string>(firstNodeNeighbors);

        for (int i = 1; i < SelectedNodes.Count; i++)
        {
            var neighbors = GetNeighbors(SelectedNodes[i].Id);
            intersection.IntersectWith(neighbors);
        }

        if (!intersection.Any())
        {
            MessageBox.Show("No common connections found between selected nodes.", "Report Info");
            return;
        }

        // 2. آماده‌سازی داده‌ها برای گزارش
        var reportNodes = new List<GraphNode>();
        reportNodes.AddRange(SelectedNodes); // نودهای اصلی
        reportNodes.AddRange(Nodes.Where(n => intersection.Contains(n.Id))); // نودهای مشترک

        var reportNodeIds = new HashSet<string>(reportNodes.Select(n => n.Id));
        // فقط خطوطی که بین این نودها هستند
        var reportEdges = Edges.Where(e => reportNodeIds.Contains(e.SourceId) && reportNodeIds.Contains(e.TargetId))
            .ToList();

        // 3. درخواست تولید تصویر از View
        byte[] graphImage = null;
        if (SubGraphImageGenerator != null)
        {
            graphImage = SubGraphImageGenerator(reportNodes, reportEdges, ReportByDuration);
        }

        // 4. ذخیره و تولید PDF
        var saveDialog = new SaveFileDialog
        {
            Filter = "PDF Report|*.pdf",
            FileName = $"CommonReport_{System.DateTime.Now:yyyyMMdd_HHmm}",
            Title = "Save Common Connections Report"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                _reportService.GenerateCommonConnectionReport(
                    saveDialog.FileName,
                    reportNodes,
                    reportEdges,
                    SelectedNodes.ToList(),
                    ReportByDuration,
                    graphImage
                );

                MessageBox.Show("Report generated successfully!", "Success");
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(saveDialog.FileName)
                        { UseShellExecute = true });
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving report: {ex.Message}", "Error");
            }
        }
    }

    // *** متد جدید: اکسپورت گزارش جامع (Full Report) ***
    public async Task ExportComprehensiveReportAsync(byte[] graphImage)
    {
        var saveDialog = new SaveFileDialog
        {
            Filter = "PDF Report|*.pdf",
            FileName = $"FullReport_{System.DateTime.Now:yyyyMMdd_HHmm}",
            Title = "Save Comprehensive Analysis Report"
        };

        if (saveDialog.ShowDialog() == true)
        {
            try
            {
                // 1. دریافت داده‌های تحلیلی از دیتابیس
                var reportData = await _dataService.GetComprehensiveReportDataAsync();

                // 2. تولید گزارش
                _reportService.GenerateComprehensiveReport(saveDialog.FileName, graphImage, reportData);

                MessageBox.Show("Comprehensive Report Generated Successfully!", "Success");
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(saveDialog.FileName)
                        { UseShellExecute = true });
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export Error: {ex.Message}");
            }
        }
    }

    private IEnumerable<string> GetNeighbors(string nodeId)
    {
        return Edges.Where(e => e.SourceId == nodeId).Select(e => e.TargetId)
            .Concat(Edges.Where(e => e.TargetId == nodeId).Select(e => e.SourceId));
    }

    private async Task UpdateDetailViewAsync()
    {
        SelectedNodeRecords.Clear();
        if (SelectedNode == null) return;

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

public class RecordDisplayModel
{
    public string Source { get; set; }
    public string Target { get; set; }
    public double Duration { get; set; }
    public string File { get; set; }
    public string Date { get; set; }
    public string Time { get; set; }
}