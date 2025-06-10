using SNStalcraftRequestLib.Interfaces;
using SNStalcraftRequestLib.Objects.Application;
using SNStalcraftRequestLib.Objects.User;

namespace SNStalcraftRequestLib
{
    public class TokenHandler
    {
        private object locker;
        public List<IToken> _tokens { get; private set; }
        private TimerCallback resetLimitCallback;
        private Timer resetLimitTimer;
        
        public TokenHandler() 
        {
            locker = new object();
            _tokens = new List<IToken>();
            resetLimitCallback = new TimerCallback(UpdateTokensLimits);
            resetLimitTimer = new Timer(resetLimitCallback,null,TimeSpan.FromSeconds(10),TimeSpan.FromSeconds(10));
        }
        public TokenHandler(TimeSpan reseterObserverPeriod)
        {
            locker = new object();
            _tokens = new List<IToken>();
            resetLimitCallback = new TimerCallback(UpdateTokensLimits);
            resetLimitTimer = new Timer(resetLimitCallback, null, reseterObserverPeriod, reseterObserverPeriod);
        }
        public TokenHandler(List<IToken> tokens, TimeSpan reseterObserverPeriod)
        {
            locker = new object();
            _tokens = tokens;
            resetLimitCallback = new TimerCallback(UpdateTokensLimits);
            resetLimitTimer = new Timer(resetLimitCallback, null, reseterObserverPeriod, reseterObserverPeriod);
        }
        public void UpdateTokensLimits(object? obj)
        {
            List<IToken> updatedTokens = new List<IToken>();
            lock (locker)
            {
                var tokens = _tokens.Where(x => x.TokenResetTime <= DateTime.UtcNow);
                foreach(var token in tokens)
                {
                    token.TokenLimit = token.MaxTokenLimit;
                    updatedTokens.Add(token);
                    token.TokenResetTime = token.TokenResetTime.AddMinutes(1);
                }
            }
            UpdatedTokenLimitNotify?.Invoke(updatedTokens);
        }
        public IToken? Take(int requestsWight = 2, bool longTake = false)
        {
            var token = _tokens.Where(x => x.TokenLimit >= requestsWight && !x.IsTaked).FirstOrDefault();
            if (token is null)
                return null;
            if (longTake)
            {
                token.IsTaked = longTake;
                return token;
            }
            
            token.TokenLimit -= requestsWight;
            return token;
        }
        /// <summary>
        /// !!!No save method!!! Take all tokens in
        /// </summary>
        /// <returns></returns>
        public List<IToken> GetTokens() =>
            _tokens;
        /// <summary>
        /// !!!No save method!!! Take all tokens in with func
        /// </summary>
        /// <returns></returns>
        public IEnumerable<IToken> GetTokens(Func<IToken, bool> func) =>
            _tokens.Where(func);
        public async Task<IToken> TakeAsync(int requestsWight = 2, bool longTake = false)
        {
            IToken? token = _tokens.FirstOrDefault(x => x.TokenLimit >= requestsWight);
            if (token is null)
                throw new Exception("Token not exist in storage");
            while (token.IsTaked)
            {
                await Task.Delay(100);
            }
            if (longTake)
            {
                token.IsTaked = longTake;
                return token;
            }

            token.TokenLimit -= requestsWight;
            return token;
        }
        public IToken? TakeUserToken(int userId, bool longTake = false)
        {
            var token = _tokens.Where(x => x is UserToken && (x as UserToken).UserId == userId).FirstOrDefault();
            if (token is null)
                return null;
            if (longTake)
                token.IsTaked = longTake;

            return token;
        }
        public void Add(IToken token)
        {
            lock (locker)
            {
                _tokens.Add(token);
            }
        }
        public void AddRange(IEnumerable<IToken> tokens)
        {
            lock(locker)
            {
                _tokens.AddRange(tokens);
            }
        }
        public void Remove(IToken token)
        {
            lock (locker)
            {
                _tokens.Remove(token);
            }
        }
        public void RemoveRange(IEnumerable<IToken> tokens)
        {
            lock (locker)
            {
                foreach(var item in tokens)
                    _tokens.Remove(item);
            }
        }
        public RequesterStatus HandlerStatus()
        {
            RequesterStatus status = new RequesterStatus();
            List<IToken> tokensSceen = new List<IToken>();
            status.CountToken = tokensSceen.Count;
            status.CountFreeToken = tokensSceen.Where(x => x.TokenLimit >= 0 && !x.IsTaked).Count();
            status.SumTokenLimit = tokensSceen.Where(x => !x.IsTaked).Sum(x => x.TokenLimit);
            return status;
        }
        public delegate void UpdatedTokens(IEnumerable<IToken> updatedTokens);
        public event UpdatedTokens UpdatedTokenLimitNotify;
    }
}
