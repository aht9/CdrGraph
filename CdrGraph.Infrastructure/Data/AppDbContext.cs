using Microsoft.EntityFrameworkCore;

namespace CdrGraph.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<ProjectMetadata> Projects { get; set; }
    // جدول جدید برای رکوردهای خام
    public DbSet<CdrEntity> CdrRecords { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite("Data Source=cdr_analysis.db");
            
        // تنظیمات حیاتی برای سرعت بالا در SQLite
        optionsBuilder.EnableSensitiveDataLogging(false);
    }
}

public class ProjectMetadata
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string FilePath { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // تنظیمات جیسون شده برای مپینگ ستون‌ها
    public string ColumnMappingJson { get; set; }
}