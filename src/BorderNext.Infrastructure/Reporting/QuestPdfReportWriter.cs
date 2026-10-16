using BorderNext.Application.Abstractions;
using BorderNext.Application.Reporting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BorderNext.Infrastructure.Reporting;

/// <summary>
/// Édition PDF des documents du poste (QuestPDF) : en-tête (organisation, titre,
/// critères), tableaux, synthèse et pied de page paginé. La licence QuestPDF est
/// déclarée « Community » — à revoir par l'intégrateur selon l'usage réel.
/// </summary>
public class QuestPdfReportWriter : IReportWriter
{
    private static readonly object LicenseGate = new();
    private static bool _licenseDeclared;

    public QuestPdfReportWriter()
    {
        lock (LicenseGate)
        {
            if (!_licenseDeclared)
            {
                QuestPDF.Settings.License = LicenseType.Community;
                _licenseDeclared = true;
            }
        }
    }

    public byte[] RenderPdf(LocalizedReport report)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(report.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(28);
                // Police embarquée de QuestPDF (Lato) : aucun prérequis de police installée
                // sur le poste, l'édition est identique d'un environnement à l'autre.
                page.DefaultTextStyle(style => style.FontSize(9));

                page.Header().Element(header => ComposeHeader(header, report));
                page.Content().Element(content => ComposeContent(content, report));
                page.Footer().Element(footer => ComposeFooter(footer, report));
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, LocalizedReport report)
    {
        container.Column(column =>
        {
            column.Spacing(3);

            if (!string.IsNullOrWhiteSpace(report.Organisation))
            {
                column.Item().Text(report.Organisation)
                    .FontSize(12).SemiBold().FontColor(Colors.Blue.Darken3);
            }

            column.Item().Text(report.Title).FontSize(15).Bold();
            if (!string.IsNullOrWhiteSpace(report.Subtitle))
                column.Item().Text(report.Subtitle!).FontSize(11).FontColor(Colors.Grey.Darken2);

            if (report.FilterLines.Count > 0)
            {
                column.Item().PaddingTop(3).Text(string.Join("  ·  ", report.FilterLines))
                    .FontSize(8).FontColor(Colors.Grey.Darken2);
            }

            column.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void ComposeContent(IContainer container, LocalizedReport report)
    {
        container.PaddingVertical(8).Column(column =>
        {
            column.Spacing(10);

            foreach (var table in report.Tables)
            {
                column.Item().Column(section =>
                {
                    section.Spacing(3);

                    if (!string.IsNullOrWhiteSpace(table.Title))
                        section.Item().Text(table.Title!).FontSize(10).SemiBold().FontColor(Colors.Blue.Darken2);

                    section.Item().Table(grid =>
                    {
                        grid.ColumnsDefinition(columns =>
                        {
                            foreach (var column2 in table.Columns)
                                columns.RelativeColumn(column2.Numeric ? 1 : 2);
                        });

                        // En-têtes.
                        foreach (var column2 in table.Columns)
                        {
                            grid.Cell().Background(Colors.Blue.Lighten4).Padding(3)
                                .Text(column2.Header).SemiBold().FontSize(8);
                        }

                        // Lignes.
                        var index = 0;
                        foreach (var row in table.Rows)
                        {
                            var background = index++ % 2 == 1 ? Colors.Grey.Lighten5 : Colors.White;
                            for (var i = 0; i < table.Columns.Count; i++)
                            {
                                var value = i < row.Length ? row[i] : null;
                                var cell = grid.Cell().Background(background).Padding(3);
                                var text = cell.Text(value ?? "—").FontSize(8);
                                if (table.Columns[i].Numeric) text.AlignRight();
                            }
                        }

                        // Totaux.
                        if (table.Totals is { } totals)
                        {
                            for (var i = 0; i < table.Columns.Count; i++)
                            {
                                var value = i < totals.Length ? totals[i] : null;
                                var cell = grid.Cell().Background(Colors.Grey.Lighten3).Padding(3);
                                var text = cell.Text(value ?? "—").FontSize(8).SemiBold();
                                if (table.Columns[i].Numeric) text.AlignRight();
                            }
                        }
                    });
                });
            }

            if (report.SummaryLines.Count > 0)
            {
                column.Item().PaddingTop(4).Column(summary =>
                {
                    summary.Spacing(2);
                    summary.Item().Text("Synthèse").FontSize(10).SemiBold().FontColor(Colors.Blue.Darken2);
                    foreach (var line in report.SummaryLines)
                        summary.Item().Text("• " + line).FontSize(9);
                });
            }
        });
    }

    private static void ComposeFooter(IContainer container, LocalizedReport report)
    {
        container.Column(column =>
        {
            column.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken2));
                    text.Span(report.GeneratedLine);
                    if (!string.IsNullOrWhiteSpace(report.FooterText))
                        text.Span(" — " + report.FooterText);
                });

                row.ConstantItem(120).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken2));
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        });
    }
}
