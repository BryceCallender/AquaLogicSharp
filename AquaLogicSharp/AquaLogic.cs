using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using EnumsNET;
using Serilog;
using Serilog.Core;
using AquaLogicSharp.Models;

namespace AquaLogicSharp
{
    public class AquaLogic : IDisposable
    {
        private IDataSource _dataSource = null!;
        
        private Timer? _timer;
        
        private readonly Logger _logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Debug()
            .CreateLogger();
        
        // Producers (SendKey/SetState) run on the caller's thread and CheckState runs on a
        // timer thread, while the read loop consumes. All three need a thread safe queue.
        private ConcurrentQueue<AquaLogicQueueInfo> SendQueue { get; } = new();

        public List<Variance> Variances { get; set; } = new();

        public Display Display { get; set; } = new();

        public bool AttemptingRequest { get; set; }

        public string[]? PoolStates => _poolStates.ToStateArray();
        private State _poolStates { get; set; } = State.EMPTY;

        public string[]? FlashingStates => _flashingStates.ToStateArray();
        private State _flashingStates { get; set; } = State.EMPTY;

        public bool IsMetric { get; private set; }
        public int? AirTemp { get; private set; }
        public int? SpaTemp { get; private set; }
        public int? PoolTemp { get; private set; }

        public double? PoolChlorinatorPercent { get; private set; }
        public double? SpaChlorinatorPercent { get; private set; }
        public double? SaltLevel { get; private set; }

        private string? CheckSystemMessage { get; set; }
        public string? Status => GetState(State.CHECK_SYSTEM) ? CheckSystemMessage : "Ok";
        public bool? IsHeaterEnabled => GetState(State.HEATER_1);
        public bool? IsSuperChlorinate => GetState(State.SUPER_CHLORINATE);
        public bool? Waterfall => GetState(State.AUX_2); //Waterfall is aux2

        public int? PumpSpeed { get; set; }
        public int? PumpPower { get; set; }
        public bool? MultiSpeedPump { get; set; }
        public bool HeaterAutoMode { get; set; } = true;

        public bool MenuLocked { get; set; }

        private Action<AquaLogic> Callback { get; set; } = null!;
        
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
        private byte[] FRAME_TYPE_PUMP_SPEED_REQUEST = { 0x0C, 0x01 };
        private byte[] FRAME_TYPE_PUMP_STATUS = { 0x00, 0x0C };
        #endregion

        public async Task Connect(IDataSource dataSource)
        {
            _dataSource = dataSource;
            await _dataSource.Connect();
        }

        private void CheckState(object? state)
        {
            if (state is null)
                return;
            
            _logger.Information("Checking pool state...");
            
            var data = (AquaLogicQueueInfo)state;
            var desiredStates = data.DesiredStates ?? Array.Empty<DesiredState>();
            foreach (var desiredState in desiredStates)
            {
                // State hasn't changed
                if (GetState(desiredState.State) != desiredState.Enabled)
                {
                    if (!data.Retries.HasValue)
                        continue;

                    if (--data.Retries != 0)
                    {
                        _logger.Information("Requeue...");
                        SendQueue.Enqueue(data);
                    }
                    else
                    {
                        AlertAttemptingRequest(false); // attempted as many as it could
                    }
                }
                else
                {
                    AlertAttemptingRequest(false);
                    _logger.Information("state changed successfully");
                }
            }
        }

        private async Task SendFrame()
        {
            if (!SendQueue.TryDequeue(out var data))
                return;

            if (!AttemptingRequest)
            {
                AlertAttemptingRequest(true);
            }

            await SendBurst(data.Frame ?? Array.Empty<byte>());
            _logger.Information("Sent Frame: {Frame}", data.Frame?.Hexlify());

            if (data.DesiredStates?.Length > 0)
            {
                // Replace rather than leak the previous one-shot timer
                var previous = Interlocked.Exchange(
                    ref _timer,
                    new Timer(CheckState, data, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan));

                previous?.Dispose();
            }
        }

        private async Task SendBurst(byte[] frame)
        {
            _logger.Information("Performing a burst write for frame... {Frame}", frame.Hexlify());
            foreach (var _ in Enumerable.Range(1, 20))
            {
                _dataSource.Write(frame);
                //_logger.Information("Wrote frame at... {Time}", DateTime.Now.TimeOfDay);
                await Task.Delay(10);
            }
        }
        
        public async Task Process(Action<AquaLogic> callback, CancellationToken cancellationToken = default)
        {
            Callback = callback;

            while (_dataSource.ContinueReading && !cancellationToken.IsCancellationRequested)
            {
                // Scoped per frame: a malformed frame or a transient read error should cost
                // us that frame, not the whole session.
                try
                {
                    if (!ReadUntilSTX())
                        continue;

                    var frame = ReadFrameData().ToArray();

                    // Two bytes of frame type plus two of CRC is the smallest legal frame
                    if (frame.Length < 4)
                    {
                        _logger.Warning("Runt frame of {Length} byte(s), discarding", frame.Length);
                        continue;
                    }

                    var frameCRC = frame[^2..].FromBytes(ByteOrder.BigEndian);
                    frame = frame[..^2];

                    var calculatedCRC = frame.Aggregate(FRAME_DLE + FRAME_STX, (current, @byte) => current + @byte) & 0xFFFF;

                    if (frameCRC != calculatedCRC)
                    {
                        _logger.Warning("Bad CRC: Got {Calculated}, expected {Frame}", calculatedCRC, frameCRC);
                        continue;
                    }

                    var frameType = frame[..2];
                    frame = frame[2..];

                    await HandleFrames(frameType, frame);
                }
                catch (Exception ex)
                {
                    _logger.Error("{Error}", ex.Message);
                }
            }
        }

        /// <returns>true when an STX was found, false on timeout or a closed source.</returns>
        private bool ReadUntilSTX()
        {
            var byteRead = 0;
            var frameRxTime = DateTime.Now;

            while (_dataSource.ContinueReading)
            {
                // TotalSeconds, not Seconds - the latter wraps at 60 and stops tripping
                if ((DateTime.Now - frameRxTime).TotalSeconds > READ_TIMEOUT_SECONDS)
                {
                    _logger.Error("Frame timeout while waiting for STX");
                    return false;
                }

                if (byteRead == FRAME_DLE)
                {
                    byteRead = _dataSource.Read();
                    if (byteRead == FRAME_STX)
                        return true;

                    continue;
                }

                byteRead = _dataSource.Read();
            }

            return false;
        }

        private List<byte> ReadFrameData()
        {
            var frameData = new List<byte>();
            var byteRead = _dataSource.Read();

            while (_dataSource.ContinueReading)
            {
                if (byteRead == FRAME_DLE)
                {
                    var nextByte = _dataSource.Read();
                    if (nextByte == FRAME_ETX)
                    {
                        break;
                    }

                    if (nextByte != 0)
                    {
                        _logger.Error("Frame ETX ({Etx}) must come after DLE ({Dle})", FRAME_ETX, FRAME_DLE);
                    }
                }

                frameData.Add(byteRead);
                byteRead = _dataSource.Read();
            }

            return frameData;
        }

        private async Task HandleFrames(byte[] frameType, byte[] frame)
        {
            if (frameType.SequenceEqual(FRAME_TYPE_KEEP_ALIVE))
            {
                //_logger.Debug("Keep Alive: {Time}", DateTime.Now.TimeOfDay);
                
                if (!SendQueue.IsEmpty)
                    await SendFrame();
            }
            else if(frameType.SequenceEqual(FRAME_TYPE_LOCAL_WIRED_KEY_EVENT))
            {
                _logger.Debug("Local Wired Key: {Frame}", frame.Hexlify());
            }
            else if (frameType.SequenceEqual(FRAME_TYPE_REMOTE_WIRED_KEY_EVENT))
            {
                _logger.Debug("Remote Wired Key: {Frame}", frame.Hexlify());
            }
            else if (frameType.SequenceEqual(FRAME_TYPE_WIRELESS_KEY_EVENT))
            {
                _logger.Debug("Wireless Key: {Frame}", frame.Hexlify());
            }
            else if (frameType.SequenceEqual(FRAME_TYPE_LEDS) && frame.Length >= 8)
            {
                var states = (State)frame[..4].FromBytes(ByteOrder.LittleEndian);
                var flashingStates = (State)frame[4..8].FromBytes(ByteOrder.LittleEndian);

                states |= flashingStates;
                if (HeaterAutoMode)
                    states |= State.HEATER_AUTO_MODE;

                // Left this one to avoid dual callbacks
                if (states != _poolStates || flashingStates != _flashingStates)
                {
                    if (states != _poolStates)
                        AddVariance(nameof(PoolStates), _poolStates, states);

                    if (flashingStates != _flashingStates)
                        AddVariance(nameof(FlashingStates), _flashingStates, flashingStates);

                    _poolStates = states;
                    _flashingStates = flashingStates;
                    CallbackAndClearVariances();
                }
            }
            else if (frameType.SequenceEqual(FRAME_TYPE_PUMP_SPEED_REQUEST) && frame.Length >= 2)
            {
                var value = frame[..2].FromBytes(ByteOrder.BigEndian);
                _logger.Debug("Pump speed request: {Value}", value);

                PumpSpeed = CompareAndCallback(nameof(PumpSpeed), PumpSpeed, value);
            }
            else if (frameType.SequenceEqual(FRAME_TYPE_PUMP_STATUS) && frame.Length >= 5)
            {
                MultiSpeedPump = true;
                var speed = frame[2];
                var power = ((((frame[3] & 0xf0) >> 4) * 1000) +
                     (((frame[3] & 0x0f)) * 100) +
                     (((frame[4] & 0xf0) >> 4) * 10) +
                     (((frame[4] & 0x0f))));

                _logger.Debug("Pump speed: {Speed}, power: {Power} watts", speed, power);
                PumpPower = CompareAndCallback(nameof(PumpPower), PumpPower, power);
            }
            else if (frameType.SequenceEqual(FRAME_TYPE_DISPLAY_UPDATE))
            {
                // The degree glyph (0xDF) is translated by the display parser itself, after
                // it has cleared the blink bit. Rewriting it to UTF-8 here would get mangled.
                Display.Parse(frame);
                var parts = Display.DisplaySections.SelectMany(d => d.Select(c => c.Content)).ToArray();

                // If we do not have pool on and theres a temp, nuke it
                if (!_poolStates.HasFlag(State.POOL) && PoolTemp.HasValue)
                {
                    AddVariance(nameof(PoolTemp), PoolTemp, null);
                    PoolTemp = null;

                    AddVariance(nameof(PoolChlorinatorPercent), PoolChlorinatorPercent, null);
                    PoolChlorinatorPercent = null;
                    CallbackAndClearVariances();
                }
                    
                // If we do not have spa on and theres a temp, nuke it
                if (!_poolStates.HasFlag(State.SPA) && SpaTemp.HasValue)
                {
                    AddVariance(nameof(SpaTemp), SpaTemp, null);
                    SpaTemp = null;
                    
                    AddVariance(nameof(SpaChlorinatorPercent), SpaChlorinatorPercent, null);
                    SpaChlorinatorPercent = null;
                    CallbackAndClearVariances();
                }
                
                try
                {
                    // parts comes straight off the wire, so every index below is length checked
                    // rather than relying on the catch to swallow a malformed display.
                    if (parts.Length >= 3)
                    {
                        switch (parts[1])
                        {
                            case "Temp" when parts[2].Length >= 2 && int.TryParse(parts[2][..^2], out var temp):
                            {
                                switch (parts[0])
                                {
                                    case "Pool": PoolTemp = CompareAndCallback(nameof(PoolTemp), PoolTemp, temp);
                                        break;
                                    case "Spa": SpaTemp = CompareAndCallback(nameof(SpaTemp), SpaTemp, temp);
                                        break;
                                    case "Air": AirTemp = CompareAndCallback(nameof(AirTemp), AirTemp, temp);
                                        break;
                                }

                                IsMetric = parts[2][^1] == 'C';

                                break;
                            }
                            case "Chlorinator" when parts[2].Length >= 1 && int.TryParse(parts[2][..^1], out var percent):
                            {
                                switch (parts[0])
                                {
                                    case "Pool": PoolChlorinatorPercent = CompareAndCallback(nameof(PoolChlorinatorPercent), PoolChlorinatorPercent, percent);
                                        break;
                                    case "Spa": SpaChlorinatorPercent = CompareAndCallback(nameof(SpaChlorinatorPercent), SpaChlorinatorPercent, percent);
                                        break;
                                }

                                break;
                            }
                            case "Level" when parts[0] == "Salt" && float.TryParse(parts[2], out var salt):
                            {
                                SaltLevel = CompareAndCallback(nameof(SaltLevel), SaltLevel, Math.Round(salt, 1));

                                if (parts.Length >= 4)
                                    IsMetric = parts[3] == "g/L";

                                break;
                            }
                            case "System" when parts[0] == "Check":
                            {
                                var message = string.Join(" ", parts[2..]);
                                CheckSystemMessage = CompareAndCallback(nameof(CheckSystemMessage), CheckSystemMessage, message);
                                break;
                            }
                        }
                    }

                    if (parts.Length >= 2 && parts[0] == "Heater1")
                    {
                        HeaterAutoMode = parts[1] == "Auto";
                    }

                    // Assign rather than latch, otherwise it never clears once seen
                    MenuLocked = parts.Contains("Menu Locked");

                    if (Display.DisplayChanged)
                    {
                        _logger.Debug("Display update: {Parts}", parts);

                        DisplayUpdated();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("{Message}", ex.Message);
                }
            }
            else if (frameType.SequenceEqual(FRAME_TYPE_LONG_DISPLAY_UPDATE))
            {
                //Does nothing at the moment
            }
            else
            {
                //_logger.Debug("Unknown frame: {Type}, {Frame}", frameType.Hexlify(), frame.Hexlify());
            }
        }


        private T CompareAndCallback<T>(string name, T? a, T b)
        {
            if (a?.Equals(b) ?? false)
                return a;
            
            AddVariance(name, a,b);
            CallbackAndClearVariances();
            return b;
        }

        private void DisplayUpdated()
        {
            Variances.Add(new Variance { Prop = "Display" });
            CallbackAndClearVariances();
        }

        private void AddVariance(string name, object? before, object? after)
        {
            Variances.Add(new Variance 
            {
                Prop = name,
                Before = before,
                After = after
            });
        }

        private void CallbackAndClearVariances()
        {
            Callback(this);
            
            JsonSerializerOptions options = new()
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            
            _logger.Debug("{Variances}", JsonSerializer.Serialize(Variances, options));
            Variances.Clear();
        }
        
        private static void AppendData(ICollection<byte> frame, IEnumerable<byte> data)
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

            // if ((int)key > 0xFFFF)
            // {
                AppendData(frame, FRAME_TYPE_WIRELESS_KEY_EVENT); //0x00 0x83 (0x8C for my system)
                AppendData(frame, new byte[] { 0x01 });
                AppendData(frame, ((int)key).ToBytes(4, ByteOrder.LittleEndian));
                AppendData(frame, ((int)key).ToBytes(4, ByteOrder.LittleEndian));
                AppendData(frame, new byte[] { 0x00 });
            // }
            // else
            // {
            //     AppendData(frame, FRAME_TYPE_LOCAL_WIRED_KEY_EVENT); //0x00 0x02
            //     AppendData(frame, ((int)key).ToBytes(2, ByteOrder.LittleEndian));
            //     AppendData(frame, ((int)key).ToBytes(2, ByteOrder.LittleEndian));
            // }

            var crc = frame.Aggregate(0, (current, frameByte) => current + frameByte);
            
            AppendData(frame, crc.ToBytes(2, ByteOrder.BigEndian));

            frame.Add(FRAME_DLE); //0x10
            frame.Add(FRAME_ETX); //0x03
            
            return frame;
        }

        public void SendKey(Key key)
        {
            _logger.Information("Queueing Key: {Key}", key);
            var frame = GetKeyEventFrame(key);
            
            SendQueue.Enqueue(new AquaLogicQueueInfo { Frame = frame.ToArray() });
        }

        public List<State> GetStates()
        {
            var stateList = Enums.GetValues<State>().Where(value => _poolStates.HasFlag(value)).ToList();

            if (_flashingStates.HasFlag(State.FILTER))
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
                    if (desiredState.State.Is(state))
                        return desiredState.Enabled;
                }
            }

            return state.Is(State.FILTER_LOW_SPEED) ? _flashingStates.HasFlag(State.FILTER) : _poolStates.HasFlag(state);
        }

        public bool SetState(State state, bool enable)
        {
            var isStateEnabled = GetState(state);
            if (enable == isStateEnabled)
                return true;

            Key key;
            var desiredStates = new List<DesiredState>();

            if (state.HasFlag(State.HEATER_AUTO_MODE))
            {
                key = Key.HEATER_1;
                desiredStates.Add(new DesiredState
                {
                    State = State.HEATER_AUTO_MODE,
                    Enabled = !HeaterAutoMode
                });
            }
            // Either bit, not both - HasFlag on a composite requires every bit to be present,
            // which is never true for a single POOL or SPA state.
            else if ((state & (State.POOL | State.SPA)) != 0)
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
                var result = Enums.TryParse(state.GetName(), out key);

                if (!result)
                    return false;
                
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

        private void AlertAttemptingRequest(bool isAttempting)
        {
            _logger.Information("IsAttempting Request: {IsAttempting}", isAttempting);
            AttemptingRequest = isAttempting;
            Callback(this);
        }

        public bool EnableMultiSpeedPump(bool enable)
        {
            MultiSpeedPump = enable;
            return true;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _timer, null)?.Dispose();
            _logger.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}