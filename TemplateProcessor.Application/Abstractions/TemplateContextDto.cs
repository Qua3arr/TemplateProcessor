using System.Collections.Generic;

namespace TemplateProcessor.Application.Abstractions
{
    public class TemplateContextDto
    {
        public Dictionary<string, object> Scalars { get; set; } = new();

        public Dictionary<string, IEnumerable<Dictionary<string, object>>> Collections { get; set; } = new();
    }
}
