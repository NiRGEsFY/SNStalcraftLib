using SNStalcraftRequestLib.DtoObjects.Application;
using SNStalcraftRequestLib.Interfaces;

namespace SNStalcraftRequestLib.Objects.Application
{
    public class ApplicationToken : IToken
    {
        public ApplicationToken(int tokenId, string tokenSecret) 
        {
            TokenId = tokenId;
            TokenSecret = tokenSecret;
        }
        public ApplicationToken(ExboTokenDto token, int tokenId, string tokenSecret)
            : this(tokenId, tokenSecret)
        {
            ExpiresIn = long.Parse(token.expires_in);
            TokenType = token.token_type;
            AccessToken = token.access_token;
        }
        public bool IsTaked { get; set; }
        public long ExpiresIn { get; set; }
        public string TokenType { get; set; }
        public string AccessToken { get; set; }
        public int TokenId { get; set; }
        public string TokenSecret { get; set; }
        public int TokenLimit { get; set; }
        public int MaxTokenLimit { get; set; }
        public DateTime TokenResetTime { get; set; }
        public DateTime TokenExpireTime { get; set; }
    }
}
