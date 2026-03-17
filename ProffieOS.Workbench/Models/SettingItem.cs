namespace ProffieOS.Workbench.Models;

public record BoolSettingItem(string BaseCmd, string Variable, string Label, bool Value);
public record IntSettingItem(string BaseCmd, string Variable, string Label, int Value);
