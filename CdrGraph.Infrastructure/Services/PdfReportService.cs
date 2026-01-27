using CdrGraph.Core.Domain.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CdrGraph.Infrastructure.Services;

public class PdfReportService
{
    public void GenerateComprehensiveReport(string filePath, byte[] graphImage,
        List<CdrDataService.FileReportData> reportData)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        Document.Create(container =>
            {
                // --- صفحه ۱: تصویر گراف (افقی) ---
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(1, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Content().Column(col =>
                    {
                        col.Item().Text("Global Connection Graph").FontSize(24).Bold().FontColor(Colors.Blue.Darken2)
                            .AlignCenter();

                        if (graphImage != null)
                        {
                            col.Item().Padding(10)
                                .Height(15, Unit.Centimetre)
                                .AlignMiddle()
                                .AlignCenter()
                                .Image(graphImage).FitArea();
                        }
                    });

                    page.Footer().AlignCenter().Text("Page 1 - Overview");
                });

                // --- صفحات بعدی: گزارش هر فایل (عمودی) ---
                foreach (var fileData in reportData)
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Portrait());
                        page.Margin(2, Unit.Centimetre);
                        page.PageColor(Colors.White);
                        page.DefaultTextStyle(x => x.FontSize(11));

                        page.Header().Column(col =>
                        {
                            col.Item().Text($"Analysis Report: {fileData.FileName}").FontSize(18).SemiBold()
                                .FontColor(Colors.Blue.Medium);
                            col.Item().Text($"Main Node Detected: {fileData.MainNodeId}").FontSize(14)
                                .FontColor(Colors.Grey.Darken2);
                            col.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                        });

                        page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                        {
                            col.Item().Row(row =>
                            {
                                // جدول سمت چپ
                                row.RelativeItem().PaddingRight(10).Column(c =>
                                {
                                    c.Item().Text("Top 10 by Call Count").Bold().FontSize(12)
                                        .FontColor(Colors.Orange.Darken2).AlignCenter();
                                    c.Item().PaddingTop(5).Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.ConstantColumn(30);
                                            columns.RelativeColumn();
                                            columns.ConstantColumn(60);
                                        });

                                        table.Header(header =>
                                        {
                                            // اصلاح شده: Bold را مستقیماً روی Text اعمال می‌کنیم نه روی کانتینر
                                            header.Cell().Element(HeaderStyle).Text("#").Bold();
                                            header.Cell().Element(HeaderStyle).Text("Number").Bold();
                                            header.Cell().Element(HeaderStyle).Text("Calls").Bold();
                                        });

                                        int rank = 1;
                                        foreach (var item in fileData.TopByCalls)
                                        {
                                            table.Cell().Element(CellStyle).Text(rank++.ToString());
                                            table.Cell().Element(CellStyle).Text(item.Number);
                                            table.Cell().Element(CellStyle).Text(item.CallCount.ToString());
                                        }
                                    });
                                });

                                // جدول سمت راست
                                row.RelativeItem().PaddingLeft(10).Column(c =>
                                {
                                    c.Item().Text("Top 10 by Duration").Bold().FontSize(12)
                                        .FontColor(Colors.Green.Darken2).AlignCenter();
                                    c.Item().PaddingTop(5).Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.ConstantColumn(30);
                                            columns.RelativeColumn();
                                            columns.ConstantColumn(80);
                                        });

                                        table.Header(header =>
                                        {
                                            header.Cell().Element(HeaderStyle).Text("#").Bold();
                                            header.Cell().Element(HeaderStyle).Text("Number").Bold();
                                            header.Cell().Element(HeaderStyle).Text("Dur (min)").Bold();
                                        });

                                        int rank = 1;
                                        foreach (var item in fileData.TopByDuration)
                                        {
                                            table.Cell().Element(CellStyle).Text(rank++.ToString());
                                            table.Cell().Element(CellStyle).Text(item.Number);
                                            table.Cell().Element(CellStyle)
                                                .Text((item.TotalDuration / 60.0).ToString("N1"));
                                        }
                                    });
                                });
                            });
                        });

                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("Generated by CDR Graph Analyzer - ");
                            x.CurrentPageNumber();
                        });
                    });
                }
            })
            .GeneratePdf(filePath);
    }

    // متد گزارش مشترکات (از قبل وجود داشت، اینجا تکرار می‌کنیم تا فایل کامل باشد)
    public void GenerateCommonConnectionReport(string filePath, List<GraphNode> allNodes, List<GraphEdge> edges,
        List<GraphNode> targetNodes, bool showDuration, byte[] graphImage)
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
                        col.Item().Text("Common Connections Analysis").SemiBold().FontSize(20)
                            .FontColor(Colors.Blue.Darken2);
                        col.Item().Text($"Target Nodes: {string.Join(", ", targetNodes.Select(n => n.Id))}")
                            .FontSize(12).FontColor(Colors.Grey.Darken2);
                    });

                    page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                    {
                        if (graphImage != null)
                        {
                            col.Item().Text("Visual Graph (Sub-Graph)").Bold().FontSize(14);
                            col.Item().Height(10, Unit.Centimetre).Image(graphImage).FitArea();
                            col.Item().PaddingBottom(1, Unit.Centimetre);
                        }

                        col.Item().Text($"{commonNodes.Count} common entities found.").Bold().FontSize(14);

                        col.Item().PaddingTop(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(2);
                                foreach (var t in targetNodes) columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(HeaderStyle).Text("Common Number").Bold();
                                foreach (var t in targetNodes)
                                    header.Cell().Element(HeaderStyle).Text($"Link to {t.Id}").Bold();
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
                                        val = showDuration
                                            ? $"{edge.TotalDurationMinutes:N1} min"
                                            : $"{edge.CallCount} calls";

                                    table.Cell().Element(CellStyle).Text(val);
                                }
                            }
                        });
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                    });
                });
            })
            .GeneratePdf(filePath);
    }

    // استایل‌ها (اصلاح شده: حذف متدهایی مثل Bold که روی کانتینر ممکن است خطا دهند)
    private static IContainer CellStyle(IContainer container)
    {
        return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(5).AlignCenter();
    }

    private static IContainer HeaderStyle(IContainer container)
    {
        // حذف .Bold() از اینجا و انتقال به .Text().Bold()
        return container.BorderBottom(2).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).AlignCenter();
    }
}