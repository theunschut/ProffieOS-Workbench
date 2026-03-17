namespace ProffieOS.Workbench.Models;

public class NamedStyle
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ArgString { get; set; } = "";
    public List<StyleArgument> Args { get; set; } = new();
    public int TemplateId { get; set; }
}
