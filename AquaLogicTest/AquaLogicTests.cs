using Xunit;
using AquaLogicSharp;
using AquaLogicSharp.Models;

namespace AquaLogicTest
{
    public class AquaLogicTests
    {
        [Fact]
        public void TestPool()
        {
            var aquaLogic = new AquaLogic();
            aquaLogic.ConnectFile("TestFiles/pool_on.bin");
            aquaLogic.Process(DataChanged);

            Assert.True(aquaLogic.IsMetric);
            Assert.Equal(-6, aquaLogic.AirTemp);
            Assert.Equal(-7, aquaLogic.PoolTemp);
            Assert.Equal(0, aquaLogic.SpaTemp);
            Assert.Equal(0, aquaLogic.PoolChlorinatorPercent);
            Assert.Equal(3, aquaLogic.SpaChlorinatorPercent);
            Assert.Equal(3.1, aquaLogic.SaltLevel);
            Assert.True(aquaLogic.GetState(State.POOL));
            Assert.True(aquaLogic.GetState(State.FILTER));
            Assert.False(aquaLogic.GetState(State.SPA));

            aquaLogic.DisconnectFile();
        }

        [Fact]
        public void TestSpa()
        {
            var aquaLogic = new AquaLogic();
            aquaLogic.ConnectFile("TestFiles/spa_on.bin");
            aquaLogic.Process(DataChanged);

            Assert.True(aquaLogic.IsMetric);
            Assert.Equal(-6, aquaLogic.AirTemp);
            Assert.Equal(0, aquaLogic.PoolTemp);
            Assert.Equal(-7, aquaLogic.SpaTemp);
            Assert.Equal(0, aquaLogic.PoolChlorinatorPercent);
            Assert.Equal(3, aquaLogic.SpaChlorinatorPercent);
            Assert.Equal(3.1, aquaLogic.SaltLevel);
            Assert.False(aquaLogic.GetState(State.POOL));
            Assert.True(aquaLogic.GetState(State.FILTER));
            Assert.True(aquaLogic.GetState(State.SPA));

            aquaLogic.DisconnectFile();
        }

        
        public void DataChanged(AquaLogic aquaLogic)
        {

        }
    }
}
