using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Moq;
using TemplateProcessor.Application.UseCases;
using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Tests.Application
{
    public class RenderDocumentUseCaseTests
    {
        [Fact]
        public async Task ExecuteAsync_WhenAllValid_ShouldRenderDocument()
        {
            var templatePath = "test.docx";
            var outputFormat = OutputFormat.Pdf;
            var context = new TemplateContext
            {
                Scalars = new Dictionary<string, object> { ["ClientName"] = "Test" },
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>()
            };
            var variables = new List<TemplateVariable>
        {
            new("ClientName", VariableType.Scalar)
        };
            var expectedStream = new MemoryStream();

            var mockStorage = new Mock<ITemplateStorage>();
            mockStorage
                .Setup(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream());

            var mockAnalyzer = new Mock<ITemplateAnalyzer>();
            mockAnalyzer
                .Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), TemplateFormat.Word, It.IsAny<CancellationToken>()))
                .ReturnsAsync(variables);

            var mockRenderer = new Mock<IDocumentRenderer>();
            mockRenderer
                .Setup(r => r.RenderAsync(It.IsAny<Stream>(), context, TemplateFormat.Word, outputFormat, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedStream);

            var useCase = new RenderDocumentUseCase(mockStorage.Object, mockAnalyzer.Object, mockRenderer.Object);

            var result = await useCase.ExecuteAsync(templatePath, outputFormat, context);

            Assert.Equal(expectedStream, result);
            mockStorage.Verify(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()), Times.Once);
            mockAnalyzer.Verify(a => a.AnalyzeAsync(It.IsAny<Stream>(), TemplateFormat.Word, It.IsAny<CancellationToken>()), Times.Once);
            mockRenderer.Verify(r => r.RenderAsync(It.IsAny<Stream>(), context, TemplateFormat.Word, outputFormat, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenVariableMissing_ThrowsMissingDataException()
        {
            var templatePath = "test.docx";
            var outputFormat = OutputFormat.Pdf;
            var context = new TemplateContext
            {
                Scalars = new Dictionary<string, object>(), //missing "ClientName"
                Collections = new Dictionary<string, IEnumerable<Dictionary<string, object>>>()
            };
            var variables = new List<TemplateVariable>
        {
            new("ClientName", VariableType.Scalar)
        };

            var mockStorage = new Mock<ITemplateStorage>();
            mockStorage
                .Setup(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream());

            var mockAnalyzer = new Mock<ITemplateAnalyzer>();
            mockAnalyzer
                .Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), TemplateFormat.Word, It.IsAny<CancellationToken>()))
                .ReturnsAsync(variables);

            var mockRenderer = new Mock<IDocumentRenderer>();

            var useCase = new RenderDocumentUseCase(mockStorage.Object, mockAnalyzer.Object, mockRenderer.Object);

            await Assert.ThrowsAsync<MissingDataException>(
                () => useCase.ExecuteAsync(templatePath, outputFormat, context));
        }
    }
}
