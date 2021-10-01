namespace AquaLogicSharp
{
    public class AquaLogicQueueInfo
    {
        public State? State { get; set; }
        public DesiredState[]? DesiredStates { get; set; }
        public bool? Enabled { get; set; }
        public byte[]? Frame { get; set; }
        public int? Retries { get; set; }
    }
}
