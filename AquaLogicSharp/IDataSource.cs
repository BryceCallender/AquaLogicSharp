using System.Threading.Tasks;

namespace AquaLogicSharp;

public interface IDataSource
{
    public bool ContinueReading { get; set; }
    
    public Task Connect();

    public byte Read();
    public void Write(byte[] buffer);
}