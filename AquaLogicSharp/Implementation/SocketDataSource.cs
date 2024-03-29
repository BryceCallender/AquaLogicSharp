using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace AquaLogicSharp.Implementation;

public class SocketDataSource(string host, int port) : IDataSource
{
    private Socket? _socket;

    private int READ_TIMEOUT_SECONDS = 5;
    
    public bool ContinueReading { get; set; } = true;

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
        var bytes = new byte[1];
        _socket?.Receive(bytes);
        return bytes[0];
    }

    public void Write(byte[] buffer)
    {
        _socket?.Send(buffer);
    }
}