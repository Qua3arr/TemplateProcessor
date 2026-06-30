using TemplateProcessor.Domain.Exceptions;
using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure.Analyzers;

namespace TemplateProcessor.Infrastructure
{
    public class TemplateAnalyzerFactory : ITemplateAnalyzerFactory
    {
        private readonly WordTemplateAnalyzer _wordAnalyzer;
        private readonly ExcelTemplateAnalyzer _excelAnalyzer;
        private readonly LatexTemplateAnalyzer _latexAnalyzer;

        public TemplateAnalyzerFactory(
            WordTemplateAnalyzer wordAnalyzer,
            ExcelTemplateAnalyzer excelAnalyzer,
            LatexTemplateAnalyzer latexAnalyzer)
        {
            _wordAnalyzer = wordAnalyzer;
            _excelAnalyzer = excelAnalyzer;
            _latexAnalyzer = latexAnalyzer;
        }

        public ITemplateAnalyzer Create(TemplateFormat format)
        {
            return format switch
            {
                TemplateFormat.Word => _wordAnalyzer,
                TemplateFormat.Excel => _excelAnalyzer,
                TemplateFormat.Latex => _latexAnalyzer,
                _ => throw new UnsupportedFormatException($"Unsupported template format: {format}")
            };
        }
    }
}
