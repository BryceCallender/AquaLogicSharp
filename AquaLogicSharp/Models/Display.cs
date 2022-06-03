using System;
using System.Collections.Generic;
using System.Text.Json;
using static System.Text.Encoding;

namespace AquaLogicSharp.Models;

public class Display
{
    public List<DisplaySection> DisplaySections { get; }

    private const int BlinkingFlag = 1 << 7;        

    public Display()
    {
        DisplaySections = new List<DisplaySection>();
    }

    public void Parse(byte[] bytes)
    {
        if (bytes[^1] != 0x0)
            return;
        
        DisplaySections.Clear();

        var byteReader = new ByteReader(bytes);
        var viableBytes = new List<byte[]>();
        var rowValues = new List<int>();
        
        var row = 1;

        // Get past spaces if in front
        byteReader.ReadWhitespace();

        var displayBytes = byteReader.ReadDisplaySequence();
        while (!byteReader.IsEoF)
        {
            if (displayBytes.Length > 0)
            {
                viableBytes.Add(displayBytes);
                rowValues.Add(row);
            }
            
            displayBytes = byteReader.ReadDisplaySequence();

            if (displayBytes.Length != 0) 
                continue;
            
            var spaces = byteReader.ReadWhitespace();
            if (spaces > 1)
                row++;
        }

        var index = 0;
        foreach (var viableByteCollection in viableBytes) 
        {
            var isBlinking = false;
            for (var i = 0; i < viableByteCollection.Length; i++)
            {
                var @byte = viableByteCollection[i];

                if ((@byte & BlinkingFlag) != BlinkingFlag) 
                    continue;
                
                viableByteCollection[i] = (byte)(@byte & ~BlinkingFlag);
                isBlinking = true;
            }

            var content = UTF8.GetString(viableByteCollection);
            content = content.Replace("B0", "°");
            
            DisplaySections.Add(new DisplaySection
            {
                Content = content,
                Blinking = !content.Contains(':') && isBlinking,
                DisplayRow = rowValues[index++]
            });
        }
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(DisplaySections);
    }
}