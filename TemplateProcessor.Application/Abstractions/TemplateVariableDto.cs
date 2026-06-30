using TemplateProcessor.Domain.ValueObjects;

namespace TemplateProcessor.Application.Abstractions
{
    public class TemplateVariableDto
    {
        public string Name { get; set; } = string.Empty;

        public VariableType Type { get; set; }
    }
}
