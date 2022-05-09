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
        
        var viableBytes = new List<byte[]>();
        var byteData = new List<byte>();
        
        var index = 0;
        while (index < bytes.Length)
        {
            if (bytes[index] == 32)
            {
                // get past random spaces in the beginning
                while (bytes[index] == 32)
                {
                    index++;
                }
				
                // read the data
                while(bytes[index] != 32 && bytes[index] != 0) 
                {
                    byteData.Add(bytes[index]);
                    index++;

                    
                    if (bytes[index] == 32)
                    {
                        byteData.Add(bytes[index]);
                        
                        // if the byte was space check to see if theres data immediately after
                        // so that they can be grouped together
                        if (index + 1 < bytes.Length && bytes[index + 1] != 32)
                        {
                            index++;
                        }
                    }
                }

                if (byteData.Count <= 0) 
                    continue;
                
                viableBytes.Add(byteData.ToArray());
                byteData.Clear();
            }
            else
            {
                index++;
            }
        }
        
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
            
            DisplaySections.Add(new DisplaySection
            {
                Content = content,
                Blinking = !content.Contains(':') && isBlinking
            });
        }
    }

    public override string ToString()
    {
        return $"{nameof(DisplaySections)}: {JsonSerializer.Serialize(DisplaySections)}";
    }
}