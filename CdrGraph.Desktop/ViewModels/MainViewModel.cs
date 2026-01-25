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

    public async Task StartMultiFileGraphProcessingAsync(List<ExcelFileWrapper> files, int userMaxNodes)
    {
        await Task.Run(async () =>
        {
            try
            {
                // 1. پاکسازی دیتابیس
                await _dataService.ClearAllDataAsync();

                // 2. پردازش و درج فایل‌ها
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

                // 3. دریافت داده‌های خام از دیتابیس
                var nodes = await _dataService.GetAggregatedNodesAsync();
                var edges = await _dataService.GetAggregatedEdgesAsync();

                if (!nodes.Any())
                {
                    Application.Current.Dispatcher.Invoke(() => MessageBox.Show("No data found."));
                    return;
                }

                // *** منطق فیلترینگ بر اساس انتخاب کاربر ***
                // اگر تعداد نودها بیشتر از حد انتخابی کاربر بود، فیلتر کن
                if (nodes.Count > userMaxNodes)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                        MessageBox.Show(
                            $"Found {nodes.Count} nodes. Filtering to top {userMaxNodes} active numbers based on your setting.",
                            "Performance Optimization"));

                    // الف) انتخاب نودهای مهم (پرتماس‌ترین‌ها)
                    var topNodes = nodes.OrderByDescending(n => n.TotalCalls)
                        .Take(userMaxNodes)
                        .ToList();

                    // ب) ساخت HashSet برای جستجوی سریع IDها
                    var topNodeIds = new HashSet<string>(topNodes.Select(n => n.Id));

                    // ج) فیلتر کردن یال‌ها (فقط خطوطی که هر دو سرشان جزو تاپ‌ها هستند)
                    var filteredEdges = edges
                        .Where(e => topNodeIds.Contains(e.SourceId) && topNodeIds.Contains(e.TargetId))
                        .ToList();

                    // جایگزینی لیست اصلی با لیست فیلتر شده
                    nodes = topNodes;
                    edges = filteredEdges;
                }

                // 4. محاسبه لی‌اوت
                await _layoutService.ApplyLayoutAsync(nodes, edges);

                // 5. نمایش گراف
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