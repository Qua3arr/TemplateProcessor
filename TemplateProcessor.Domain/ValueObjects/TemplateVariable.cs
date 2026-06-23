namespace TemplateProcessor.Domain.ValueObjects
{
    //описание одной переменной в шаблоне
    public record TemplateVariable
    {
        public string Name { get; init; }
        public VariableType Type { get; init; }

        public TemplateVariable(string name, VariableType type)
        {
            Name = name;
            Type = type;
        }
    }
}
