using System.Collections.ObjectModel;
using System.Windows; // برای MessageBox
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

    // لیست انتخاب چندگانه
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

    // --- پراپرتی‌های مورد نیاز برای رفع خطا ---

    // 1. Zoom Properties
    public List<string> ZoomLevels { get; } = new List<string> { "50%", "75%", "100%", "125%", "150%", "200%", "300%" };

    private string _selectedZoom = "100%";

    public string SelectedZoom
    {
        get => _selectedZoom;
        set => SetProperty(ref _selectedZoom, value);
    }

    // 2. Theme Properties
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

    // 3. Report Properties
    private bool _reportByDuration;

    public bool ReportByDuration
    {
        get => _reportByDuration;
        set => SetProperty(ref _reportByDuration, value);
    }

    // --- Commands ---
    public RelayCommand ResetCommand { get; }
    public RelayCommand GenerateCommonReportCommand { get; }

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
        GenerateCommonReportCommand = new RelayCommand(GenerateCommonReport, _ => SelectedNodes.Count > 1);
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

    // *** ویژگی جدید: اینDelegate توسط View مقداردهی می‌شود ***
    // ورودی‌ها: نودها، یال‌ها، آیا مدت زمان نمایش داده شود؟ -> خروجی: آرایه بایت تصویر
    public Func<List<GraphNode>, List<GraphEdge>, bool, byte[]> SubGraphImageGenerator { get; set; }

    private void GenerateCommonReport(object param)
    {
        if (SelectedNodes.Count < 2) return;

        // 1. منطق پیدا کردن مشترکات
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

        // 2. آماده‌سازی داده‌ها
        var reportNodes = new List<GraphNode>();
        reportNodes.AddRange(SelectedNodes);
        reportNodes.AddRange(Nodes.Where(n => intersection.Contains(n.Id)));

        var reportNodeIds = new HashSet<string>(reportNodes.Select(n => n.Id));
        var reportEdges = Edges.Where(e => reportNodeIds.Contains(e.SourceId) && reportNodeIds.Contains(e.TargetId))
            .ToList();

        // 3. درخواست تولید تصویر از View
        byte[] graphImage = null;
        if (SubGraphImageGenerator != null)
        {
            graphImage = SubGraphImageGenerator(reportNodes, reportEdges, ReportByDuration);
        }

        // *** 4. باز کردن دیالوگ ذخیره فایل (تغییر جدید) ***
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
                string path = saveDialog.FileName;

                _reportService.GenerateCommonConnectionReport(
                    path,
                    reportNodes,
                    reportEdges,
                    SelectedNodes.ToList(),
                    ReportByDuration,
                    graphImage
                );

                MessageBox.Show("Report generated successfully!", "Success");

                // تلاش برای باز کردن فایل پس از ذخیره
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                        { UseShellExecute = true });
                }
                catch
                {
                    /* نادیده گرفتن خطا اگر فایل باز نشد */
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error saving report: {ex.Message}", "Error");
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