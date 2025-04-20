using SNStalcraftRequestLib.Interfaces;
using SNStalcraftRequestLib.Objects.Application;

namespace SNStalcraftRequestLib
{
    public class TokenHandler
    {
        private static object? locker;
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
                var tokens = _tokens.Where(x => x.TokenResetTime <= DateTime.Now);
                foreach(var token in tokens)
                {
                    if (token is ApplicationToken)
                        token.TokenLimit = 400;
                    else
                        token.TokenLimit = 30;
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
            token.IsTaked = longTake;
            if(!longTake)
                token.TokenLimit -= requestsWight;
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

        public delegate void UpdatedTokens(IEnumerable<IToken> updatedTokens);
        public event UpdatedTokens UpdatedTokenLimitNotify;
    }
}
