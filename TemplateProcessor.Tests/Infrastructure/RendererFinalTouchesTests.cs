using System.Reflection;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure.Renderers;

namespace TemplateProcessor.Tests.Infrastructure
{
    public class RendererFinalTouchesTests
    {
        [Fact]
        public async Task ExcelRenderer_PreservesMergedRanges_WhenCollectionRowsAreCloned()
        {
            using var templateStream = CreateExcelTemplateWithMergedCells();
            var renderer = new ExcelRenderer();
            var context = CreateCollectionContext();

            using var resultStream = await renderer.RenderAsync(
                templateStream,
                context,
                TemplateFormat.Excel,
                OutputFormat.Xlsx);

            using var workbook = new XLWorkbook(resultStream);
            var worksheet = workbook.Worksheet(1);

            Assert.Equal("Items", worksheet.Cell(1, 1).GetString());
            Assert.Equal("Alpha", worksheet.Cell(2, 1).GetString());
            Assert.Equal("Beta", worksheet.Cell(3, 1).GetString());

            Assert.True(HasMergedRange(worksheet, 1, 1, 1, 4));
            Assert.True(HasMergedRange(worksheet, 2, 1, 2, 2));
            Assert.True(HasMergedRange(worksheet, 3, 1, 3, 2));
        }

        [Fact]
        public async Task WordRenderer_PreservesMergedCells_WhenCollectionRowsAreCloned()
        {
            using var templateStream = CreateWordTemplateWithMergedCells();
            var renderer = new WordRenderer();
            var context = CreateCollectionContext();

            using var resultStream = await renderer.RenderAsync(
                templateStream,
                context,
                TemplateFormat.Word,
                OutputFormat.Docx);

            using var document = WordprocessingDocument.Open(resultStream, false);
            var body = document.MainDocumentPart?.Document?.Body;

            Assert.NotNull(body);

            var table = body!.Descendants<Table>().Single();
            var rows = table.Elements<TableRow>().ToList();

            Assert.Equal(2, rows.Count);
            Assert.Contains("Alpha", GetElementText(rows[0]));
            Assert.Contains("Beta", GetElementText(rows[1]));

            foreach (var row in rows)
            {
                var firstCell = row.Elements<TableCell>().First();
                var gridSpan = firstCell.TableCellProperties!.GetFirstChild<GridSpan>();

                Assert.NotNull(gridSpan);
                Assert.Equal(2, gridSpan!.Val!.Value);
            }
        }

        [Fact]
        public void LatexRenderer_EscapesSpecialCharacters()
        {
            var method = typeof(LatexRenderer).GetMethod(
                "EscapeLaTeX",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            var result = (string)method!.Invoke(null, new object[] { "# $ % & _ { } \\ ~ ^" })!;

            Assert.Equal(
                "\\# \\$ \\% \\& \\_ \\{ \\} \\textbackslash{} \\textasciitilde{} \\textasciicircum{}",
                result);
        }

        private static TemplateContext CreateCollectionContext()
        {
            return new TemplateContext
            {
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>
                {
                    ["Items"] = new List<Dictionary<string, object>>
                    {
                        new() { ["Name"] = "Alpha", ["Quantity"] = 2, ["Price"] = 10.00 },
                        new() { ["Name"] = "Beta", ["Quantity"] = 3, ["Price"] = 20.00 }
                    }
                }
            };
        }

        private static MemoryStream CreateExcelTemplateWithMergedCells()
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.AddWorksheet("Template");

            worksheet.Cell("A1").Value = "{{#Items}}";
            worksheet.Range("A2:D2").Merge();
            worksheet.Cell("A2").Value = "Items";
            worksheet.Range("A3:B3").Merge();
            worksheet.Cell("A3").Value = "{{Name}}";
            worksheet.Cell("C3").Value = "{{Quantity}}";
            worksheet.Cell("D3").Value = "{{Price}}";
            worksheet.Cell("A4").Value = "{{/Items}}";

            var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        private static MemoryStream CreateWordTemplateWithMergedCells()
        {
            var stream = new MemoryStream();
            using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
            {
                var mainPart = document.AddMainDocumentPart();
                mainPart.Document = new Document(new Body());

                var table = new Table(
                    new TableGrid(
                        new GridColumn(),
                        new GridColumn(),
                        new GridColumn()));

                table.Append(CreateWordRow("{{#Items}}"));
                table.Append(CreateMergedWordRow("{{Name}}", "{{Quantity}}"));
                table.Append(CreateWordRow("{{/Items}}"));

                mainPart.Document.Body!.Append(table);
                mainPart.Document.Save();
            }

            stream.Position = 0;
            return stream;
        }

        private static TableRow CreateWordRow(string text)
        {
            return new TableRow(new TableCell(new Paragraph(new Run(new Text(text)))));
        }

        private static TableRow CreateMergedWordRow(string mergedCellText, string regularCellText)
        {
            return new TableRow(
                new TableCell(
                    new TableCellProperties(new GridSpan { Val = 2 }),
                    new Paragraph(new Run(new Text(mergedCellText)))),
                new TableCell(new Paragraph(new Run(new Text(regularCellText)))));
        }

        private static bool HasMergedRange(
            IXLWorksheet worksheet,
            int firstRow,
            int firstColumn,
            int lastRow,
            int lastColumn)
        {
            return worksheet.MergedRanges.Any(range =>
                range.FirstRow().RowNumber() == firstRow &&
                range.FirstColumn().ColumnNumber() == firstColumn &&
                range.LastRow().RowNumber() == lastRow &&
                range.LastColumn().ColumnNumber() == lastColumn);
        }

        private static string GetElementText(OpenXmlElement element)
        {
            return string.Join("", element.Descendants<Text>().Select(text => text.Text));
        }
    }
}
