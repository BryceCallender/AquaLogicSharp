using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using static System.Text.Encoding;

namespace AquaLogicSharp.Models;

public class Display
{
    public List<List<DisplaySection>> DisplaySections { get; }
    public bool DisplayChanged { get; set; }

    private byte[] _displayData;

    private const int BlinkingFlag = 1 << 7;        

    public Display()
    {
        DisplaySections = new List<List<DisplaySection>>();
        _displayData = Array.Empty<byte>();
    }

    public void Parse(byte[] bytes)
    {
        if (bytes[^1] != 0x0)
            return;

        if (_displayData.SequenceEqual(bytes))
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
    }

    private (string, bool) ParseUtf8Bytes(byte[] displayBytes)
    {
        var isBlinking = false;
        for (var i = 0; i < displayBytes.Length; i++)
        {
            var @byte = displayBytes[i];

            if ((@byte & BlinkingFlag) != BlinkingFlag) 
                continue;
                
            displayBytes[i] = (byte)(@byte & ~BlinkingFlag);
            isBlinking = true;
        }
                
        var content = UTF8.GetString(displayBytes);
        return (content.Replace("B0", "°"), isBlinking);
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(DisplaySections);
    }
}