namespace CdrGraph.Core.Domain.Models;

public class CdrRecord
{
    public string SourceNumber { get; set; }
    public string TargetNumber { get; set; }
    public double DurationSeconds { get; set; }
    
    // فیلدهای جدید برای تاریخ و ساعت
    public DateTime CallDateTime { get; set; } 
    public string DateStr { get; set; } // نگهداری مقدار خام تاریخ (اختیاری)
    public string TimeStr { get; set; } // نگهداری مقدار خام ساعت (اختیاری)

    public string OriginFileName { get; set; }
    public Dictionary<string, object> RawMetadata { get; set; } = new Dictionary<string, object>();
}