using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace AquaLogicSharp.Implementation;

public class SerialDataSource(string serialPortName) : IDataSource
{
    private SerialPort? _serial;

    public bool ContinueReading { get; set; } = true;

    public Task Connect()
    {
        _serial = new SerialPort(
            serialPortName, 
            19200,
            Parity.None,
            8,
            StopBits.Two);
        
        return Task.CompletedTask;
    }

    public byte Read()
    {
        var data = _serial?.ReadByte();
        return Convert.ToByte(data);
    }

    public void Write(byte[] buffer)
    {
        _serial?.Write(buffer, 0, buffer.Length);
    }
}