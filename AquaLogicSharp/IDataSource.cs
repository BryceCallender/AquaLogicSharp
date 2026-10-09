using System;
using System.Threading.Tasks;

namespace AquaLogicSharp;

public interface IDataSource
{
    public bool ContinueReading { get; set; }

    /// <summary>
    /// How many times each queued frame is written. Only raise this for adapters that
    /// lose single writes (see <see cref="Implementation.SocketDataSource"/>).
    /// </summary>
    public int TxBurstCount => 1;

    /// <summary>
    /// Delay between the writes of a burst. Zero sends every copy in a single write.
    /// </summary>
    public TimeSpan TxBurstDelay => TimeSpan.FromMilliseconds(1);
    
    public Task Connect();
    public void Disconnect();

    public byte Read();
    public void Write(byte[] buffer);
}
