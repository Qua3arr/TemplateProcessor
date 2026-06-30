using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
            var inputFormat = TemplateFormat.Word;
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

            var mockFormatResolver = new Mock<ITemplateFormatResolver>();
            mockFormatResolver
                .Setup(r => r.GetTemplateFormat(templatePath))
                .Returns(inputFormat);

            var mockAnalyzer = new Mock<ITemplateAnalyzer>();
            mockAnalyzer
                .Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), inputFormat, It.IsAny<CancellationToken>()))
                .ReturnsAsync(variables);

            var mockAnalyzerFactory = new Mock<ITemplateAnalyzerFactory>();
            mockAnalyzerFactory
                .Setup(f => f.Create(inputFormat))
                .Returns(mockAnalyzer.Object);

            var mockRenderer = new Mock<IDocumentRenderer>();
            mockRenderer
                .Setup(r => r.RenderAsync(It.IsAny<Stream>(), context, inputFormat, outputFormat, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedStream);

            var mockRenderingFactory = new Mock<IRenderingEngineFactory>();
            mockRenderingFactory
                .Setup(f => f.Create(inputFormat, outputFormat))
                .Returns(mockRenderer.Object);

            var useCase = new RenderDocumentUseCase(
                mockStorage.Object,
                mockFormatResolver.Object,
                mockAnalyzerFactory.Object,
                mockRenderingFactory.Object);

            var result = await useCase.ExecuteAsync(templatePath, outputFormat, context);

            Assert.Equal(expectedStream, result);
            mockStorage.Verify(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()), Times.Once);
            mockAnalyzer.Verify(a => a.AnalyzeAsync(It.IsAny<Stream>(), inputFormat, It.IsAny<CancellationToken>()), Times.Once);
            mockRenderer.Verify(r => r.RenderAsync(It.IsAny<Stream>(), context, inputFormat, outputFormat, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenVariableMissing_ThrowsMissingDataException()
        {
            var templatePath = "test.docx";
            var inputFormat = TemplateFormat.Word;
            var outputFormat = OutputFormat.Pdf;
            var context = new TemplateContext
            {
                Scalars = new Dictionary<string, object>(),
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

            var mockFormatResolver = new Mock<ITemplateFormatResolver>();
            mockFormatResolver
                .Setup(r => r.GetTemplateFormat(templatePath))
                .Returns(inputFormat);

            var mockAnalyzer = new Mock<ITemplateAnalyzer>();
            mockAnalyzer
                .Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), inputFormat, It.IsAny<CancellationToken>()))
                .ReturnsAsync(variables);

            var mockAnalyzerFactory = new Mock<ITemplateAnalyzerFactory>();
            mockAnalyzerFactory
                .Setup(f => f.Create(inputFormat))
                .Returns(mockAnalyzer.Object);

            var mockRenderingFactory = new Mock<IRenderingEngineFactory>();

            var useCase = new RenderDocumentUseCase(
                mockStorage.Object,
                mockFormatResolver.Object,
                mockAnalyzerFactory.Object,
                mockRenderingFactory.Object);

            await Assert.ThrowsAsync<MissingDataException>(
                () => useCase.ExecuteAsync(templatePath, outputFormat, context));
        }

        [Fact]
        public async Task ExecuteAsync_WhenRendererFails_WrapsExceptionIntoTemplateParsingException()
        {
            var templatePath = "test.docx";
            var inputFormat = TemplateFormat.Word;
            var outputFormat = OutputFormat.Docx;
            var context = new TemplateContext();

            var mockStorage = new Mock<ITemplateStorage>();
            mockStorage
                .Setup(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream());

            var mockFormatResolver = new Mock<ITemplateFormatResolver>();
            mockFormatResolver
                .Setup(r => r.GetTemplateFormat(templatePath))
                .Returns(inputFormat);

            var mockAnalyzer = new Mock<ITemplateAnalyzer>();
            mockAnalyzer
                .Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), inputFormat, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<TemplateVariable>());

            var mockAnalyzerFactory = new Mock<ITemplateAnalyzerFactory>();
            mockAnalyzerFactory
                .Setup(f => f.Create(inputFormat))
                .Returns(mockAnalyzer.Object);

            var mockRenderer = new Mock<IDocumentRenderer>();
            mockRenderer
                .Setup(r => r.RenderAsync(It.IsAny<Stream>(), context, inputFormat, outputFormat, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidDataException("Broken document"));

            var mockRenderingFactory = new Mock<IRenderingEngineFactory>();
            mockRenderingFactory
                .Setup(f => f.Create(inputFormat, outputFormat))
                .Returns(mockRenderer.Object);

            var useCase = new RenderDocumentUseCase(
                mockStorage.Object,
                mockFormatResolver.Object,
                mockAnalyzerFactory.Object,
                mockRenderingFactory.Object);

            await Assert.ThrowsAsync<TemplateParsingException>(
                () => useCase.ExecuteAsync(templatePath, outputFormat, context));
        }
    }
}
