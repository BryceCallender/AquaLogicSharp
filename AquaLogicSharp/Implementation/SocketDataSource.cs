using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AquaLogicSharp.Implementation;

/// <param name="txBurstCount">
/// How many times to write each queued frame. Some Wi-Fi RS-485 bridges (e.g. the USR-W610)
/// cannot turn a single write around inside the ~1ms window the controller listens in after
/// its keep-alive, so the keypress is never seen. Repeating the frame puts one copy inside
/// the window. Defaults to 1; only raise it if single writes are lost. Values below 1 are
/// treated as 1.
/// </param>
/// <param name="txBurstDelay">
/// Delay between burst writes. Defaults to 1ms. <see cref="TimeSpan.Zero"/> sends every copy
/// back to back in a single write.
/// </param>
public class SocketDataSource(string host, int port, int txBurstCount = 1, TimeSpan? txBurstDelay = null) : IDataSource
{
    private Socket? _socket;

    private int READ_TIMEOUT_SECONDS = 5;
    
    public bool ContinueReading { get; set; } = true;

    public int TxBurstCount { get; } = Math.Max(1, txBurstCount);

    public TimeSpan TxBurstDelay { get; } =
        txBurstDelay is { } delay && delay >= TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);

    public async Task Connect()
    {
        _socket = new Socket(
            AddressFamily.InterNetwork, 
            SocketType.Stream,
            ProtocolType.Tcp);
            
        await _socket.ConnectAsync(host, port);
        _socket.ReceiveTimeout = READ_TIMEOUT_SECONDS * 1000;
    }

    public void Disconnect()
    {
        _socket?.Disconnect(false);
        _socket?.Close();
    }
    
    public byte Read()
    {
        if (_socket is null)
        {
            ContinueReading = false;
            return 0x0;
        }

        var bytes = new byte[1];

        // A zero length receive means the peer closed the connection. Ignoring it left the
        // read loop spinning on a stale byte forever.
        if (_socket.Receive(bytes) == 0)
        {
            ContinueReading = false;
            return 0x0;
        }

        return bytes[0];
    }

    public void Write(byte[] buffer)
    {
        _socket?.Send(buffer);
    }
}