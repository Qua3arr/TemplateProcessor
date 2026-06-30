using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Domain.Ports
{
    public interface ITemplateFormatResolver
    {
        TemplateFormat GetTemplateFormat(string templatePath);
    }
}
