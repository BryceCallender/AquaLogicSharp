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

        // Without this the port is never actually opened and every read fails
        _serial.Open();

        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        _serial?.Close();
    }

    public byte Read()
    {
        if (_serial is null)
        {
            ContinueReading = false;
            return 0x0;
        }

        // ReadByte returns -1 at end of stream, which Convert.ToByte would throw on
        var data = _serial.ReadByte();
        if (data == -1)
        {
            ContinueReading = false;
            return 0x0;
        }

        return (byte)data;
    }

    public void Write(byte[] buffer)
    {
        _serial?.Write(buffer, 0, buffer.Length);
    }
}