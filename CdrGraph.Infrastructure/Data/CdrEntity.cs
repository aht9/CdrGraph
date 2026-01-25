using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace CdrGraph.Infrastructure.Data;

[Index(nameof(SourceNumber))]
[Index(nameof(TargetNumber))]
public class CdrEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(50)]
    public string SourceNumber { get; set; }

    [MaxLength(50)]
    public string TargetNumber { get; set; }

    public double Duration { get; set; }
    public DateTime CallDate { get; set; }
        
    [MaxLength(20)]
    public string DateStr { get; set; }
        
    [MaxLength(20)]
    public string TimeStr { get; set; }

    [MaxLength(100)]
    public string FileName { get; set; }
}