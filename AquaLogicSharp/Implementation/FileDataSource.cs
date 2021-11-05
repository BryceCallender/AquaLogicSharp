using System.IO;
using System.Threading.Tasks;

namespace AquaLogicSharp.Implementation;

public class FileDataSource : IDataSource
{
    private FileStream? _fileStream;
    private string _fileName;

    public FileDataSource(string fileName)
    {
        _fileName = fileName;
    }
    
    public Task Connect()
    {
        _fileStream = File.Open(_fileName, FileMode.Open);
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
            
        return (byte)_fileStream.ReadByte();
    }

    public void Write(byte[] buffer)
    {
        _fileStream?.Write(buffer);
    }
}