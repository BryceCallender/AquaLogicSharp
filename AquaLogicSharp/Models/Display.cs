using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AquaLogicSharp.Models;

public class Display
{
    public List<List<DisplaySection>> DisplaySections { get; }
    public bool DisplayChanged { get; set; }

    private byte[] _displayData;

    private const byte BlinkingFlag = 1 << 7;
    private const byte DegreeSymbol = 0xDF;
    private const byte Colon = (byte)':';
    private const byte Space = (byte)' ';

    public Display()
    {
        DisplaySections = new List<List<DisplaySection>>();
        _displayData = Array.Empty<byte>();
    }

    public void Parse(byte[] bytes)
    {
        // Reset first, otherwise a rejected frame leaves the previous frame's flag set
        // and fires a second update for a display that never changed.
        if (bytes.Length == 0 || bytes[^1] != 0x0)
        {
            DisplayChanged = false;
            return;
        }

        if (_displayData.SequenceEqual(bytes) || IsBlinkOnlyDifference(_displayData, bytes))
        {
            DisplayChanged = false;
            return;
        }
        
        _displayData = bytes;
        DisplayChanged = true;
        DisplaySections.Clear();

        var byteReader = new ByteReader(bytes);
        var displaySections = new List<DisplaySection>();
        
        // Get past spaces if in front
        byteReader.ReadWhitespace();

        var displayBytes = byteReader.ReadDisplaySequence();
        while (!byteReader.IsEoF)
        {
            if (displayBytes.Length > 0)
            {
                var (content, isBlinking) = ParseUtf8Bytes(displayBytes);

                if (content.Contains(':'))
                {
                    var parts = content.Split(':');

                    displaySections.AddRange(parts.Select(part => new DisplaySection
                    {
                        Content = part,
                        Blinking = false
                    }));

                    displaySections.Insert(displaySections.Count - 1, new DisplaySection
                    {
                        Content = ":",
                        Blinking = true
                    });
                }
                else
                {
                    displaySections.Add(new DisplaySection
                    {
                        Content = content,
                        Blinking = isBlinking
                    }); 
                }
            }
            
            displayBytes = byteReader.ReadDisplaySequence();

            if (displayBytes.Length != 0)
                continue;

            var spaces = byteReader.ReadWhitespace();
            if (spaces <= 1)
                continue;

            DisplaySections.Add(displaySections);
            displaySections = new List<DisplaySection>();
        }

        // A frame that ends without trailing whitespace still has a pending section
        if (displaySections.Count > 0)
            DisplaySections.Add(displaySections);
    }

    /// <summary>
    /// True when the only thing separating the two frames is a character blinking:
    /// either the blink bit toggling, or a colon alternating with a blank. The clock
    /// flips those every frame and they would otherwise force a redraw each time.
    /// Anything else — including a single digit of a temperature changing — counts
    /// as a real update.
    /// </summary>
    private static bool IsBlinkOnlyDifference(byte[] oldData, byte[] newData)
    {
        if (oldData.Length != newData.Length)
            return false;

        var sawDifference = false;

        for (var i = 0; i < oldData.Length; i++)
        {
            if (oldData[i] == newData[i])
                continue;

            var oldChar = (byte)(oldData[i] & ~BlinkingFlag);
            var newChar = (byte)(newData[i] & ~BlinkingFlag);

            // Same glyph, only the blink bit moved
            if (oldChar == newChar)
            {
                sawDifference = true;
                continue;
            }

            // A colon flashing against a blank
            if ((oldChar == Colon && newChar == Space) || (oldChar == Space && newChar == Colon))
            {
                sawDifference = true;
                continue;
            }

            return false;
        }

        return sawDifference;
    }

    private static (string, bool) ParseUtf8Bytes(byte[] displayBytes)
    {
        var isBlinking = false;
        var content = new StringBuilder(displayBytes.Length);

        foreach (var @byte in displayBytes)
        {
            // 0xDF is the LCD's degree glyph. It has the high bit set but is not a
            // blinking '_', so it has to be handled before the blink bit is stripped.
            if (@byte == DegreeSymbol)
            {
                content.Append('°');
                continue;
            }

            if ((@byte & BlinkingFlag) == BlinkingFlag)
                isBlinking = true;

            // Everything is ASCII once the blink bit is cleared
            content.Append((char)(@byte & ~BlinkingFlag));
        }

        return (content.ToString(), isBlinking);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(DisplaySections);
    }
}