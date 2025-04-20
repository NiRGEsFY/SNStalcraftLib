using SNStalcraftRequestLib.DtoObjects.Application;
using SNStalcraftRequestLib.Interfaces;

namespace SNStalcraftRequestLib.Objects.Application
{
    public class ApplicationToken : IToken
    {
        public ApplicationToken(ExboTokenDto token)
        {
            ExpiresIn = long.Parse(token.expires_in);
            TokenType = token.token_type;
            AccessToken = token.access_token;
        }
        public bool IsTaked { get; set; }
        public long ExpiresIn { get; set; }
        public string TokenType { get; set; }
        public string AccessToken { get; set; }
        public int TokenLimit { get; set; }
        public DateTime TokenResetTime { get; set; }
    }
}
