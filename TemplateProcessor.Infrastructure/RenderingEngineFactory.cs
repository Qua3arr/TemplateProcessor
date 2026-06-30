using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure.Renderers;

namespace TemplateProcessor.Infrastructure
{
    public class RenderingEngineFactory : IRenderingEngineFactory
    {
        private readonly WordRenderer _wordRenderer;
        private readonly ExcelRenderer _excelRenderer;
        private readonly LatexRenderer _latexRenderer;

        public RenderingEngineFactory(
            WordRenderer wordRenderer,
            ExcelRenderer excelRenderer,
            LatexRenderer latexRenderer)
        {
            _wordRenderer = wordRenderer;
            _excelRenderer = excelRenderer;
            _latexRenderer = latexRenderer;
        }

        public IDocumentRenderer Create(TemplateFormat inputFormat, OutputFormat outputFormat)
        {
            return (inputFormat, outputFormat) switch
            {
                (TemplateFormat.Word, OutputFormat.Docx) => _wordRenderer,
                (TemplateFormat.Word, OutputFormat.Pdf) => _wordRenderer,
                (TemplateFormat.Excel, OutputFormat.Xlsx) => _excelRenderer,
                (TemplateFormat.Latex, OutputFormat.Tex) => _latexRenderer,
                (TemplateFormat.Latex, OutputFormat.Pdf) => _latexRenderer,
                _ => throw new UnsupportedFormatException(
                    $"Unsupported conversion: {inputFormat} -> {outputFormat}")
            };
        }
    }
}
