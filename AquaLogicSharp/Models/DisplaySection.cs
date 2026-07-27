namespace AquaLogicSharp.Models;

public record DisplaySection
{
    // The parser always assigns a value, so callers never have to null check a section
    public string Content { get; set; } = "";
    public bool Blinking { get; set; }
}