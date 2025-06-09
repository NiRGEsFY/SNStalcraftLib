namespace SNStalcraftRequestLib.Interfaces
{
    public interface IToken
    {
        public string AccessToken { get; set; }
        public DateTime TokenResetTime { get; set; }
        public DateTime TokenExpireTime { get; set; }
        public int TokenLimit { get; set; }
        public string TokenType { get; set; }
        public bool IsTaked { get; set; }
        public int MaxTokenLimit { get; set; }
    }
}
