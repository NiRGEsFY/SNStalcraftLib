using SNStalcraftRequestLib.Interfaces;

namespace SNStalcraftRequestLib.Objects.User
{
    public class UserToken : IToken
    {
        public bool IsTaked { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int TokenLimit { get; set; }
        public DateTime TokenResetTime { get; set; }
        public string TokenType { get; set; }
        public long ExpiresIn { get; set; }
    }
}
