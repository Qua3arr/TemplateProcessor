using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Domain.Ports
{
    public interface IRenderingEngineFactory
    {
        IDocumentRenderer Create(TemplateFormat inputFormat, OutputFormat outputFormat);
    }
}
