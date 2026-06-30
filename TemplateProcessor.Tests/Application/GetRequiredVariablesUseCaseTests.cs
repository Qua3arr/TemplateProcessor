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
    public class GetRequiredVariablesUseCaseTests
    {
        [Fact]
        public async Task ExecuteAsync_ShouldReturnVariablesFromAnalyzer()
        {
            var templatePath = "test.docx";
            var inputFormat = TemplateFormat.Word;
            var expectedVariables = new List<TemplateVariable>
            {
                new("ClientName", VariableType.Scalar),
                new("TotalSum", VariableType.Scalar)
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
                .ReturnsAsync(expectedVariables);

            var mockAnalyzerFactory = new Mock<ITemplateAnalyzerFactory>();
            mockAnalyzerFactory
                .Setup(f => f.Create(inputFormat))
                .Returns(mockAnalyzer.Object);

            var useCase = new GetRequiredVariablesUseCase(
                mockStorage.Object,
                mockFormatResolver.Object,
                mockAnalyzerFactory.Object);

            var result = await useCase.ExecuteAsync(templatePath);

            Assert.Equal(expectedVariables, result);
            mockStorage.Verify(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()), Times.Once);
            mockAnalyzer.Verify(a => a.AnalyzeAsync(It.IsAny<Stream>(), inputFormat, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenUnsupportedExtension_ThrowsUnsupportedFormatException()
        {
            var templatePath = "test.unsupported";
            var mockStorage = new Mock<ITemplateStorage>();
            var mockFormatResolver = new Mock<ITemplateFormatResolver>();
            var mockAnalyzerFactory = new Mock<ITemplateAnalyzerFactory>();

            mockFormatResolver
                .Setup(r => r.GetTemplateFormat(templatePath))
                .Throws(new UnsupportedFormatException("Unsupported"));

            var useCase = new GetRequiredVariablesUseCase(
                mockStorage.Object,
                mockFormatResolver.Object,
                mockAnalyzerFactory.Object);

            await Assert.ThrowsAsync<UnsupportedFormatException>(
               () => useCase.ExecuteAsync(templatePath));
        }

        [Fact]
        public async Task ExecuteAsync_WhenStorageFails_WrapsExceptionIntoTemplateParsingException()
        {
            var templatePath = "test.docx";
            var inputFormat = TemplateFormat.Word;
            var mockStorage = new Mock<ITemplateStorage>();
            var mockFormatResolver = new Mock<ITemplateFormatResolver>();
            var mockAnalyzerFactory = new Mock<ITemplateAnalyzerFactory>();

            mockFormatResolver
                .Setup(r => r.GetTemplateFormat(templatePath))
                .Returns(inputFormat);

            mockStorage
                .Setup(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("Cannot read template"));

            var useCase = new GetRequiredVariablesUseCase(
                mockStorage.Object,
                mockFormatResolver.Object,
                mockAnalyzerFactory.Object);

            await Assert.ThrowsAsync<TemplateParsingException>(
                () => useCase.ExecuteAsync(templatePath));
        }
    }
}
