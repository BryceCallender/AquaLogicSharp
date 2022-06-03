namespace AquaLogicSharp.Models;

public record DisplaySection
{
    public string Content { get; set; }
    public bool Blinking { get; set; }
    public int DisplayRow { get; set; }
}