using System;
using System.Linq;
using System.IO;
using System.IO.Ports;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using EnumsNET;
using Serilog;
using Serilog.Core;
using AquaLogicSharp.Models;

namespace AquaLogicSharp
{
    public class AquaLogic
    {
        private Socket? socket;
        private SerialPort? serial;
        private FileStream? fileStream;

        private Timer timer;

        private Action<byte[]> Write;
        private Func<byte> Read;

        private Logger _logger;
        
        public int PoolStates { get; set; }
        public int FlashingStates { get; set; }

        public bool IsMetric { get; set; }
        public int AirTemp { get; set; }
        public int SpaTemp { get; set; }
        public int PoolTemp { get; set; }

        public double PoolChlorinatorPercent { get; set; }
        public double SpaChlorinatorPercent { get; set; }
        public double SaltLevel { get; set; }
        public string CheckSystemMessage { get; set; }
        public string Status { get; set; }
        public bool IsHeaterEnabled { get; set; }

        public int PumpSpeed { get; set; }
        public int PumpPower { get; set; }

        public Queue<AquaLogicQueueInfo> SendQueue { get; }
        public bool MultiSpeedPump { get; set; }
        public bool HeaterAutoMode { get; set; } = true;
        
        #region Frame constants
        private const int FRAME_DLE = 0x10;
        private const int FRAME_STX = 0x02;
        private const int FRAME_ETX = 0x03;

        private const int READ_TIMEOUT = 5;

        // Local wired panel (black face with service button)
        private byte[] FRAME_TYPE_LOCAL_WIRED_KEY_EVENT = { 0x00, 0x02 };
        // Remote wired panel (white face)
        private byte[] FRAME_TYPE_REMOTE_WIRED_KEY_EVENT = { 0x00, 0x03 };
        // Wireless remote
        private byte[] FRAME_TYPE_WIRELESS_KEY_EVENT = { 0x00, 0x83 };
        private byte[] FRAME_TYPE_ON_OFF_EVENT = { 0x00, 0x05 };   //Seems to only work for some keys

        private byte[] FRAME_TYPE_KEEP_ALIVE = { 0x01, 0x01 };
        private byte[] FRAME_TYPE_LEDS = { 0x01, 0x02 };
        private byte[] FRAME_TYPE_DISPLAY_UPDATE = { 0x01, 0x03 };
        private byte[] FRAME_TYPE_LONG_DISPLAY_UPDATE = { 0x04, 0x0a };
        private byte[] FRAME_TYPE_PUMP_SPEED_REQUEST = { 0x0c, 0x01 };
        private byte[] FRAME_TYPE_PUMP_STATUS = { 0x00, 0x0c };
        #endregion

        public AquaLogic(int webPort = 8129)
        {
            SendQueue = new Queue<AquaLogicQueueInfo>();
            _logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .CreateLogger();
        }

        public async Task Connect(string host, int port)
        {
            await ConnectSocket(host, port);
        }

        public async Task ConnectSocket(string host, int port)
        {
            socket = new Socket(
                AddressFamily.InterNetwork, 
                SocketType.Stream,
                ProtocolType.Tcp);
            
            await socket.ConnectAsync(host, port);
            socket.ReceiveTimeout = READ_TIMEOUT * 1000;

            Read = ReadByteFromSocket;
            Write = WriteToSocket;
        }

        public void ConnectSerial(string serialPortName)
        {
            serial = new SerialPort(
                serialPortName, 
                19200,
                Parity.None,
                8,
                StopBits.Two);

            Read = ReadByteFromSerial;
            Write = WriteToSerial;
        }

        public void ConnectFile(string fileName)
        {
            fileStream = File.Open(fileName, FileMode.Open);
            Read = ReadByteFromFile;
            Write = WriteToFile;
        }

        public void DisconnectFile()
        {
            fileStream.Dispose();
        }

        public void CheckState(object? state)
        {
            if (state is null)
                return;
            
            var data = (AquaLogicQueueInfo)state;
            var desiredStates = data.DesiredStates ?? Array.Empty<DesiredState>();
            foreach (var desiredState in desiredStates)
            {
                // State hasnt changed
                if(GetState(desiredState.State) != desiredState.Enabled)
                {
                    if (data.Retries.HasValue)
                    {
                        --data.Retries;

                        if (data.Retries != 0)
                        {
                            _logger.Information("Requeued...");
                            SendQueue.Enqueue(data);
                        }
                    }
                }
                else
                {
                    _logger.Debug("state changed successfully");
                }
            }
        }

        public byte ReadByteFromSocket()
        {
            var bytes = new byte[1];
            socket?.Receive(bytes);
            return bytes[0];
        }
        
        public byte ReadByteFromSerial()
        {
            var data = serial?.ReadByte();
            return Convert.ToByte(data);
        }

        public byte ReadByteFromFile()
        {
            if (fileStream is null)
                return 0x0;
            
            return (byte)fileStream?.ReadByte();
        }

        public void WriteToSocket(byte[] buffer)
        {
            socket?.Send(buffer);
        }

        public void WriteToSerial(byte[] buffer)
        {
            serial?.Write(buffer, 0, 1);
        }

        public void WriteToFile(byte[] buffer)
        {
            fileStream?.Write(buffer);
        }

        public void SendFrame()
        {
            if (SendQueue.Count > 0)
            {
                var data = SendQueue.Dequeue();
                Write(data.Frame ?? Array.Empty<byte>());

                try
                {
                    if (data.DesiredStates?.Length > 0)
                    {
                        timer = new Timer(CheckState, data, 0, 2 * 1000);
                    }
                }
                catch (Exception)
                {
                    return;
                }
            }
        }

        public void Process(Action<AquaLogic> callback)
        {
            try
            {
                while (true)
                {
                    var byteRead = Read();

                    DateTime frameStartTime;
                    var frameRxTime = DateTime.Now;

                    while (true)
                    {
                        if (byteRead == FRAME_DLE)
                        {
                            frameStartTime = DateTime.Now;
                            var nextByte = Read();
                            if (nextByte == FRAME_STX)
                            {
                                break;
                            }
                            else
                            {
                                continue;
                            }
                        }

                        byteRead = Read();
                        var elapsed = DateTime.Now - frameRxTime;
                        if (elapsed.Seconds > READ_TIMEOUT)
                            return;
                    }

                    var frameData = new List<byte>();
                    byteRead = Read();

                    while (true)
                    {
                        if (byteRead == FRAME_DLE)
                        {
                            var nextByte = Read();
                            if (nextByte == FRAME_ETX)
                                break;
                            else if (nextByte != 0)
                            {
                            }
                        }

                        frameData.Add(byteRead);
                        byteRead = Read();
                    }

                    var frame = frameData.ToArray();
                    
                    var frameCRC = frame[^2..].FromBytes(ByteOrder.BigEndian);
                    frame = frame[..^2];

                    var calculatedCRC = FRAME_DLE + FRAME_STX;
                    foreach (var @byte in frame)
                        calculatedCRC += @byte;

                    if (frameCRC != calculatedCRC)
                    {
                        _logger.Warning("Bad CRC");
                        continue;
                    }
                        

                    var frameType = frame[..2];
                    frame = frame[2..];

                    if (frameType.SequenceEqual(FRAME_TYPE_KEEP_ALIVE))
                    {
                        if (SendQueue.Count > 0)
                            SendFrame();

                        continue;
                    }
                    else if(frameType.SequenceEqual(FRAME_TYPE_LOCAL_WIRED_KEY_EVENT))
                    {
                        _logger.Debug("{@time} Local Wired Key: {@frame}", frameStartTime, frame.Hexlify());
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_REMOTE_WIRED_KEY_EVENT))
                    {
                        _logger.Debug("{@time} Remote Wired Key: {@frame}", frameStartTime, frame.Hexlify());
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_WIRELESS_KEY_EVENT))
                    {
                        _logger.Debug("{@time} Wireless Key: {@frame}", frameStartTime, frame.Hexlify());
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_LEDS))
                    {
                        var states = frame[..4].FromBytes(ByteOrder.LittleEndian);
                        var flashingStates = frame[4..8].FromBytes(ByteOrder.LittleEndian);

                        states |= flashingStates;
                        if (HeaterAutoMode)
                            states |= (int)State.HEATER_AUTO_MODE;

                        if (states != PoolStates || flashingStates != FlashingStates)
                        {
                            PoolStates = states;
                            FlashingStates = flashingStates;
                            callback(this);
                        }
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_PUMP_SPEED_REQUEST))
                    {
                        var value = frame[..2].ToArray().FromBytes(ByteOrder.BigEndian);
                        _logger.Debug("{@time} Pump speed request: {@value}", frameStartTime, value);

                        if (PumpSpeed != value)
                        {
                            PumpSpeed = value;
                            callback(this);
                        }
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_PUMP_STATUS) && frame.Length >= 5)
                    {
                        MultiSpeedPump = true;
                        var speed = frame[2];
                        var power = ((((frame[3] & 0xf0) >> 4) * 1000) +
                             (((frame[3] & 0x0f)) * 100) +
                             (((frame[4] & 0xf0) >> 4) * 10) +
                             (((frame[4] & 0x0f))));

                        _logger.Debug("{@time} Pump speed: {@speed}, power: {@power} watts", frameStartTime, speed, power);
                        if (PumpPower != power)
                        {
                            PumpPower = power;
                            callback(this);
                        }
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_DISPLAY_UPDATE))
                    {
                        var utf8Frame = frame.ToList();
                        // Convert LCD-specific degree symbol and decode to utf-8
                        for(var i = 0; i < utf8Frame.Count; i++)
                        {
                            if (utf8Frame[i] == 0xDF)
                            {
                                utf8Frame[i] = 0xC2;
                                utf8Frame.Insert(i + 1, 0xB0);
                            }    
                        }

                        frame = utf8Frame.ToArray();

                        var text = System.Text.Encoding.UTF8.GetString(frame.ToArray());
                        text = Regex.Replace(text.TrimStart(), @"\s+", ",");
                        var parts = text.Split(',');
                        
                        _logger.Debug("{@time} Display update: {@parts}", frameStartTime, parts);

                        try
                        {
                            if (parts[1] == "Temp")
                            {
                                var value = int.Parse(parts[2][0..^2]);

                                if (parts[0] == "Pool")
                                {
                                    if (PoolTemp != value)
                                    {
                                        PoolTemp = value;
                                        callback(this);
                                    }
                                }
                                else if (parts[0] == "Spa")
                                {
                                    if (SpaTemp != value)
                                    {
                                        SpaTemp = value;
                                        callback(this);
                                    }
                                }
                                else if (parts[0] == "Air")
                                {
                                    if (AirTemp != value)
                                    {
                                        AirTemp = value;
                                        callback(this);
                                    }
                                }

                                IsMetric = parts[2][^1] == 'C';
                            }
                            else if (parts[1] == "Chlorinator")
                            {
                                var value = int.Parse(parts[2][0..^1]);

                                if (parts[0] == "Pool")
                                {
                                    if (PoolChlorinatorPercent != value)
                                    {
                                        PoolChlorinatorPercent = value;
                                        callback(this);
                                    }
                                }
                                else if (parts[0] == "Spa")
                                {
                                    if (SpaChlorinatorPercent != value)
                                    {
                                        SpaChlorinatorPercent = value;
                                        callback(this);
                                    }
                                }
                            }
                            else if (parts[0] == "Salt" && parts[1] == "Level")
                            {
                                var value = Math.Round(float.Parse(parts[2]), 1);

                                if (SaltLevel != value)
                                {
                                    SaltLevel = value;
                                    IsMetric = parts[3] == "g/L";
                                    callback(this);
                                }
                            }
                            else if (parts[0] == "Check" && parts[1] == "System")
                            {
                                var message = string.Join(" ", parts[2..]);
                                if (CheckSystemMessage != message)
                                {
                                    CheckSystemMessage = message;
                                    callback(this);
                                }
                            }
                            else if (parts[0] == "Heater1")
                            {
                                HeaterAutoMode = parts[1] == "Auto";
                            }
                        }
                        catch (Exception)
                        {

                        }
                    }
                    else if (frameType == FRAME_TYPE_LONG_DISPLAY_UPDATE)
                    {
                        continue;
                    }
                    else
                    {
                        _logger.Debug("{@time}: Unknown frame: {@type}, {@frame}", frameType.Hexlify(), frame.Hexlify());
                    }
                }
            }
            catch(Exception ex)
            {
                _logger.Error(ex.Message);
            }
        }

        public void AppendData(List<byte> frame, byte[] data)
        {
            foreach(var byteData in data)
            {
                frame.Add(byteData);
                if (byteData == FRAME_DLE)
                    frame.Add(0);
            }
        }

        public List<byte> GetKeyEventFrame(Key key)
        {
            var frame = new List<byte>
            {
                FRAME_DLE,
                FRAME_STX
            };

            if ((int)key > 0xFFFF)
            {
                AppendData(frame, FRAME_TYPE_WIRELESS_KEY_EVENT);
                AppendData(frame, new byte[] { 0x01 });
                AppendData(frame, ((int)key).ToBytes(4, ByteOrder.LittleEndian));
                AppendData(frame, ((int)key).ToBytes(4, ByteOrder.LittleEndian));
                AppendData(frame, new byte[] { 0x00 });
            }
            else
            {
                AppendData(frame, FRAME_TYPE_LOCAL_WIRED_KEY_EVENT);
                AppendData(frame, ((int)key).ToBytes(2, ByteOrder.LittleEndian));
                AppendData(frame, ((int)key).ToBytes(2, ByteOrder.LittleEndian));
            }

            int crc = 0;
            foreach (var frameByte in frame)
            {
                crc += frameByte;
            }

            AppendData(frame, crc.ToBytes(2, ByteOrder.BigEndian));

            frame.Add(FRAME_DLE);
            frame.Add(FRAME_ETX);

            return frame;
        }

        public void SendKey(Key key)
        {
            _logger.Information("Queueing Key: {@key}", key);
            var frame = GetKeyEventFrame(key);

            SendQueue.Enqueue(new AquaLogicQueueInfo { Frame = frame.ToArray() });
        }

        public List<State> GetStates()
        {
            var stateList = new List<State>();

            foreach(var value in Enums.GetValues<State>())
            {
                if(((int)value & PoolStates) != 0)
                    stateList.Add(value);
            }

            if ((FlashingStates & (int)State.FILTER) != 0)
                stateList.Add(State.FILTER_LOW_SPEED);

            return stateList;
        }

        public bool GetState(State state)
        {
            // Check to see if we have a change request pending; if we do
            // return the value we expect it to change to.
            foreach (var data in SendQueue)
            {
                foreach (var desiredState in data.DesiredStates ?? Array.Empty<DesiredState>())
                {
                    if (desiredState.State == state)
                        return desiredState.Enabled;
                }
            }

            if (state == State.FILTER_LOW_SPEED)
                return ((int)State.FILTER & FlashingStates) != 0;

            return ((int)state & PoolStates) != 0;
        }

        public bool SetState(State state, bool enable)
        {
            var isStateEnabled = GetState(state);
            if (enable == isStateEnabled)
                return true;

            var key = Key.NONE;
            var desiredStates = new List<DesiredState>();

            if (state.HasFlag(State.FILTER_LOW_SPEED))
            {
                /***
                 * Send the FILTER key once.
                 * If the pump is in high speed, it wil switch to low speed.
                 * If the pump is off the retry mechanism will send an additional
                 * FILTER key to switch into low speed.
                 * If the pump is in low speed then we pretend the pump is off;
                 * the retry mechanism will send an additional FILTER key
                 * to switch into high speed.
                ***/
                key = Key.FILTER;
                
            }
            else if (state.HasFlag(State.HEATER_AUTO_MODE))
            {
                key = Key.HEATER_1;
                desiredStates.Add(new DesiredState
                {
                    State = State.HEATER_AUTO_MODE,
                    Enabled = !HeaterAutoMode
                });
            }
            else if (state.HasFlag(State.POOL | State.SPA))
            {
                key = Key.POOL_SPA;
                desiredStates.Add(new DesiredState
                {
                    State = state,
                    Enabled = !isStateEnabled
                });
            }
            else if (state.HasFlag(State.HEATER_1))
            {
                //TODO: is there a way to force the heater on?
                //Perhaps press & hold?
                return false;
            }
            else
            {
                try
                {
                    key = Enums.Parse<Key>(nameof(state));
                }
                catch (Exception)
                {
                    return false;
                }

                desiredStates.Add(new DesiredState
                {
                    State = state,
                    Enabled = !isStateEnabled
                });
            }

            var frame = GetKeyEventFrame(key);

            SendQueue.Enqueue(new AquaLogicQueueInfo
            {
                Frame = frame.ToArray(),
                DesiredStates = desiredStates.ToArray(),
                Retries = 10
            });

            return true;
        }

        public bool EnableMultiSpeedPump(bool enable)
        {
            MultiSpeedPump = enable;
            return true;
        }
    }
}