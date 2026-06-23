namespace TemplateProcessor.Domain.ValueObjects
{
    //данные, которые в шаблон будут подставляться
    public record TemplateContext
    {
        //скалярные
        public Dictionary<string, object> Scalars { get; init; } = new();

        //табличные
        public Dictionary<string, IEnumerable<Dictionary<string, object>>> Collections { get; init; } = new();
    }
}
