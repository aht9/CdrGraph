using CdrGraph.Core.Domain.Models;
using CdrGraph.Core.Interfaces;
using MiniExcelLibs;

namespace CdrGraph.Infrastructure.Services;
public class ExcelReaderService : IExcelReaderService
{
    public async Task<IEnumerable<string>> GetHeadersAsync(string filePath)
    {
        // خواندن فقط سطر اول برای گرفتن هدرها
        var columns = MiniExcel.GetColumns(filePath);
        return await Task.FromResult(columns);
    }

    public async Task<IEnumerable<CdrRecord>> ParseFileAsync(string filePath, ColumnMapping mapping)
    {
        // استفاده از QueryAsync برای خواندن خط به خط (Lazy Loading) برای مدیریت حافظه
        var rows = await MiniExcel.QueryAsync(filePath);
        var records = new List<CdrRecord>();

        foreach (var row in rows)
        {
            // MiniExcel سطرها را به صورت IDictionary<string, object> برمی‌گرداند
            var rowData = (IDictionary<string, object>)row;

            // استخراج داده‌ها بر اساس نام ستون‌هایی که کاربر مپ کرده است
            if (rowData.TryGetValue(mapping.SourceColumn, out var source) &&
                rowData.TryGetValue(mapping.TargetColumn, out var target))
            {
                var record = new CdrRecord
                {
                    SourceNumber = source?.ToString(),
                    TargetNumber = target?.ToString(),
                    CallType = "Unknown" 
                };

                // پارس کردن مدت زمان (ممکن است عدد یا رشته باشد)
                if (rowData.TryGetValue(mapping.DurationColumn, out var durationObj))
                {
                    if (double.TryParse(durationObj?.ToString(), out double duration))
                    {
                        record.DurationSeconds = duration;
                    }
                }

                records.Add(record);
            }
        }

        return records;
    }
}