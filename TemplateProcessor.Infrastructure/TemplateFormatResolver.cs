using TemplateProcessor.Domain.Ports;
using TemplateProcessor.Domain.ValueObjects;
using TemplateProcessor.Infrastructure.Helpers;

namespace TemplateProcessor.Infrastructure
{
    public class TemplateFormatResolver : ITemplateFormatResolver
    {
        public TemplateFormat GetTemplateFormat(string templatePath)
        {
            return FormatHelper.GetTemplateFormat(templatePath);
        }
    }
}
