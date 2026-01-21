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

    private static IContainer CellStyle(IContainer container)
    {
        return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
    }
}