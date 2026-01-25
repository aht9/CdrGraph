using System.Windows;
using CdrGraph.Core.Common;
using CdrGraph.Core.Domain.Models;
using CdrGraph.Core.Interfaces;

namespace CdrGraph.Desktop.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly IExcelReaderService _excelService;
    private readonly IGraphLayoutService _layoutService;

    private object _currentView;

    public object CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public MainViewModel(IExcelReaderService excelService, IGraphLayoutService layoutService)
    {
        _excelService = excelService;
        _layoutService = layoutService;
        CurrentView = new ImportViewModel(_excelService, this);
    }

    public async Task StartMultiFileGraphProcessingAsync(List<ExcelFileWrapper> files)
    {
        try
        {
            var allRecords = new List<CdrRecord>();

            // 1. خواندن تمام فایل‌ها
            foreach (var file in files)
            {
                if (string.IsNullOrEmpty(file.SelectedSource) || string.IsNullOrEmpty(file.SelectedTarget)) continue;

                var mapping = new ColumnMapping
                {
                    SourceColumn = file.SelectedSource,
                    TargetColumn = file.SelectedTarget,
                    DurationColumn = file.SelectedDuration
                };

                // فرض: متد ParseFileAsync در سرویس باید طوری باشد که نام فایل و متادیتا را هم پر کند
                var records = await _excelService.ParseFileAsync(file.FilePath, mapping);
                allRecords.AddRange(records);
            }

            if (!allRecords.Any())
            {
                MessageBox.Show("No records found.");
                return;
            }

            // 2. تجمیع داده‌ها
            var (nodes, edges) = ProcessDataAggregated(allRecords);

            // 3. محاسبه لی‌اوت
            await _layoutService.ApplyLayoutAsync(nodes, edges);

            // 4. نمایش گراف
            CurrentView = new GraphViewModel(nodes, edges);
        }
        catch (System.Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}");
        }
    }

    private (List<GraphNode> nodes, List<GraphEdge> edges) ProcessDataAggregated(List<CdrRecord> records)
    {
        var nodeDict = new Dictionary<string, GraphNode>();
        var edgeDict = new Dictionary<string, GraphEdge>();

        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.SourceNumber) || string.IsNullOrWhiteSpace(r.TargetNumber)) continue;

            // مدیریت نودها
            if (!nodeDict.ContainsKey(r.SourceNumber)) nodeDict[r.SourceNumber] = new GraphNode(r.SourceNumber);
            if (!nodeDict.ContainsKey(r.TargetNumber)) nodeDict[r.TargetNumber] = new GraphNode(r.TargetNumber);

            var sNode = nodeDict[r.SourceNumber];
            var tNode = nodeDict[r.TargetNumber];

            sNode.AddMetrics(1, r.DurationSeconds);
            tNode.AddMetrics(1, r.DurationSeconds);

            // افزودن رکورد به لیست جزئیات نودها
            sNode.AddRecord(r);
            tNode.AddRecord(r);

            // مدیریت یال‌ها (بدون جهت: A_B == B_A)
            var id1 = string.Compare(r.SourceNumber, r.TargetNumber) < 0 ? r.SourceNumber : r.TargetNumber;
            var id2 = string.Compare(r.SourceNumber, r.TargetNumber) < 0 ? r.TargetNumber : r.SourceNumber;
            var edgeKey = $"{id1}_{id2}";

            if (!edgeDict.ContainsKey(edgeKey)) edgeDict[edgeKey] = new GraphEdge(id1, id2);
            edgeDict[edgeKey].AddInteraction(r.DurationSeconds);
        }

        // نرمال‌سازی گرافیکی
        var edges = edgeDict.Values.ToList();
        if (edges.Any())
        {
            var maxCalls = edges.Max(e => e.CallCount);
            foreach (var e in edges)
            {
                e.CalculatedWeight = (double)e.CallCount / maxCalls;
                e.Thickness = (float)(1 + (e.CalculatedWeight * 7));
            }
        }

        var nodes = nodeDict.Values.ToList();
        if (nodes.Any())
        {
            var maxCalls = nodes.Max(n => n.TotalCalls);
            foreach (var n in nodes) n.Weight = (double)n.TotalCalls / maxCalls;
        }

        return (nodes, edges);
    }

    // جهت سازگاری
    public Task StartGraphProcessingAsync(string f, ColumnMapping m) => Task.CompletedTask;
}