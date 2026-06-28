using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using TemplateProcessor.Application.Abstractions;
using TemplateProcessor.Application.UseCases;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure.Analyzers;
using TemplateProcessor.Infrastructure.Renderers;
using TemplateProcessor.Infrastructure.Storage;
using TemplateProcessor.Domain.Exceptions;
using Xunit;

namespace TemplateProcessor.Tests.Integration
{
    public class WordIntegrationTests
    {
        private readonly string _templatesPath;
        private readonly ITemplateEngineModule _module;

        public WordIntegrationTests()
        {
            _templatesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "Templates");

            var storage = new LocalFileStorage(_templatesPath);
            var analyzer = new WordTemplateAnalyzer();
            var renderer = new WordRenderer();

            var getVariablesUseCase = new GetRequiredVariablesUseCase(storage, analyzer);
            var renderUseCase = new RenderDocumentUseCase(storage, analyzer, renderer);

            _module = new TemplateEngineModule(getVariablesUseCase, renderUseCase);
        }

        [Fact]
        public async Task RenderDocument_WithValidData_ShouldGenerateCorrectDocument()
        {
            var templatePath = Path.Combine(_templatesPath, "SampleTemplate.docx");
            var outputFormat = OutputFormat.Docx;

            var context = new TemplateContext
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

            var outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", "GeneratedDocument.docx");
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            using (var fileStream = File.Create(outputPath))
            {
                resultStream.CopyTo(fileStream);
            }

            resultStream.Position = 0;
            using (var document = WordprocessingDocument.Open(resultStream, false))
            {
                var body = document.MainDocumentPart?.Document.Body;
                Assert.NotNull(body);

                var fullText = string.Join(" ", body.Descendants<Text>().Select(t => t.Text));

                Assert.Contains("123-456", fullText);
                Assert.Contains("ООО Ромашка", fullText);
                Assert.Contains("01.01.2024", fullText);
                Assert.Contains("15000.50", fullText);

                Assert.Contains("Товар А", fullText);
                Assert.Contains("Товар Б", fullText);
                Assert.Contains("Товар В", fullText);

                Assert.DoesNotContain("{{#Items}}", fullText);
                Assert.DoesNotContain("{{/Items}}", fullText);
                Assert.DoesNotContain("{{Name}}", fullText);
                Assert.DoesNotContain("{{Quantity}}", fullText);
                Assert.DoesNotContain("{{Price}}", fullText);
                Assert.DoesNotContain("{{Total}}", fullText);
            }
        }

        [Fact]
        public async Task GetRequiredVariables_ShouldReturnCorrectVariables()
        {
            var templatePath = Path.Combine(_templatesPath, "SampleTemplate.docx");

            var variables = await _module.GetRequiredVariablesAsync(templatePath);

            Assert.NotNull(variables);
            Assert.Contains(variables, v => v.Name == "ContractNumber" && v.Type == VariableType.Scalar);
            Assert.Contains(variables, v => v.Name == "ClientName" && v.Type == VariableType.Scalar);
            Assert.Contains(variables, v => v.Name == "Date" && v.Type == VariableType.Scalar);
            Assert.Contains(variables, v => v.Name == "TotalSum" && v.Type == VariableType.Scalar);
            Assert.Contains(variables, v => v.Name == "Items" && v.Type == VariableType.Collection);

            Assert.DoesNotContain(variables, v => v.Name == "Name");
            Assert.DoesNotContain(variables, v => v.Name == "Quantity");
            Assert.DoesNotContain(variables, v => v.Name == "Price");
            Assert.DoesNotContain(variables, v => v.Name == "Total");
        }

        [Fact]
        public async Task RenderDocument_WhenDataMissing_ShouldThrowMissingDataException()
        {
            var templatePath = Path.Combine(_templatesPath, "SampleTemplate.docx");
            var outputFormat = OutputFormat.Docx;

            var context = new TemplateContext
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
