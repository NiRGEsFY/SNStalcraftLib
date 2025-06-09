using SNStalcraftRequestLib.DtoObjects.Application;
using SNStalcraftRequestLib.Interfaces;

namespace SNStalcraftRequestLib.Objects.User
{
    public class UserToken : IToken
    {
        public UserToken() 
        {

        }
        public UserToken(ExboTokenDto token) 
        {
            ExpiresIn = long.Parse(token.expires_in);
            TokenType = token.token_type;
            AccessToken = token.access_token;
            RefreshToken = token.refresh_token;
            TokenLimit = 4;
        }
        public bool IsTaked { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int TokenLimit { get; set; }
        public int MaxTokenLimit { get; set; }
        public int TokenOwnerId { get; set; }
        public long UserId { get; set; }
        public string TokenType { get; set; }
        public long ExpiresIn { get; set; }
        public DateTime TokenResetTime { get; set; }
        public DateTime TokenExpireTime { get; set; }
    }
}
