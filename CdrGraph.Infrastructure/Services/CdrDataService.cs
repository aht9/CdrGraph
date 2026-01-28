using CdrGraph.Core.Domain.Models;
using CdrGraph.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace CdrGraph.Infrastructure.Services;

public class CdrDataService
{
    private readonly AppDbContext _context;

    public CdrDataService(AppDbContext context)
    {
        _context = context;
        _context.Database.EnsureCreated();
    }

    public async Task ClearAllDataAsync()
    {
        // استفاده از دستور SQL مستقیم برای حذف آنی
        await _context.Database.ExecuteSqlRawAsync("DELETE FROM CdrRecords");
        // فشرده سازی فایل دیتابیس برای کاهش حجم
        await _context.Database.ExecuteSqlRawAsync("VACUUM");
        _context.ChangeTracker.Clear();
    }

    // درج فوق سریع با Transaction
    public async Task BulkInsertFastAsync(IEnumerable<CdrRecord> records)
    {
        // غیرفعال کردن رهگیری تغییرات (حیاتی برای سرعت)
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
        _context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        // شروع تراکنش: این کار سرعت SQLite را تا ۱۰۰ برابر افزایش می‌دهد
        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            int count = 0;
            foreach (var r in records)
            {
                // تبدیل مستقیم و اضافه کردن به کانتکست
                var entity = new CdrEntity
                {
                    SourceNumber = r.SourceNumber,
                    TargetNumber = r.TargetNumber,
                    Duration = r.DurationSeconds,
                    CallDate = r.CallDateTime,
                    DateStr = r.DateStr,
                    TimeStr = r.TimeStr,
                    FileName = r.OriginFileName
                };

                await _context.CdrRecords.AddAsync(entity);
                count++;

                // هر ۲۰۰۰ رکورد یکبار ذخیره کن تا رم پر نشود
                if (count % 2000 == 0)
                {
                    await _context.SaveChangesAsync();
                    _context.ChangeTracker.Clear(); // خالی کردن کش کانتکست
                }
            }

            // ذخیره باقیمانده‌ها
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            _context.ChangeTracker.Clear();
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
        }
    }

    public async Task<List<GraphNode>> GetAggregatedNodesAsync(Dictionary<string, string> fileColors)
    {
        // دریافت آمار تماس‌ها + فایل‌ها
        var rawData = await _context.CdrRecords
            .AsNoTracking()
            .Select(x => new { S = x.SourceNumber, T = x.TargetNumber, D = x.Duration, F = x.FileName })
            .ToListAsync();

        var nodeStats = new Dictionary<string, NodeStatDto>();

        // کلاس کمکی داخلی برای نگهداری آمار
        // HashSet برای Neighbors استفاده می‌شود تا همسایگان تکراری شمرده نشوند
        foreach (var item in rawData)
        {
            AddStat(nodeStats, item.S, item.D, item.F, item.T);
            AddStat(nodeStats, item.T, item.D, item.F, item.S);
        }

        var nodes = nodeStats.Select(kvp =>
        {
            var n = new GraphNode(kvp.Key);
            n.AddMetrics(kvp.Value.Calls, kvp.Value.Dur);

            // *** منطق جدید: تشخیص نودهای Hub (پل ارتباطی) ***
            // اگر نود با بیش از 2 شماره منحصر به فرد ارتباط دارد
            if (kvp.Value.UniqueNeighbors.Count > 2)
            {
                n.Color = "#8A2BE2"; // رنگ بنفش (BlueViolet) برای نودهای مشترک/مهم
            }
            else
            {
                // اگر Hub نبود، رنگ فایل اصلی را بگیرد
                var topFile = kvp.Value.FileCounts.OrderByDescending(x => x.Value).FirstOrDefault().Key;
                if (topFile != null && fileColors.ContainsKey(topFile))
                {
                    n.Color = fileColors[topFile];
                }
            }

            return n;
        }).ToList();

        // نرمال‌سازی وزن‌ها
        if (nodes.Any())
        {
            var maxCalls = nodes.Max(n => n.TotalCalls);
            foreach (var n in nodes) n.Weight = (double)n.TotalCalls / maxCalls;
        }

        return nodes;
    }

    // متد کمکی برای افزودن آمار
    private void AddStat(Dictionary<string, NodeStatDto> dict, string id, double dur, string file, string neighborId)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!dict.ContainsKey(id)) dict[id] = new NodeStatDto();

        var stat = dict[id];
        stat.Calls++;
        stat.Dur += dur;

        if (!stat.FileCounts.ContainsKey(file)) stat.FileCounts[file] = 0;
        stat.FileCounts[file]++;

        if (!string.IsNullOrEmpty(neighborId)) stat.UniqueNeighbors.Add(neighborId);
    }

    public async Task<List<GraphEdge>> GetAggregatedEdgesAsync()
    {
        var rawData = await _context.CdrRecords
            .AsNoTracking()
            .Select(x => new { S = x.SourceNumber, T = x.TargetNumber, D = x.Duration })
            .ToListAsync();

        var edgeDict = new Dictionary<string, GraphEdge>();

        foreach (var r in rawData)
        {
            if (string.IsNullOrEmpty(r.S) || string.IsNullOrEmpty(r.T)) continue;

            var id1 = string.Compare(r.S, r.T) < 0 ? r.S : r.T;
            var id2 = string.Compare(r.S, r.T) < 0 ? r.T : r.S;
            var key = $"{id1}_{id2}";

            if (!edgeDict.ContainsKey(key)) edgeDict[key] = new GraphEdge(id1, id2);
            edgeDict[key].AddInteraction(r.D);
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

        return edges;
    }

    public async Task<List<CdrRecord>> GetRecordsForNodeAsync(string nodeId)
    {
        return await _context.CdrRecords
            .AsNoTracking()
            .Where(x => x.SourceNumber == nodeId || x.TargetNumber == nodeId)
            .OrderByDescending(x => x.CallDate)
            .Take(200) // محدودیت برای جلوگیری از کندی UI
            .Select(e => new CdrRecord
            {
                SourceNumber = e.SourceNumber,
                TargetNumber = e.TargetNumber,
                DurationSeconds = e.Duration,
                OriginFileName = e.FileName,
                DateStr = e.DateStr,
                TimeStr = e.TimeStr
            })
            .ToListAsync();
    }

    // DTO داخلی
    private class NodeStatDto
    {
        public int Calls { get; set; }
        public double Dur { get; set; }
        public Dictionary<string, int> FileCounts { get; set; } = new Dictionary<string, int>();
        public HashSet<string> UniqueNeighbors { get; set; } = new HashSet<string>();
    }

    // DTO برای انتقال داده‌های گزارش
    public class FileReportData
    {
        public string FileName { get; set; }
        public string MainNodeId { get; set; }
        public List<CallStatDto> TopByCalls { get; set; }
        public List<CallStatDto> TopByDuration { get; set; }
    }

    public class CallStatDto
    {
        public string Number { get; set; }
        public int CallCount { get; set; }
        public double TotalDuration { get; set; }
    }

    // متد گزارش جامع (به روز شده با لاجیک جدید نود اصلی)
    public async Task<List<FileReportData>> GetComprehensiveReportDataAsync()
    {
        var result = new List<FileReportData>();
        var mainNodesMap = await GetMainNodesPerFileAsync();

        foreach (var kvp in mainNodesMap)
        {
            string file = kvp.Key;
            string mainNode = kvp.Value;

            var interactions = await _context.CdrRecords
                .AsNoTracking()
                .Where(x => x.FileName == file && (x.SourceNumber == mainNode || x.TargetNumber == mainNode))
                .ToListAsync();

            var stats = interactions
                .GroupBy(x => x.SourceNumber == mainNode ? x.TargetNumber : x.SourceNumber)
                .Select(g => new CallStatDto
                {
                    Number = g.Key,
                    CallCount = g.Count(),
                    TotalDuration = g.Sum(x => x.Duration)
                })
                .ToList();

            result.Add(new FileReportData
            {
                FileName = file,
                MainNodeId = mainNode,
                TopByCalls = stats.OrderByDescending(x => x.CallCount).Take(10).ToList(),
                TopByDuration = stats.OrderByDescending(x => x.TotalDuration).Take(10).ToList()
            });
        }
        return result;
    }

    //Common Targets
    // --- متد اصلاح شده و هوشمند برای پیدا کردن مشترکات ---
    public async Task<(List<GraphNode> nodes, List<GraphEdge> edges)> GetCommonNodesAndEdgesAsync(
        Dictionary<string, string> fileColors)
    {
        // 1. ابتدا نودهای اصلی (Main Nodes) هر فایل را دقیق شناسایی می‌کنیم
        var mainNodesMap = await GetMainNodesPerFileAsync();
        var mainNodeIds = mainNodesMap.Values.ToHashSet();

        if (!mainNodeIds.Any()) return (new List<GraphNode>(), new List<GraphEdge>());

        // 2. واکشی تمام تماس‌هایی که حداقل یک طرفش "نود اصلی" باشد
        var interactions = await _context.CdrRecords
            .AsNoTracking()
            .Where(x => mainNodeIds.Contains(x.SourceNumber) || mainNodeIds.Contains(x.TargetNumber))
            .Select(x => new { S = x.SourceNumber, T = x.TargetNumber, D = x.Duration, F = x.FileName })
            .ToListAsync();

        // 3. پیدا کردن "نودهای واسط" (Bridges)
        // نودی واسط است که با بیش از یک "نود اصلی" متفاوت در ارتباط باشد
        // یا خود نود اصلی باشد که با نود اصلی دیگری در تماس است

        // Map: ContactNumber -> Set of MainNodes it talked to
        var contactConnectivity = new Dictionary<string, HashSet<string>>();

        foreach (var record in interactions)
        {
            // تشخیص اینکه کدام طرف نود اصلی است (ممکن است هر دو باشند)
            string mainSide = null;
            string otherSide = null;

            if (mainNodeIds.Contains(record.S))
            {
                mainSide = record.S;
                otherSide = record.T;
            }
            else if (mainNodeIds.Contains(record.T))
            {
                mainSide = record.T;
                otherSide = record.S;
            }

            if (mainSide != null && !string.IsNullOrEmpty(otherSide))
            {
                if (!contactConnectivity.ContainsKey(otherSide))
                    contactConnectivity[otherSide] = new HashSet<string>();

                contactConnectivity[otherSide].Add(mainSide);
            }

            // حالت خاص: تماس مستقیم بین دو نود اصلی
            if (mainNodeIds.Contains(record.S) && mainNodeIds.Contains(record.T))
            {
                if (!contactConnectivity.ContainsKey(record.S)) contactConnectivity[record.S] = new HashSet<string>();
                contactConnectivity[record.S].Add(record.T);

                if (!contactConnectivity.ContainsKey(record.T)) contactConnectivity[record.T] = new HashSet<string>();
                contactConnectivity[record.T].Add(record.S);
            }
        }

        // فیلتر کردن: فقط نودهایی را نگه دار که به حداقل 2 نود اصلی وصل هستند
        // یا خودشان نود اصلی هستند
        var validNodeIds = new HashSet<string>();

        // الف) نودهای اصلی همیشه باشند
        foreach (var mn in mainNodeIds) validNodeIds.Add(mn);

        // ب) نودهای مشترک (که با بیش از 1 نود اصلی حرف زده‌اند)
        foreach (var kvp in contactConnectivity)
        {
            if (kvp.Value.Count >= 2)
            {
                validNodeIds.Add(kvp.Key);
            }
        }

        // 4. ساخت گراف نهایی فقط با نودهای معتبر
        var nodeStats = new Dictionary<string, (int Calls, double Dur)>();
        var edgeDict = new Dictionary<string, GraphEdge>();

        foreach (var r in interactions)
        {
            // فقط تماس‌هایی که دو طرفش جزو نودهای معتبر هستند
            if (validNodeIds.Contains(r.S) && validNodeIds.Contains(r.T))
            {
                // ساخت آمار نود
                if (!nodeStats.ContainsKey(r.S)) nodeStats[r.S] = (0, 0);
                nodeStats[r.S] = (nodeStats[r.S].Calls + 1, nodeStats[r.S].Dur + r.D);

                if (!nodeStats.ContainsKey(r.T)) nodeStats[r.T] = (0, 0);
                nodeStats[r.T] = (nodeStats[r.T].Calls + 1, nodeStats[r.T].Dur + r.D);

                // ساخت یال
                var id1 = string.Compare(r.S, r.T) < 0 ? r.S : r.T;
                var id2 = string.Compare(r.S, r.T) < 0 ? r.T : r.S;
                var key = $"{id1}_{id2}";

                if (!edgeDict.ContainsKey(key)) edgeDict[key] = new GraphEdge(id1, id2);
                edgeDict[key].AddInteraction(r.D);
            }
        }

        // تبدیل به آبجکت‌های گراف
        var nodes = nodeStats.Select(kvp =>
        {
            var n = new GraphNode(kvp.Key);
            n.AddMetrics(kvp.Value.Calls, kvp.Value.Dur);

            // رنگ‌بندی
            if (mainNodeIds.Contains(kvp.Key))
            {
                // پیدا کردن فایل مربوط به این نود اصلی برای رنگ‌دهی
                var ownerFile = mainNodesMap.FirstOrDefault(x => x.Value == kvp.Key).Key;
                if (ownerFile != null && fileColors.ContainsKey(ownerFile))
                    n.Color = fileColors[ownerFile];

                // نودهای اصلی کمی بزرگتر دیده شوند
                n.Weight += 0.2;
            }
            else
            {
                // نودهای مشترک (Bridge) بنفش شوند
                n.Color = "#8A2BE2";
            }

            return n;
        }).ToList();

        var edges = edgeDict.Values.ToList();
        if (edges.Any())
        {
            var maxCalls = edges.Max(e => e.CallCount);
            foreach (var e in edges)
            {
                e.CalculatedWeight = (double)e.CallCount / maxCalls;
                e.Thickness = (float)(1 + e.CalculatedWeight * 7);
            }
        }

        // نرمال‌سازی وزن نودها برای نمایش
        if (nodes.Any())
        {
            var maxCalls = nodes.Max(n => n.TotalCalls);
            foreach (var n in nodes) n.Weight = (double)n.TotalCalls / maxCalls;
        }

        return (nodes, edges);
    }

    // متد دقیق تشخیص نود اصلی (مجموع ورودی و خروجی)
    public async Task<Dictionary<string, string>> GetMainNodesPerFileAsync()
    {
        var result = new Dictionary<string, string>();
        var fileNames = await _context.CdrRecords.Select(x => x.FileName).Distinct().ToListAsync();

        foreach (var file in fileNames)
        {
            // خواندن تمام شماره‌های درگیر در فایل
            var records = await _context.CdrRecords
                .AsNoTracking()
                .Where(x => x.FileName == file)
                .Select(x => new { S = x.SourceNumber, T = x.TargetNumber })
                .ToListAsync();

            if (!records.Any()) continue;

            // شمارش فرکانس هر شماره (هم در S و هم در T)
            var frequency = new Dictionary<string, int>();
            foreach (var r in records)
            {
                if (!string.IsNullOrEmpty(r.S)) { if (!frequency.ContainsKey(r.S)) frequency[r.S] = 0; frequency[r.S]++; }
                if (!string.IsNullOrEmpty(r.T)) { if (!frequency.ContainsKey(r.T)) frequency[r.T] = 0; frequency[r.T]++; }
            }

            // انتخاب پرتکرارترین شماره به عنوان صاحب فایل
            var mainNode = frequency.OrderByDescending(x => x.Value).FirstOrDefault().Key;
            if (!string.IsNullOrEmpty(mainNode)) result[file] = mainNode;
        }
        return result;
    }
}