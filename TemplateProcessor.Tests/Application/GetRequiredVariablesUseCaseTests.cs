using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using TemplateProcessor.Application.UseCases;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;
using Xunit;
using TemplateProcessor.Domain.Exceptions;

namespace TemplateProcessor.Tests.Application
{
    public class GetRequiredVariablesUseCaseTests
    {
        [Fact]
        public async Task ExecuteAsync_ShouldReturnVariablesFromAnalyzer()
        {
            var templatePath = "test.docx";
            var expectedVariables = new List<TemplateVariable>
        {
            new("ClientName", VariableType.Scalar),
            new("TotalSum", VariableType.Scalar)
        };

            var mockStorage = new Mock<ITemplateStorage>();
            mockStorage
                .Setup(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MemoryStream());

            var mockAnalyzer = new Mock<ITemplateAnalyzer>();
            mockAnalyzer
                .Setup(a => a.AnalyzeAsync(It.IsAny<Stream>(), TemplateFormat.Word, It.IsAny<CancellationToken>()))
                .ReturnsAsync(expectedVariables);

            var useCase = new GetRequiredVariablesUseCase(mockStorage.Object, mockAnalyzer.Object);

            var result = await useCase.ExecuteAsync(templatePath);

            Assert.Equal(expectedVariables, result);
            mockStorage.Verify(s => s.ReadAsync(templatePath, It.IsAny<CancellationToken>()), Times.Once);
            mockAnalyzer.Verify(a => a.AnalyzeAsync(It.IsAny<Stream>(), TemplateFormat.Word, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenUnsupportedExtension_ThrowsUnsupportedFormatException()
        {
            var templatePath = "test.unsupported";
            var mockStorage = new Mock<ITemplateStorage>();
            var mockAnalyzer = new Mock<ITemplateAnalyzer>();
            var useCase = new GetRequiredVariablesUseCase(mockStorage.Object, mockAnalyzer.Object);

            await Assert.ThrowsAsync<UnsupportedFormatException>(
               () => useCase.ExecuteAsync(templatePath));
        }
    }
}
