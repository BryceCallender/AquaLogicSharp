using System.IO;
using System.Threading.Tasks;

namespace AquaLogicSharp.Implementation;

public class FileDataSource(string fileName) : IDataSource
{
    private FileStream? _fileStream;

    public bool ContinueReading { get; set; } = true;

    public Task Connect()
    {
        _fileStream = File.Open(fileName, FileMode.Open);
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        _fileStream?.Close();
    }

    public byte Read()
    {
        if (_fileStream is null)
            return 0x0;
        
        var data = _fileStream.ReadByte();
        if (data == -1)
            ContinueReading = false;
            
        return (byte)data;
    }

    public void Write(byte[] buffer)
    {
        _fileStream?.Write(buffer);
    }
}