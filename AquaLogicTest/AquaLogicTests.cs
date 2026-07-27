using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AquaLogicSharp;
using AquaLogicSharp.Implementation;
using AquaLogicSharp.Models;

namespace AquaLogicTest
{
    public class AquaLogicTests
    {
        [Fact]
        public async Task TestPool()
        {
            var aquaLogic = new AquaLogic();
            var dataSource = new FileDataSource("TestFiles/pool_on.bin");
            await aquaLogic.Connect(dataSource);
            await aquaLogic.Process(DataChanged);

            Assert.True(aquaLogic.IsMetric);
            Assert.Equal(-6, aquaLogic.AirTemp);
            Assert.Equal(-7, aquaLogic.PoolTemp);
            Assert.Null( aquaLogic.SpaTemp);
            Assert.Null(aquaLogic.PoolChlorinatorPercent);
            Assert.Equal(3, aquaLogic.SpaChlorinatorPercent);
            Assert.Equal(3.1, aquaLogic.SaltLevel);
            Assert.True(aquaLogic.GetState(State.POOL));
            Assert.True(aquaLogic.GetState(State.FILTER));
            Assert.False(aquaLogic.GetState(State.SPA));

            dataSource.Disconnect();
        }

        [Fact]
        public async Task TestSpa()
        {
            var aquaLogic = new AquaLogic();
            var dataSource = new FileDataSource("TestFiles/spa_on.bin");
            await aquaLogic.Connect(dataSource);
            await aquaLogic.Process(DataChanged);

            Assert.True(aquaLogic.IsMetric);
            Assert.Equal(-6, aquaLogic.AirTemp);
            Assert.Null(aquaLogic.PoolTemp);
            Assert.Equal(-7, aquaLogic.SpaTemp);
            Assert.Null(aquaLogic.PoolChlorinatorPercent);
            Assert.Equal(3, aquaLogic.SpaChlorinatorPercent);
            Assert.Equal(3.1, aquaLogic.SaltLevel);
            Assert.False(aquaLogic.GetState(State.POOL));
            Assert.True(aquaLogic.GetState(State.FILTER));
            Assert.True(aquaLogic.GetState(State.SPA));

            dataSource.Disconnect();
        }

        [Theory]
        [InlineData(Key.RIGHT,    "01-00-00-00",    "00-A1")]
        [InlineData(Key.MENU,     "02-00-00-00",    "00-A3")]
        [InlineData(Key.LEFT,     "04-00-00-00",    "00-A7")]
        [InlineData(Key.LEFTRIGHT,"05-00-00-00",    "00-A9")]
        [InlineData(Key.SERVICE,  "08-00-00-00",    "00-AF")]
        [InlineData(Key.MINUS,    "10-00-00-00-00", "00-BF")]
        [InlineData(Key.PLUS,     "20-00-00-00",    "00-DF")]
        [InlineData(Key.POOL_SPA, "40-00-00-00",    "01-1F")]
        [InlineData(Key.FILTER,   "80-00-00-00",    "01-9F")]
        [InlineData(Key.LIGHTS,   "00-01-00-00",    "00-A1")]
        [InlineData(Key.AUX_1,    "00-02-00-00",    "00-A3")]
        [InlineData(Key.AUX_2,    "00-04-00-00",    "00-A7")]
        [InlineData(Key.AUX_3,    "00-08-00-00",    "00-AF")]
        [InlineData(Key.AUX_4,    "00-10-00-00-00", "00-BF")]
        [InlineData(Key.AUX_5,    "00-20-00-00",    "00-DF")]
        [InlineData(Key.AUX_6,    "00-40-00-00",    "01-1F")]
        [InlineData(Key.AUX_7,    "00-80-00-00",    "01-9F")]
        [InlineData(Key.VALVE_3,  "00-00-01-00",    "00-A1")]
        [InlineData(Key.VALVE_4,  "00-00-02-00",    "00-A3")]
        [InlineData(Key.HEATER_1, "00-00-04-00",    "00-A7")]
        [InlineData(Key.AUX_8,    "00-00-08-00",    "00-AF")]
        [InlineData(Key.AUX_9,    "00-00-10-00-00", "00-BF")]
        [InlineData(Key.AUX_10,   "00-00-20-00",    "00-DF")]
        [InlineData(Key.AUX_11,   "00-00-40-00",    "01-1F")]
        [InlineData(Key.AUX_12,   "00-00-80-00",    "01-9F")]
        [InlineData(Key.AUX_13,   "00-00-00-01",    "00-A1")]
        [InlineData(Key.AUX_14,   "00-00-00-02",    "00-A3")]
        [InlineData(Key.SUPER_CHLORINATE, "00-00-00-04", "00-A7")]
        public void TestKeyEventFrame(Key key, string data, string crc)
        {
            const string beginningSequence = "10-02-00-8C-01";
            const string endSequence = "10-03";
            var aquaLogic = new AquaLogic();
            var actualHexSequence = aquaLogic.GetKeyEventFrame(key).ToArray().Hexlify();
            Assert.Equal($"{beginningSequence}-{data}-{data}-00-{crc}-{endSequence}", actualHexSequence);
        }

        [Theory]
        [InlineData(new byte[] {32,32,65,105,114,32,84,101,109,112,32,32,32,56,52,95,70,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,32,0 })]
        public void TestDisplayReader(byte[] bytes)
        {
            var display = new Display();
            
            display.Parse(bytes);

            var expected = new List<List<DisplaySection>>
            {
                new ()
                {
                    new DisplaySection
                    {
                        Content = "Air",
                        Blinking = false,
                    },
                    new DisplaySection
                    {
                        Content = "Temp",
                        Blinking = false
                    }
                },
                new ()
                {
                    new DisplaySection
                    {
                        Content = "84_F",
                        Blinking = false
                    }
                }
            };
            
            Assert.Equal(expected, display.DisplaySections);
        }

        [Theory] 
        [InlineData(new byte[] { 32, 32, 32, 32, 32, 32, 84, 117, 101, 115, 100, 97, 121, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 49, 48, 58, 48, 52, 80, 32, 32, 32, 32, 32, 32, 32, 0 })]
        public void TestBlinkingTime(byte[] bytes)
        {
            //Tuesday 10:04P
            var display = new Display();
            
            display.Parse(bytes);

            var expected = new List<List<DisplaySection>>
            {
                new ()
                {
                    new DisplaySection
                    {
                        Content = "Tuesday",
                        Blinking = false,
                    }
                },
                new ()
                {
                    new DisplaySection
                    {
                        Content = "10",
                        Blinking = false
                    },
                    new DisplaySection
                    {
                        Content = ":",
                        Blinking = true
                    },
                    new DisplaySection
                    {
                        Content = "04P",
                        Blinking = false
                    }
                }
            };
            
            Assert.Equal(expected, display.DisplaySections);
        }
        
        [Theory]
        // Four byte spans must decode all 32 bits, not just the low 16
        [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x02 }, ByteOrder.LittleEndian, 1 << 25)] // SUPER_CHLORINATE
        [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00 }, ByteOrder.LittleEndian, 1 << 16)] // SYSTEM_OFF
        // Bit 15 must not sign extend across the upper half
        [InlineData(new byte[] { 0x00, 0x80, 0x00, 0x00 }, ByteOrder.LittleEndian, 1 << 15)] // SPILLOVER
        [InlineData(new byte[] { 0x08, 0x00, 0x00, 0x00 }, ByteOrder.LittleEndian, 1 << 3)]  // POOL
        [InlineData(new byte[] { 0x01, 0x9F }, ByteOrder.BigEndian, 0x019F)]
        [InlineData(new byte[] { 0xFF, 0xFF }, ByteOrder.BigEndian, 0xFFFF)]
        public void TestFromBytesDecodesFullWidth(byte[] bytes, ByteOrder byteOrder, int expected)
        {
            Assert.Equal(expected, bytes.FromBytes(byteOrder));
        }

        [Fact]
        public void TestSpilloverDoesNotBleedIntoOtherStates()
        {
            // 0x8000 read as a signed 16 bit value used to sign extend and light up every
            // state above it, including the HEATER_AUTO_MODE and FILTER_LOW_SPEED kludges.
            var states = (State)new byte[] { 0x00, 0x80, 0x00, 0x00 }.FromBytes(ByteOrder.LittleEndian);

            Assert.True(states.HasFlag(State.SPILLOVER));
            Assert.False(states.HasFlag(State.SYSTEM_OFF));
            Assert.False(states.HasFlag(State.SUPER_CHLORINATE));
            Assert.False(states.HasFlag(State.HEATER_AUTO_MODE));
            Assert.False(states.HasFlag(State.FILTER_LOW_SPEED));
        }

        [Theory]
        [InlineData(State.POOL)]
        [InlineData(State.SPA)]
        [InlineData(State.LIGHTS)]
        [InlineData(State.AUX_1)]
        public void TestSetStateQueuesRequest(State state)
        {
            using var aquaLogic = new AquaLogic();

            Assert.True(aquaLogic.SetState(state, true));
            // A queued request reports its desired value until the panel confirms it
            Assert.True(aquaLogic.GetState(state));
        }

        [Theory]
        [InlineData("  Air Temp   84°F", "84°F")]
        [InlineData("  Pool Temp   9°C", "9°C")]
        public void TestDegreeSymbolIsNotMangled(string text, string expected)
        {
            var display = new Display();
            display.Parse(DisplayFrame(text));

            var parts = display.DisplaySections.SelectMany(section => section).ToList();

            Assert.Contains(parts, section => section.Content == expected);
            // 0xDF has the high bit set but is a degree glyph, not a blinking character
            Assert.All(parts, section => Assert.False(section.Blinking));
        }

        [Fact]
        public void TestSingleCharacterChangeIsNotSuppressed()
        {
            var display = new Display();

            display.Parse(DisplayFrame("  Air Temp   84°F"));
            Assert.True(display.DisplayChanged);

            // One byte differs, which the old diff treated as noise and dropped
            display.Parse(DisplayFrame("  Air Temp   85°F"));
            Assert.True(display.DisplayChanged);

            var parts = display.DisplaySections.SelectMany(section => section).ToList();
            Assert.Contains(parts, section => section.Content == "85°F");
        }

        [Fact]
        public void TestBlinkingColonIsSuppressed()
        {
            var display = new Display();

            display.Parse(DisplayFrame("      Tuesday             10:04P"));
            Assert.True(display.DisplayChanged);

            // The colon blanking out is the clock blinking, not a new reading
            display.Parse(DisplayFrame("      Tuesday             10 04P"));
            Assert.False(display.DisplayChanged);
        }

        [Fact]
        public void TestBlinkBitToggleIsSuppressed()
        {
            var display = new Display();

            var frame = DisplayFrame("  Air Temp   84°F");
            display.Parse(frame);
            Assert.True(display.DisplayChanged);

            var blinking = DisplayFrame("  Air Temp   84°F");
            blinking[13] |= 1 << 7; // same glyph, blink bit set
            blinking[14] |= 1 << 7;

            display.Parse(blinking);
            Assert.False(display.DisplayChanged);
        }

        /// <summary>
        /// Builds a 40 character display frame padded with spaces and null terminated,
        /// mapping '°' onto the LCD's 0xDF glyph.
        /// </summary>
        private static byte[] DisplayFrame(string text)
        {
            const int frameLength = 40;
            var bytes = new byte[frameLength + 1];

            for (var i = 0; i < frameLength; i++)
                bytes[i] = (byte)' ';

            for (var i = 0; i < text.Length && i < frameLength; i++)
                bytes[i] = text[i] == '°' ? (byte)0xDF : (byte)text[i];

            bytes[frameLength] = 0x0;
            return bytes;
        }

        private static void DataChanged(AquaLogic aquaLogic)
        {

        }
    }
}
