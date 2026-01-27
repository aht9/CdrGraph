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

    // متد جدید: استخراج داده‌های کامل برای گزارش نهایی
    public async Task<List<FileReportData>> GetComprehensiveReportDataAsync()
    {
        var result = new List<FileReportData>();

        // 1. پیدا کردن لیست فایل‌ها
        var fileNames = await _context.CdrRecords
            .Select(x => x.FileName)
            .Distinct()
            .ToListAsync();

        foreach (var file in fileNames)
        {
            if (string.IsNullOrEmpty(file)) continue;

            // 2. پیدا کردن "نود اصلی" واقعی (براساس مجموع تکرار در مبدا و مقصد)
            // از آنجا که GroupBy روی Union در EF Core برای SQLite محدودیت دارد،
            // لیست شماره‌های درگیر در این فایل را می‌گیریم و در حافظه محاسبه می‌کنیم.

            var numbersInFile = await _context.CdrRecords
                .Where(x => x.FileName == file)
                .Select(x => new { S = x.SourceNumber, T = x.TargetNumber })
                .ToListAsync();

            // تجمیع تمام شماره‌ها (چه مبدا چه مقصد)
            var allNumbers = numbersInFile.Select(x => x.S).Concat(numbersInFile.Select(x => x.T));

            // پیدا کردن پرتکرارترین شماره
            var mainNode = allNumbers
                .GroupBy(n => n)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            if (mainNode == null) continue;

            // 3. محاسبه آمار تماس‌ها برای نود اصلی پیدا شده
            var interactions = await _context.CdrRecords
                .Where(x => x.FileName == file && (x.SourceNumber == mainNode || x.TargetNumber == mainNode))
                .GroupBy(x => x.SourceNumber == mainNode ? x.TargetNumber : x.SourceNumber)
                .Select(g => new CallStatDto
                {
                    Number = g.Key,
                    CallCount = g.Count(),
                    TotalDuration = g.Sum(x => x.Duration)
                })
                .ToListAsync();

            var topCalls = interactions.OrderByDescending(x => x.CallCount).Take(10).ToList();
            var topDuration = interactions.OrderByDescending(x => x.TotalDuration).Take(10).ToList();

            result.Add(new FileReportData
            {
                FileName = file,
                MainNodeId = mainNode,
                TopByCalls = topCalls,
                TopByDuration = topDuration
            });
        }

        return result;
    }
}