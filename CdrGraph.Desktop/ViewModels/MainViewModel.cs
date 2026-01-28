using System.Windows;
using CdrGraph.Core.Common;
using CdrGraph.Core.Domain.Models;
using CdrGraph.Core.Interfaces;
using CdrGraph.Infrastructure.Services;

namespace CdrGraph.Desktop.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly IExcelReaderService _excelService;
    private readonly IGraphLayoutService _layoutService;
    private readonly CdrDataService _dataService;
    private object _currentView;

    public object CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    public MainViewModel(IExcelReaderService excelService, IGraphLayoutService layoutService,
        CdrDataService dataService)
    {
        _excelService = excelService;
        _layoutService = layoutService;
        _dataService = dataService;

        // شروع اولیه
        CurrentView = new ImportViewModel(_excelService, this);
    }

    public async Task StartMultiFileGraphProcessingAsync(List<ExcelFileWrapper> files, int userMaxNodes,
        bool isCommonMode)
    {
        var fileColorMap = files.ToDictionary(f => f.FileName, f => f.SelectedFileColor);

        await Task.Run(async () =>
        {
            try
            {
                await _dataService.ClearAllDataAsync();

                // 1. درج داده‌ها (Load) - مشترک برای هر دو حالت
                foreach (var file in files)
                {
                    if (string.IsNullOrEmpty(file.SelectedSource) || string.IsNullOrEmpty(file.SelectedTarget))
                        continue;

                    var mapping = new ColumnMapping
                    {
                        SourceColumn = file.SelectedSource,
                        TargetColumn = file.SelectedTarget,
                        DurationColumn = file.SelectedDuration,
                        DateColumn = file.SelectedDate,
                        TimeColumn = file.SelectedTime
                    };

                    var records = await _excelService.ParseFileAsync(file.FilePath, mapping);
                    await _dataService.BulkInsertFastAsync(records);
                    records = null;
                    GC.Collect();
                }

                List<GraphNode> nodes;
                List<GraphEdge> edges;

                // *** گام جدید: شناسایی نودهای اصلی فایل‌ها ***
                var mainNodesMap = await _dataService.GetMainNodesPerFileAsync();
                var mainNodeIds = mainNodesMap.Values.ToHashSet();

                // 2. انشعاب منطق (Branching Logic)
                if (isCommonMode)
                {
                    // الف) حالت مشترکات: فقط نودهایی که در >1 فایل بوده‌اند
                    var result = await _dataService.GetCommonNodesAndEdgesAsync(fileColorMap);
                    nodes = result.nodes;
                    edges = result.edges;

                    if (!nodes.Any())
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show("No common connections found between these files.", "Result"));
                        return;
                    }

                    Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show($"Found {nodes.Count} common entities crossing between files.",
                            "Intersection Analysis"));
                }
                else
                {
                    nodes = await _dataService.GetAggregatedNodesAsync(fileColorMap);
                    edges = await _dataService.GetAggregatedEdgesAsync();

                    // *** فیلترینگ هوشمند با حفظ نودهای اصلی ***
                    if (nodes.Count > userMaxNodes)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                            MessageBox.Show($"Filtering to top {userMaxNodes} nodes (keeping Main File Hubs)."));

                        // همیشه نودهای اصلی فایل‌ها را نگه دار، حتی اگر تماس کمتری داشته باشند
                        var mustKeepNodes = nodes.Where(n => mainNodeIds.Contains(n.Id)).ToList();

                        // بقیه ظرفیت را با پرتماس‌ترین‌ها پر کن
                        var otherNodes = nodes
                            .Where(n => !mainNodeIds.Contains(n.Id))
                            .OrderByDescending(n => n.TotalCalls)
                            .Take(userMaxNodes - mustKeepNodes.Count)
                            .ToList();

                        nodes = mustKeepNodes.Concat(otherNodes).ToList();

                        var allowedIds = new HashSet<string>(nodes.Select(n => n.Id));
                        edges = edges.Where(e => allowedIds.Contains(e.SourceId) && allowedIds.Contains(e.TargetId))
                            .ToList();
                    }
                }

                // رنگ‌دهی ویژه به نودهای اصلی (مثلاً بزرگتر یا با حاشیه طلایی - اینجا با تگ خاص مشخص می‌کنیم)
                foreach (var n in nodes)
                {
                    if (mainNodeIds.Contains(n.Id))
                    {
                        n.Weight += 0.5; // کمی بزرگتر کردن نودهای اصلی
                        n.Label = $"★ {n.Label}"; // ستاره دار کردن (اختیاری)
                    }
                }

                // 3. محاسبه لی‌اوت و نمایش
                await _layoutService.ApplyLayoutAsync(nodes, edges);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentView = new GraphViewModel(nodes, edges, this, _dataService);
                });

                GC.Collect();
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => MessageBox.Show($"Error: {ex.Message}"));
            }
        });
    }

    // متد جدید برای بازنشانی برنامه
    public void ResetApplication()
    {
        // 1. حذف ویو فعلی (گراف سنگین)
        CurrentView = null;

        // 2. درخواست صریح از Garbage Collector برای خالی کردن رم
        // (در برنامه‌های پردازش داده سنگین این کار توجیه‌پذیر است)
        GC.Collect();
        GC.WaitForPendingFinalizers();

        // 3. ساختن ویو جدید برای شروع مجدد
        CurrentView = new ImportViewModel(_excelService, this);
    }

    private (List<GraphNode> nodes, List<GraphEdge> edges) ProcessDataAggregated(List<CdrRecord> records)
    {
        var nodeDict = new Dictionary<string, GraphNode>();
        var edgeDict = new Dictionary<string, GraphEdge>();

        foreach (var r in records)
        {
            if (string.IsNullOrWhiteSpace(r.SourceNumber) || string.IsNullOrWhiteSpace(r.TargetNumber)) continue;

            if (!nodeDict.ContainsKey(r.SourceNumber)) nodeDict[r.SourceNumber] = new GraphNode(r.SourceNumber);
            if (!nodeDict.ContainsKey(r.TargetNumber)) nodeDict[r.TargetNumber] = new GraphNode(r.TargetNumber);

            var sNode = nodeDict[r.SourceNumber];
            var tNode = nodeDict[r.TargetNumber];

            sNode.AddMetrics(1, r.DurationSeconds);
            tNode.AddMetrics(1, r.DurationSeconds);
            sNode.AddRecord(r);
            tNode.AddRecord(r);

            var id1 = string.Compare(r.SourceNumber, r.TargetNumber) < 0 ? r.SourceNumber : r.TargetNumber;
            var id2 = string.Compare(r.SourceNumber, r.TargetNumber) < 0 ? r.TargetNumber : r.SourceNumber;
            var edgeKey = $"{id1}_{id2}";

            if (!edgeDict.ContainsKey(edgeKey)) edgeDict[edgeKey] = new GraphEdge(id1, id2);
            edgeDict[edgeKey].AddInteraction(r.DurationSeconds);
        }

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
}