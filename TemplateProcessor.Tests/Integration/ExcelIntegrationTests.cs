using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ClosedXML.Excel;
using TemplateProcessor.Application.Abstractions;
using TemplateProcessor.Application.UseCases;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure;
using TemplateProcessor.Infrastructure.Analyzers;
using TemplateProcessor.Infrastructure.Renderers;
using TemplateProcessor.Infrastructure.Storage;

namespace TemplateProcessor.Tests.Integration
{
    public class ExcelIntegrationTests
    {
        private readonly string _templatesPath;
        private readonly ITemplateEngineModule _module;

        public ExcelIntegrationTests()
        {
            _templatesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "Templates");

            var storage = new LocalFileStorage(_templatesPath);
            var formatResolver = new TemplateFormatResolver();
            var analyzerFactory = new TemplateAnalyzerFactory(
                new WordTemplateAnalyzer(),
                new ExcelTemplateAnalyzer(),
                new LatexTemplateAnalyzer());
            var renderingFactory = new RenderingEngineFactory(
                new WordRenderer(),
                new ExcelRenderer(),
                new LatexRenderer());

            var getVariablesUseCase = new GetRequiredVariablesUseCase(
                storage,
                formatResolver,
                analyzerFactory);
            var renderUseCase = new RenderDocumentUseCase(
                storage,
                formatResolver,
                analyzerFactory,
                renderingFactory);

            _module = new TemplateEngineModule(getVariablesUseCase, renderUseCase);
        }

        [Fact]
        public async Task RenderDocument_WithValidData_ShouldGenerateCorrectDocument()
        {
            var templatePath = Path.Combine(_templatesPath, "SampleTemplate.xlsx");
            var outputFormat = OutputFormat.Xlsx;

            var context = new TemplateContextDto
            {
                Scalars = new Dictionary<string, object>
                {
                    ["ContractNumber"] = "123-456",
                    ["ClientName"] = "ООО Ромашка",
                    ["Date"] = "01.01.2024",
                    ["TotalSum"] = 15000.50
                },
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>
                {
                    ["Items"] = new List<Dictionary<string, object>>
                {
                    new() { ["Name"] = "Товар А", ["Quantity"] = 2, ["Price"] = 1000.00, ["Total"] = 2000.00 },
                    new() { ["Name"] = "Товар Б", ["Quantity"] = 1, ["Price"] = 5000.00, ["Total"] = 5000.00 },
                    new() { ["Name"] = "Товар В", ["Quantity"] = 4, ["Price"] = 2000.00, ["Total"] = 8000.00 }
                }
                }
            };

            var resultStream = await _module.RenderDocumentAsync(templatePath, outputFormat, context);
            resultStream.Position = 0;

            Assert.NotNull(resultStream);
            Assert.True(resultStream.Length > 0, "Generated document is empty");

            using var workbook = new XLWorkbook(resultStream);
            var worksheet = workbook.Worksheet(1);

            //Собираем весь текст с листа
            var allText = string.Join(" ", worksheet.RowsUsed().SelectMany(r => r.Cells().Select(c => c.GetString())));

            //Проверяем скаляры
            Assert.Contains("123-456", allText);
            Assert.Contains("ООО Ромашка", allText);
            Assert.Contains("01.01.2024", allText);
            Assert.Contains("15000.50", allText);

            //Проверяем элементы коллекции
            Assert.Contains("Товар А", allText);
            Assert.Contains("Товар Б", allText);
            Assert.Contains("Товар В", allText);

            //Проверяем, что маркеры удалены
            Assert.DoesNotContain("{{#Items}}", allText);
            Assert.DoesNotContain("{{/Items}}", allText);
            Assert.DoesNotContain("{{Name}}", allText);
            Assert.DoesNotContain("{{Quantity}}", allText);
            Assert.DoesNotContain("{{Price}}", allText);
            Assert.DoesNotContain("{{Total}}", allText);
        }

        [Fact]
        public async Task GetRequiredVariables_ShouldReturnCorrectVariables()
        {
            var templatePath = Path.Combine(_templatesPath, "SampleTemplate.xlsx");

            var variables = await _module.GetRequiredVariablesAsync(templatePath);

            Assert.NotNull(variables);
            Assert.Contains(variables, v => v.Name == "ContractNumber" && v.Type == VariableType.Scalar);
            Assert.Contains(variables, v => v.Name == "ClientName" && v.Type == VariableType.Scalar);
            Assert.Contains(variables, v => v.Name == "Date" && v.Type == VariableType.Scalar);
            Assert.Contains(variables, v => v.Name == "TotalSum" && v.Type == VariableType.Scalar);
            Assert.Contains(variables, v => v.Name == "Items" && v.Type == VariableType.Collection);

            //Внутренние переменные коллекции не должны возвращаться
            Assert.DoesNotContain(variables, v => v.Name == "Name");
            Assert.DoesNotContain(variables, v => v.Name == "Quantity");
            Assert.DoesNotContain(variables, v => v.Name == "Price");
            Assert.DoesNotContain(variables, v => v.Name == "Total");
        }

        [Fact]
        public async Task RenderDocument_WhenDataMissing_ShouldThrowMissingDataException()
        {
            var templatePath = Path.Combine(_templatesPath, "SampleTemplate.xlsx");
            var outputFormat = OutputFormat.Xlsx;

            var context = new TemplateContextDto
            {
                Scalars = new Dictionary<string, object>
                {
                    ["ContractNumber"] = "123-456"
                },
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>
                {
                    ["Items"] = new List<Dictionary<string, object>>()
                }
            };

            await Assert.ThrowsAsync<MissingDataException>(
                () => _module.RenderDocumentAsync(templatePath, outputFormat, context));
        }
    }
}
