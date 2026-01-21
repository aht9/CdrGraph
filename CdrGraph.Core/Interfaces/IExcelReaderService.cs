using CdrGraph.Core.Domain.Models;

namespace CdrGraph.Core.Interfaces;

public interface IExcelReaderService
{
    /// <summary>
    /// Reads headers from an Excel file to allow column mapping.
    /// </summary>
    Task<IEnumerable<string>> GetHeadersAsync(string filePath);

    /// <summary>
    /// Parses the Excel file based on the provided column mapping.
    /// </summary>
    Task<IEnumerable<CdrRecord>> ParseFileAsync(string filePath, ColumnMapping mapping);
}

public class ColumnMapping
{
    public string SourceColumn { get; set; }
    public string TargetColumn { get; set; }
    public string DurationColumn { get; set; }
    // Optional: Date, Type, etc.
}