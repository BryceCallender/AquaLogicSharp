using System.Collections.Generic;
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
        
        private static void DataChanged(AquaLogic aquaLogic)
        {

        }
    }
}
