using System.Collections.Generic;

namespace AquaLogicSharp;

public class ByteReader(byte[] bytes)
{
    private int _index = 0;
    private const byte EOF = 0;
    private const byte Space = 32;

    public bool IsEoF => bytes[_index] == EOF;

    public int ReadWhitespace()
    {
        var byteData = new List<byte>();
        
        var readByte = ReadByte();
        while (readByte == Space)
        { 
            byteData.Add(readByte);
            Advance();
            readByte = ReadByte();
        }

        return byteData.Count;
    }

    private byte[] ReadTill(byte value)
    {
        var byteData = new List<byte>();
        
        var readByte = ReadByte();
        while (readByte != value && readByte != EOF)
        {
            byteData.Add(readByte);
            Advance();
            readByte = ReadByte();
        }

        return byteData.ToArray();
    }

    public byte[] ReadDisplaySequence()
    {
        return ReadTill(Space);
    }

    private byte ReadByte()
    {
        return _index == bytes.Length ? EOF : bytes[_index];
    }

    private void Advance()
    {
        ++_index;
    }
}