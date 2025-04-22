namespace SNStalcraftRequestLib
{
    public class RequesterStatus
    {
        public int CountToken { get; set; }
        public int CountFreeToken { get; set; }
        public int SumTokenLimit { get; set; }

        public int RequestInTask { get; set; }
        public int RequestInProgress { get; set; }
        public int RequestComplete { get; set; }
    }
}
