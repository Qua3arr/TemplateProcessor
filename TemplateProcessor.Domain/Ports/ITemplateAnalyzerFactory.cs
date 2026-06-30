using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Domain.Ports
{
    public interface ITemplateAnalyzerFactory
    {
        ITemplateAnalyzer Create(TemplateFormat format);
    }
}
