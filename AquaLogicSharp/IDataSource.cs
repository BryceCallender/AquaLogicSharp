using System.Threading.Tasks;

namespace AquaLogicSharp;

public interface IDataSource
{
    public bool ContinueReading { get; set; }
    
    public Task Connect();
    public void Disconnect();

    public byte Read();
    public void Write(byte[] buffer);
}