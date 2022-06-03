using System.Collections.Generic;

namespace AquaLogicSharp;

public class ByteReader
{
    private byte[] _bytes;
    private int _index;
    private const byte EOF = 0;
    private const byte Space = 32;

    public bool IsEoF => _bytes[_index] == EOF;

    public ByteReader(byte[] bytes)
    {
        _bytes = bytes;
        _index = 0;
    }

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
        return _index == _bytes.Length ? EOF : _bytes[_index];
    }

    private void Advance()
    {
        ++_index;
    }
}