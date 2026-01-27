using CdrGraph.Core.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CdrGraph.Infrastructure.Services;

public class PdfReportService
{
    public void GenerateReport(string filePath, List<GraphNode> nodes, List<GraphEdge> edges, byte[] graphImageBytes)
    {
        // تنظیم لایسنس کامیونیتی (رایگان)
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(2, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Header()
                        .Text("CDR Analysis Report")
                        .SemiBold().FontSize(24).FontColor(Colors.Blue.Medium);

                    page.Content()
                        .PaddingVertical(1, Unit.Centimetre)
                        .Column(x =>
                        {
                            // 1. نمایش تصویر گراف
                            x.Item().Text("1. Graph Visualization").Bold().FontSize(14);
                            if (graphImageBytes != null)
                            {
                                x.Item().Image(graphImageBytes).FitArea();
                            }

                            x.Item().PaddingTop(20).Text("2. Summary Statistics").Bold().FontSize(14);

                            // 2. جدول آمار
                            x.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(CellStyle).Text("Total Nodes");
                                    header.Cell().Element(CellStyle).Text("Total Calls");
                                    header.Cell().Element(CellStyle).Text("Total Duration (min)");
                                });

                                table.Cell().Element(CellStyle).Text(nodes.Count.ToString());
                                table.Cell().Element(CellStyle).Text(edges.Sum(e => e.CallCount).ToString());
                                table.Cell().Element(CellStyle)
                                    .Text(edges.Sum(e => e.TotalDurationMinutes).ToString("N2"));
                            });

                            x.Item().PaddingTop(20).Text("3. Top Active Numbers").Bold().FontSize(14);

                            // 3. جدول نودهای مهم
                            x.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(40);
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });

                                table.Header(header =>
                                {
                                    header.Cell().Element(CellStyle).Text("#");
                                    header.Cell().Element(CellStyle).Text("Number");
                                    header.Cell().Element(CellStyle).Text("Calls");
                                    header.Cell().Element(CellStyle).Text("Total Duration");
                                });

                                int rank = 1;
                                foreach (var node in nodes.OrderByDescending(n => n.TotalCalls).Take(10))
                                {
                                    table.Cell().Element(CellStyle).Text(rank++.ToString());
                                    table.Cell().Element(CellStyle).Text(node.Id);
                                    table.Cell().Element(CellStyle).Text(node.TotalCalls.ToString());
                                    table.Cell().Element(CellStyle).Text(node.TotalDurationMinutes.ToString("N1"));
                                }
                            });
                        });

                    page.Footer()
                        .AlignCenter()
                        .Text(x =>
                        {
                            x.Span("Page ");
                            x.CurrentPageNumber();
                        });
                });
            })
            .GeneratePdf(filePath);
    }

    // متد جدید برای گزارش مشترکات
    public void GenerateCommonConnectionReport(string filePath, List<GraphNode> allNodes, List<GraphEdge> edges, List<GraphNode> targetNodes, bool showDuration, byte[] graphImage)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var commonNodes = allNodes.Except(targetNodes).ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col => 
                {
                    col.Item().Text("Common Connections Analysis").SemiBold().FontSize(20).FontColor(Colors.Blue.Darken2);
                    col.Item().Text($"Target Nodes: {string.Join(", ", targetNodes.Select(n => n.Id))}").FontSize(12).FontColor(Colors.Grey.Darken2);
                });

                page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                {
                    // *** اضافه کردن تصویر گراف ***
                    if (graphImage != null)
                    {
                        col.Item().Text("Visual Graph (Sub-Graph)").Bold().FontSize(14);
                        // نمایش تصویر با ارتفاع محدود (مثلا 10 سانتیمتر)
                        col.Item().Height(10, Unit.Centimetre).Image(graphImage).FitArea();
                        col.Item().PaddingBottom(1, Unit.Centimetre);
                    }

                    col.Item().Text($"{commonNodes.Count} common entities found.").Bold().FontSize(14);
                    
                    // ... (جدول داده‌ها مثل قبل) ...
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        // (تعریف ستون‌ها و هدر جدول - کد قبلی را حفظ کنید)
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            foreach(var t in targetNodes) columns.RelativeColumn();
                        });
                        
                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("Common Number").Bold();
                            foreach (var t in targetNodes) header.Cell().Element(CellStyle).Text($"Link to {t.Id}").Bold();
                        });

                        foreach (var common in commonNodes)
                        {
                            table.Cell().Element(CellStyle).Text(common.Id);
                            foreach (var target in targetNodes)
                            {
                                var edge = edges.FirstOrDefault(e => 
                                    (e.SourceId == common.Id && e.TargetId == target.Id) || 
                                    (e.TargetId == common.Id && e.SourceId == target.Id));

                                string val = "-";
                                if (edge != null)
                                    val = showDuration ? $"{edge.TotalDurationMinutes:N1} min" : $"{edge.CallCount} calls";
                                
                                table.Cell().Element(CellStyle).Text(val);
                            }
                        }
                    });
                });

                page.Footer().AlignCenter().Text(x => { x.Span("Page "); x.CurrentPageNumber(); });
            });
        })
        .GeneratePdf(filePath);
    }

    private static IContainer CellStyle(IContainer container)
    {
        return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).PaddingHorizontal(2);
    }
}