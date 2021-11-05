using System.Threading.Tasks;

namespace AquaLogicSharp;

public interface IDataSource
{
    public Task Connect();

    public byte Read();
    public void Write(byte[] buffer);
}