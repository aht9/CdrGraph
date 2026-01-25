using CdrGraph.Core.Domain.Models;
using CdrGraph.Core.Interfaces;
using MiniExcelLibs;

namespace CdrGraph.Infrastructure.Services;

public class ExcelReaderService : IExcelReaderService
{
    public async Task<IEnumerable<string>> GetHeadersAsync(string filePath)
    {
        // خواندن 2 ردیف اول برای تشخیص هدر
        var rows = MiniExcel.Query(filePath).Take(2).ToList();

        if (!rows.Any()) return new List<string>();

        // ردیف اول (داده‌های سلول‌ها)
        var firstRow = rows[0] as IDictionary<string, object>;

        // ردیف دوم (اگر وجود داشته باشد)
        var secondRow = rows.Count > 1 ? rows[1] as IDictionary<string, object> : null;

        // تشخیص اینکه آیا ردیف اول هدر واقعی است یا فقط حروف ستون (A, B, C)
        // منطق: اگر مقدار داخل سلول با کلید ستون یکی بود (مثلا ستون A مقدارش A بود)، یعنی هدر نیست
        bool isFirstRowJustLetters = true;
        foreach (var key in firstRow.Keys)
        {
            var value = firstRow[key]?.ToString();
            if (value != key) // اگر حداقل یک ستون نامش با حروفش فرق داشت (مثلا A != Date)
            {
                isFirstRowJustLetters = false;
                break;
            }
        }

        // انتخاب ردیفی که حاوی نام هدرهاست
        IDictionary<string, object> headerRow;
        if (isFirstRowJustLetters && secondRow != null)
        {
            headerRow = secondRow;
        }
        else
        {
            headerRow = firstRow;
        }

        var formattedHeaders = new List<string>();

        // ساخت لیست خروجی به فرمت: "A (Phone Number)"
        // ما همیشه از کلیدهای ردیف اول (A, B, C) استفاده می‌کنیم تا ترتیب درست باشد
        foreach (var key in firstRow.Keys)
        {
            string headerName = "";

            // تلاش برای گرفتن نام از ردیف انتخاب شده
            if (headerRow.ContainsKey(key) && headerRow[key] != null)
            {
                headerName = headerRow[key].ToString();
            }

            // اگر نام خالی بود، یا ردیف دوم وجود نداشت، همان کلید را نشان بده
            if (string.IsNullOrWhiteSpace(headerName))
            {
                formattedHeaders.Add(key);
            }
            else
            {
                formattedHeaders.Add($"{key} ({headerName})");
            }
        }

        return await Task.FromResult(formattedHeaders);
    }

    // متد کمکی اصلاح شده: استخراج کلید ستون (مثلاً "A") از رشته فرمت شده
    // ورودی: "A (Source Number)" -> خروجی: "A"
    private string ExtractColumnKey(string formattedName)
    {
        if (string.IsNullOrEmpty(formattedName)) return null;

        // جدا کردن بر اساس فاصله اول یا پرانتز
        // فرمت ما همیشه "Key (Name)" است.
        var parts = formattedName.Split(new[] { ' ', '(' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0)
        {
            return parts[0].Trim(); // برگرداندن "A"
        }

        return formattedName;
    }

    public async Task<IEnumerable<CdrRecord>> ParseFileAsync(string filePath, ColumnMapping mapping)
    {
        var rows = await MiniExcel.QueryAsync(filePath);
        var records = new List<CdrRecord>();
        string fileName = System.IO.Path.GetFileName(filePath);

        // استخراج کلیدهای واقعی ستون‌ها (A, B, C) برای MiniExcel
        string keySource = ExtractColumnKey(mapping.SourceColumn);
        string keyTarget = ExtractColumnKey(mapping.TargetColumn);
        string keyDuration = ExtractColumnKey(mapping.DurationColumn);
        string keyDate = ExtractColumnKey(mapping.DateColumn);
        string keyTime = ExtractColumnKey(mapping.TimeColumn);

        // پرش از روی ردیف هدر (اختیاری: اگر ردیف اول هدر باشد، ممکن است بخواهیم ردش کنیم تا در گراف نیاید)
        // اما چون CdrRecord فیلد عددی و متنی دارد، معمولا در TryParse رد می‌شوند.
        // با این حال، می‌توانیم یک شمارنده بگذاریم.
        bool firstRowSkipped = false;

        foreach (var row in rows)
        {
            var rowData = (IDictionary<string, object>)row;

            // اگر ردیف اول دقیقا همان نام هدرها بود، ردش کن (برای جلوگیری از ساخت نود با نام "Source Number")
            if (!firstRowSkipped)
            {
                // بررسی ساده: اگر مقدار ستون سورس دقیقا برابر با هدر انتخاب شده بود
                if (rowData.TryGetValue(keySource, out var sVal) &&
                    mapping.SourceColumn.Contains(sVal?.ToString() ?? "####"))
                {
                    firstRowSkipped = true;
                    continue;
                }

                firstRowSkipped = true;
            }

            if (rowData.TryGetValue(keySource, out var source) &&
                rowData.TryGetValue(keyTarget, out var target))
            {
                // اگر شماره‌ها خالی بودند رد کن
                if (source == null || target == null) continue;

                var record = new CdrRecord
                {
                    SourceNumber = source.ToString(),
                    TargetNumber = target.ToString(),
                    OriginFileName = fileName,
                    RawMetadata = new Dictionary<string, object>(rowData)
                };

                if (!string.IsNullOrEmpty(keyDuration) &&
                    rowData.TryGetValue(keyDuration, out var durObj))
                {
                    if (double.TryParse(durObj?.ToString(), out double d)) record.DurationSeconds = d;
                }

                string datePart = "";
                string timePart = "";

                if (!string.IsNullOrEmpty(keyDate) && rowData.TryGetValue(keyDate, out var dObj))
                    datePart = dObj?.ToString();

                if (!string.IsNullOrEmpty(keyTime) && rowData.TryGetValue(keyTime, out var tObj))
                    timePart = tObj?.ToString();

                record.DateStr = datePart;
                record.TimeStr = timePart;

                // ترکیب تاریخ و ساعت
                string dateTimeStr = $"{datePart} {timePart}".Trim();
                if (DateTime.TryParse(dateTimeStr, out DateTime dt))
                {
                    record.CallDateTime = dt;
                }

                records.Add(record);
            }
        }

        return records;
    }
}