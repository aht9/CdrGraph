using CdrGraph.Core.Domain.Models;
using CdrGraph.Core.Interfaces;
using MiniExcelLibs;

namespace CdrGraph.Infrastructure.Services;

public class ExcelReaderService : IExcelReaderService
{
    public async Task<IEnumerable<string>> GetHeadersAsync(string filePath)
    {
        return MiniExcel.GetColumns(filePath);
    }

    public async Task<IEnumerable<CdrRecord>> ParseFileAsync(string filePath, ColumnMapping mapping)
    {
        var rows = await MiniExcel.QueryAsync(filePath);
        var records = new List<CdrRecord>();
        string fileName = System.IO.Path.GetFileName(filePath);

        foreach (var row in rows)
        {
            var rowData = (IDictionary<string, object>)row;

            if (rowData.TryGetValue(mapping.SourceColumn, out var source) &&
                rowData.TryGetValue(mapping.TargetColumn, out var target))
            {
                var record = new CdrRecord
                {
                    SourceNumber = source?.ToString(),
                    TargetNumber = target?.ToString(),
                    OriginFileName = fileName,
                    RawMetadata = new Dictionary<string, object>(rowData)
                };

                // پارس کردن مدت زمان
                if (!string.IsNullOrEmpty(mapping.DurationColumn) &&
                    rowData.TryGetValue(mapping.DurationColumn, out var durObj))
                {
                    if (double.TryParse(durObj?.ToString(), out double d)) record.DurationSeconds = d;
                }

                // پارس کردن تاریخ و ساعت
                string datePart = "";
                string timePart = "";

                if (!string.IsNullOrEmpty(mapping.DateColumn) && rowData.TryGetValue(mapping.DateColumn, out var dObj))
                    datePart = dObj?.ToString();

                if (!string.IsNullOrEmpty(mapping.TimeColumn) && rowData.TryGetValue(mapping.TimeColumn, out var tObj))
                    timePart = tObj?.ToString();

                record.DateStr = datePart;
                record.TimeStr = timePart;

                // تلاش برای ساخت DateTime واقعی (برای سورت و فیلتر)
                if (DateTime.TryParse($"{datePart} {timePart}", out DateTime dt))
                {
                    record.CallDateTime = dt;
                }

                records.Add(record);
            }
        }

        return records;
    }
}