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
        // 1. دریافت آمار کلی تماس‌ها (مثل قبل)
        var rawData = await _context.CdrRecords
            .AsNoTracking()
            .Select(x => new { S = x.SourceNumber, T = x.TargetNumber, D = x.Duration, F = x.FileName })
            .ToListAsync();

        var nodeStats = new Dictionary<string, (int Calls, double Dur)>();
        // دیکشنری برای شمارش اینکه هر نود در کدام فایل چند بار دیده شده
        var nodeFileCounts = new Dictionary<string, Dictionary<string, int>>();

        void AddStat(string num, double dur, string fileName)
        {
            if (string.IsNullOrEmpty(num)) return;

            // آمار کلی
            if (!nodeStats.ContainsKey(num)) nodeStats[num] = (0, 0);
            var current = nodeStats[num];
            nodeStats[num] = (current.Calls + 1, current.Dur + dur);

            // آمار فایل (برای تعیین رنگ)
            if (!nodeFileCounts.ContainsKey(num)) nodeFileCounts[num] = new Dictionary<string, int>();
            if (!nodeFileCounts[num].ContainsKey(fileName)) nodeFileCounts[num][fileName] = 0;
            nodeFileCounts[num][fileName]++;
        }

        foreach (var item in rawData)
        {
            AddStat(item.S, item.D, item.F);
            AddStat(item.T, item.D, item.F);
        }

        var nodes = nodeStats.Select(kvp =>
        {
            var n = new GraphNode(kvp.Key);
            n.AddMetrics(kvp.Value.Calls, kvp.Value.Dur);

            // تعیین رنگ: پیدا کردن فایلی که این شماره بیشترین تکرار را در آن داشته
            if (nodeFileCounts.ContainsKey(kvp.Key))
            {
                var topFile = nodeFileCounts[kvp.Key]
                    .OrderByDescending(x => x.Value)
                    .FirstOrDefault().Key;

                if (topFile != null && fileColors.ContainsKey(topFile))
                {
                    n.Color = fileColors[topFile];
                }
            }

            return n;
        }).ToList();

        if (nodes.Any())
        {
            var maxCalls = nodes.Max(n => n.TotalCalls);
            foreach (var n in nodes) n.Weight = (double)n.TotalCalls / maxCalls;
        }

        return nodes;
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
}