using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace AquaLogicSharp.Implementation;

public class SerialDataSource : IDataSource
{
    private SerialPort? _serial;
    private string _serialPortName;

    public bool ContinueReading { get; set; } = true;

    public SerialDataSource(string serialPortName)
    {
        _serialPortName = serialPortName;
    }

    public Task Connect()
    {
        _serial = new SerialPort(
            _serialPortName, 
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