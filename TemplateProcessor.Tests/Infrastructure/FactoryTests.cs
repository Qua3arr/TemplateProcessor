using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure;
using TemplateProcessor.Infrastructure.Analyzers;
using TemplateProcessor.Infrastructure.Renderers;

namespace TemplateProcessor.Tests.Infrastructure
{
    public class FactoryTests
    {
        [Fact]
        public void TemplateAnalyzerFactory_ReturnsAnalyzer_ForSupportedFormat()
        {
            var factory = new TemplateAnalyzerFactory(
                new WordTemplateAnalyzer(),
                new ExcelTemplateAnalyzer(),
                new LatexTemplateAnalyzer());

            var analyzer = factory.Create(TemplateFormat.Excel);

            Assert.IsType<ExcelTemplateAnalyzer>(analyzer);
        }

        [Fact]
        public void RenderingEngineFactory_ReturnsRenderer_ForSupportedConversion()
        {
            var factory = new RenderingEngineFactory(
                new WordRenderer(),
                new ExcelRenderer(),
                new LatexRenderer());

            var renderer = factory.Create(TemplateFormat.Word, OutputFormat.Pdf);

            Assert.IsType<WordRenderer>(renderer);
        }

        [Fact]
        public void RenderingEngineFactory_WhenConversionUnsupported_ThrowsUnsupportedFormatException()
        {
            var factory = new RenderingEngineFactory(
                new WordRenderer(),
                new ExcelRenderer(),
                new LatexRenderer());

            Assert.Throws<UnsupportedFormatException>(
                () => factory.Create(TemplateFormat.Excel, OutputFormat.Pdf));
        }
    }
}
