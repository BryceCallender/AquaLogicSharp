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
        private IDataSource _dataSource;

        private Timer? _timer;
        
        private readonly Logger _logger;
        
        public State PoolStates { get; set; }
        public State FlashingStates { get; set; }

        public bool IsMetric { get; private set; }
        public int? AirTemp { get; private set; }
        public int? SpaTemp { get; private set; }
        public int? PoolTemp { get; private set; }

        public double? PoolChlorinatorPercent { get; private set; }
        public double? SpaChlorinatorPercent { get; private set; }
        public double? SaltLevel { get; private set; }

        private string CheckSystemMessage { get; set; }

        public string Status => GetState(State.CHECK_SYSTEM) ? CheckSystemMessage : "Ok";

        public bool? IsHeaterEnabled => GetState(State.HEATER_1);

        public bool? IsSuperChlorinate => GetState(State.SUPER_CHLORINATE);

        public bool? Waterfall => GetState(State.AUX_2); //Waterfall is aux2

        public int? PumpSpeed { get; set; }
        public int? PumpPower { get; set; }

        public Queue<AquaLogicQueueInfo> SendQueue { get; }
        public bool? MultiSpeedPump { get; set; }
        public bool HeaterAutoMode { get; set; } = true;
        
        #region Frame constants
        private const int FRAME_DLE = 0x10;
        private const int FRAME_STX = 0x02;
        private const int FRAME_ETX = 0x03;

        private const int READ_TIMEOUT_SECONDS = 5;

        // Local wired panel (black face with service button)
        private byte[] FRAME_TYPE_LOCAL_WIRED_KEY_EVENT = { 0x00, 0x02 };
        // Remote wired panel (white face)
        private byte[] FRAME_TYPE_REMOTE_WIRED_KEY_EVENT = { 0x00, 0x03 };
        // Wireless remote
        private byte[] FRAME_TYPE_WIRELESS_KEY_EVENT = { 0x00, 0x8C };
        private byte[] FRAME_TYPE_ON_OFF_EVENT = { 0x00, 0x05 };   //Seems to only work for some keys

        private byte[] FRAME_TYPE_KEEP_ALIVE = { 0x01, 0x01 };
        private byte[] FRAME_TYPE_LEDS = { 0x01, 0x02 };
        private byte[] FRAME_TYPE_DISPLAY_UPDATE = { 0x01, 0x03 };
        private byte[] FRAME_TYPE_LONG_DISPLAY_UPDATE = { 0x04, 0x0a };
        private byte[] FRAME_TYPE_PUMP_SPEED_REQUEST = { 0x0c, 0x01 };
        private byte[] FRAME_TYPE_PUMP_STATUS = { 0x00, 0x0c };
        #endregion

        public AquaLogic()
        {
            SendQueue = new Queue<AquaLogicQueueInfo>();
            PoolStates = State.EMPTY;
            FlashingStates = State.EMPTY;

            _logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .CreateLogger();
        }

        public void Connect(IDataSource dataSource)
        {
            _dataSource = dataSource;
            _dataSource.Connect();
        }

        private void CheckState(object? state)
        {
            if (state is null)
                return;
            
            var data = (AquaLogicQueueInfo)state;
            var desiredStates = data.DesiredStates ?? Array.Empty<DesiredState>();
            foreach (var desiredState in desiredStates)
            {
                // State hasn't changed
                if(GetState(desiredState.State) != desiredState.Enabled)
                {
                    if (!data.Retries.HasValue)
                        continue;

                    if (--data.Retries != 0)
                    {
                        _logger.Information("Requeued...");
                        //SendQueue.Enqueue(data);
                    }
                }
                else
                {
                    _logger.Debug("state changed successfully");
                }
            }
        }

        private async Task SendFrame()
        {
            if (SendQueue.Count <= 0) 
                return;
            
            var data = SendQueue.Dequeue();
            _logger.Information("Sent Frame: {@frame}", data.Frame);
            
            await Task.Delay(50);
            await SendBurst(data.Frame ?? Array.Empty<byte>());
        
            if (data.DesiredStates?.Length > 0)
            {
                _timer = new Timer(CheckState, data, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
            }
        }

        private async Task SendBurst(byte[] frame)
        {
            _logger.Information("Performing a burst write for frame... {@frame}", frame.Hexlify());
            foreach (var i in Enumerable.Range(1,5))
            {
                _dataSource.Write(frame);
                await Task.Delay(8);
            }
        }

        public async Task Process(Action<AquaLogic> callback)
        {
            try
            {
                while (true)
                {
                    var byteRead = _dataSource.Read();
                    var frameRxTime = DateTime.Now;

                    while (true)
                    {
                        if (byteRead == FRAME_DLE)
                        {
                            var nextByte = _dataSource.Read();
                            if (nextByte == FRAME_STX)
                            {
                                break;
                            }
                            
                            continue;
                        }

                        byteRead = _dataSource.Read();
                        var elapsed = DateTime.Now - frameRxTime;
                        if (elapsed.Seconds > READ_TIMEOUT_SECONDS)
                            return;
                    }

                    var frameData = new List<byte>();
                    byteRead = _dataSource.Read();

                    while (true)
                    {
                        if (byteRead == FRAME_DLE)
                        {
                            var nextByte = _dataSource.Read();
                            if (nextByte == FRAME_ETX)
                                break;

                            if (nextByte != 0)
                                _logger.Error($"Frame ETX ({FRAME_ETX}) must come after DLE ({FRAME_DLE})");
                        }

                        frameData.Add(byteRead);
                        byteRead = _dataSource.Read();
                    }

                    var frame = frameData.ToArray();
                    
                    //_logger.Information("{frame}",  frame.Hexlify());
                    
                    var frameCRC = frame[^2..].FromBytes(ByteOrder.BigEndian);
                    frame = frame[..^2];

                    var calculatedCRC = frame.Aggregate(FRAME_DLE + FRAME_STX, (current, @byte) => current + @byte);

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
                            await SendFrame();

                        continue;
                    }
                    else if(frameType.SequenceEqual(FRAME_TYPE_LOCAL_WIRED_KEY_EVENT))
                    {
                        _logger.Debug("Local Wired Key: {@frame}", frame.Hexlify());
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_REMOTE_WIRED_KEY_EVENT))
                    {
                        _logger.Debug("Remote Wired Key: {@frame}", frame.Hexlify());
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_WIRELESS_KEY_EVENT))
                    {
                        _logger.Debug("Wireless Key: {@frame}", frame.Hexlify());
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_LEDS))
                    {
                        var states = (State)frame[..4].FromBytes(ByteOrder.LittleEndian);
                        var flashingStates = (State)frame[4..8].FromBytes(ByteOrder.LittleEndian);
                        
                        states |= flashingStates;
                        if (HeaterAutoMode)
                            states |= State.HEATER_AUTO_MODE;

                        // Left this one to avoid dual callbacks
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
                        _logger.Debug("Pump speed request: {@value}", value);

                        PumpSpeed = Convert.ToInt32(CompareAndCallback(PumpSpeed, value, callback));
                    }
                    else if (frameType.SequenceEqual(FRAME_TYPE_PUMP_STATUS) && frame.Length >= 5)
                    {
                        MultiSpeedPump = true;
                        var speed = frame[2];
                        var power = ((((frame[3] & 0xf0) >> 4) * 1000) +
                             (((frame[3] & 0x0f)) * 100) +
                             (((frame[4] & 0xf0) >> 4) * 10) +
                             (((frame[4] & 0x0f))));

                        _logger.Debug("Pump speed: {@speed}, power: {@power} watts", speed, power);
                        PumpPower = Convert.ToInt32(CompareAndCallback(PumpPower, power, callback));
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

                            if (utf8Frame[i] == 0xBA) //random byte that should be :
                            {
                                utf8Frame[i] = 0x3A; // : char in bytes
                            }
                        }

                        frame = utf8Frame.ToArray();

                        var text = System.Text.Encoding.UTF8.GetString(frame.ToArray());
                        text = Regex.Replace(text.TrimStart(), @"\s+", ",");
                        var parts = text.Split(',');
                        
                        _logger.Debug("Display update: {@parts}", parts);

                        try
                        {
                            switch (parts[1])
                            {
                                case "Temp":
                                {
                                    var value = int.Parse(parts[2][..^2]);

                                    switch (parts[0])
                                    {
                                        case "Pool": PoolTemp = Convert.ToInt32(CompareAndCallback(PoolTemp, value, callback));
                                            break;
                                        case "Spa": SpaTemp = Convert.ToInt32(CompareAndCallback(SpaTemp, value, callback));
                                            break;
                                        case "Air": AirTemp = Convert.ToInt32(CompareAndCallback(AirTemp, value, callback));
                                            break;
                                    }
                                
                                    IsMetric = parts[2][^1] == 'C';
                                    break;
                                }
                                case "Chlorinator":
                                {
                                    var value = int.Parse(parts[2][..^1]);
                                
                                    switch (parts[0])
                                    {
                                        case "Pool": PoolChlorinatorPercent = Convert.ToDouble(CompareAndCallback(PoolChlorinatorPercent, value, callback));
                                            break;
                                        case "Spa": SpaChlorinatorPercent = Convert.ToDouble(CompareAndCallback(SpaChlorinatorPercent, value, callback));
                                            break;
                                    }

                                    break;
                                }
                                case "Level" when parts[0] == "Salt":
                                {
                                    var value = Math.Round(float.Parse(parts[2]), 1);
                                    SaltLevel = Convert.ToDouble(CompareAndCallback(SaltLevel, value, callback));
                                    IsMetric = parts[3] == "g/L";
                                    break;
                                }
                                case "System" when parts[0] == "Check":
                                {
                                    var message = string.Join(" ", parts[2..]);
                                    CheckSystemMessage = Convert.ToString(CompareAndCallback( CheckSystemMessage, message, callback));
                                    break;
                                }
                                case "Heater1":
                                {
                                    HeaterAutoMode = parts[1] == "Auto";
                                    break;
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // ignored
                        }
                    }
                    else if (frameType == FRAME_TYPE_LONG_DISPLAY_UPDATE)
                    {
                        //Does nothing at the moment
                        continue;
                    }
                    else
                    {
                        //_logger.Debug("Unknown frame: {@type}, {@frame}", frameType.Hexlify(), frame.Hexlify());
                    }
                }
            }
            catch(Exception ex)
            {
                _logger.Error(ex.Message);
            }
        }

        private object CompareAndCallback(object a, object b, Action<AquaLogic> callback)
        {
            if (a.Equals(b))
                return a;
            
            callback(this);
            return b;
        }
        
        private void AppendData(ICollection<byte> frame, IEnumerable<byte> data)
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
            //Must always start with 0x10 0x02 and then end with 0x10 0x03
            var frame = new List<byte>
            {
                FRAME_DLE, //0x10
                FRAME_STX  //0x02
            };
            
            AppendData(frame, FRAME_TYPE_WIRELESS_KEY_EVENT); //0x00 0x83 (0x8C for my system)
            AppendData(frame, new byte[] { 0x01 });
            AppendData(frame, ((int)key).ToBytes(4, ByteOrder.LittleEndian));
            AppendData(frame, ((int)key).ToBytes(4, ByteOrder.LittleEndian));
            AppendData(frame, new byte[] { 0x00 });

            var crc = frame.Aggregate(0, (current, frameByte) => current + frameByte);

            AppendData(frame, crc.ToBytes(2, ByteOrder.BigEndian));

            frame.Add(FRAME_DLE); //0x10
            frame.Add(FRAME_ETX); //0x03
            
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
            var stateList = Enums.GetValues<State>().Where(value => (value & PoolStates) != 0).ToList();

            if ((FlashingStates & State.FILTER) != 0)
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
                return (State.FILTER & FlashingStates) != 0;

            return (state & PoolStates) != 0;
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
                    key = Enums.Parse<Key>(state.GetName() ?? "None");
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
            var test = frame.ToArray().Hexlify();

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