using Newtonsoft.Json;
using SNStalcraftRequestLib.DtoObjects.Application;
using SNStalcraftRequestLib.DtoObjects.Auction;
using SNStalcraftRequestLib.Interfaces;
using SNStalcraftRequestLib.Objects.Application;
using SNStalcraftRequestLib.Objects.Auction;
using SNStalcraftRequestLib.Objects.Comparers;
using SNStalcraftRequestLib.Objects.User;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;

namespace SNStalcraftRequestLib
{
    public class StalcraftMultipleRequester
    {
        private TokenHandler _TokenHandler;
        public const string _exboUrl = "https://exbo.net/";
        public const string _stalcraftUrl = "https://eapi.stalcraft.net/";
        public const int _weightOneRequest = 2;
        public const int _requestLotsLimit = 200;
        private readonly ConcurrentDictionary<IToken, ManualResetEventSlim> _tokensLimitUpdateEventDict = new ConcurrentDictionary<IToken, ManualResetEventSlim>();
        public StalcraftMultipleRequester(ApplicationToken appToken)
        {
            TimeSpan updateTokensTimer = TimeSpan.FromSeconds(10);
            _TokenHandler = new TokenHandler(updateTokensTimer);
            AddToken(ApplicationAuthAsync(appToken).GetAwaiter().GetResult());
            _TokenHandler.UpdatedTokenLimitNotify += OnTokenLimitUpdated;
        }
        public StalcraftMultipleRequester(ApplicationToken appToken, TimeSpan updateTokensTimer)
        {
            _TokenHandler = new TokenHandler(updateTokensTimer);
            AddToken(ApplicationAuthAsync(appToken).GetAwaiter().GetResult());
            _TokenHandler.UpdatedTokenLimitNotify += OnTokenLimitUpdated;
        }
        public StalcraftMultipleRequester(ApplicationToken appToken, IEnumerable<UserToken> userTokens)
        {
            TimeSpan updateTokensTimer = TimeSpan.FromSeconds(10);
            _TokenHandler = new TokenHandler(updateTokensTimer);
            AddToken(ApplicationAuthAsync(appToken).GetAwaiter().GetResult());
            _TokenHandler.UpdatedTokenLimitNotify += OnTokenLimitUpdated;
        }
        public StalcraftMultipleRequester(ApplicationToken appToken, IEnumerable<UserToken> userTokens, TimeSpan updateTokensTimer)
        {
            _TokenHandler = new TokenHandler(updateTokensTimer);
            AddToken(ApplicationAuthAsync(appToken).GetAwaiter().GetResult());
            _TokenHandler.UpdatedTokenLimitNotify += OnTokenLimitUpdated;
        }
        
        private static readonly SocketsHttpHandler _httpHandler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 1000,
            ConnectTimeout = TimeSpan.FromMinutes(2),
        };


        #region Auth methods

        private async Task<ApplicationToken> ApplicationAuthAsync(ApplicationToken token)
        {
            using HttpClient client = new HttpClient(_httpHandler);
            string url = _exboUrl + "oauth/token";
            var requestData = new
            {
                client_id = token.TokenId,
                client_secret = token.TokenSecret,
                grant_type = "client_credentials"
            };
            string jsonData = JsonConvert.SerializeObject(requestData);
            StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(url, content);

            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            ExboTokenDto tokenDto = JsonConvert.DeserializeObject<ExboTokenDto>(responseBody)
                ?? throw new InvalidOperationException("Failed to deserialize token response");
            return new ApplicationToken(tokenDto, token.TokenId, token.TokenSecret);
        }
        private async Task<UserToken> RefreshUserToken(UserToken userToken, ApplicationToken appToken)
        {
            using HttpClient client = new HttpClient(_httpHandler);
            string url = _exboUrl + "oauth/token";
            var requestData = new
            {
                client_id = appToken.TokenId,
                client_secret = appToken.TokenSecret,
                grant_type = "refresh_token",
                refresh_token = userToken.RefreshToken
            };

            string jsonData = JsonConvert.SerializeObject(requestData);
            StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(url, content);

            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            ExboTokenDto tokenDto = JsonConvert.DeserializeObject<ExboTokenDto>(responseBody)
                ?? throw new InvalidOperationException("Failed to deserialize token response");

            var newToken = new UserToken(tokenDto)
            {
                TokenOwnerId = appToken.TokenId
            };

            return new UserToken(tokenDto);
        }
        private async Task<UserToken> AuthUserTokenAsync(string accessToken, ApplicationToken appToken, string url)
        {
            using HttpClient client = new HttpClient(_httpHandler);

            var requestData = new
            {
                client_id = appToken.TokenId,
                client_secret = appToken.TokenSecret,
                grant_type = "authorization_code",
                code = accessToken,
                redirect_uri = url
            };

            string jsonData = JsonConvert.SerializeObject(requestData);
            StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(url, content);

            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            ExboTokenDto tokenDto = JsonConvert.DeserializeObject<ExboTokenDto>(responseBody)
                ?? throw new InvalidOperationException("Failed to deserialize token response");

            var newToken = new UserToken(tokenDto)
            {
                TokenOwnerId = appToken.TokenId
            };

            return new UserToken(tokenDto);
        }

        #endregion

        #region Work with tokens

        /// <summary>
        /// Addition new token to handler
        /// </summary>
        /// <param name="token"></param>
        public void AddToken(IToken token)
        {
            var decoded = new JwtSecurityToken(token.AccessToken);
            if (token is UserToken)
            {
                (token as UserToken).TokenOwnerId = int.Parse(decoded.Audiences.FirstOrDefault());
                (token as UserToken).UserId = int.Parse(decoded.Claims.FirstOrDefault(x => x.Type.ToString() == "sub").Value);
            }

            token.TokenExpireTime = decoded.ValidTo;

            _TokenHandler.Add(token);
        }
        /// <summary>
        /// Remove token from handler
        /// </summary>
        /// <param name="token"></param>
        public void RemoveToken(IToken token) =>
            _TokenHandler.Remove(token);
        /// <summary>
        /// Take token with possibillity private this token in handler
        /// </summary>
        /// <param name="weightOperation">How much request actually token do x2</param>
        /// <param name="longTake">Freeze token in handler</param>
        /// <returns></returns>
        public IToken? TakeToken(int weightOperation, bool longTake) =>
            _TokenHandler.Take(weightOperation, longTake);
        /// <summary>
        /// Take token with possibillity private this token in handler async
        /// </summary>
        /// <param name="weightOperation">How much request actually token do x2</param>
        /// <param name="longTake">Freeze token in handler</param>
        /// <returns></returns>
        public async Task<IToken> TakeTokenAsync(int weightOperation, bool longTake) =>
            await _TokenHandler.TakeAsync(weightOperation, longTake);
        /// <summary>
        /// !!!No safety method (you dont freeze they)!!! You take all tokens in with func
        /// </summary>
        /// <param name="func"></param>
        /// <returns></returns>
        public IEnumerable<IToken> GetToken(Func<IToken, bool> func) =>
            _TokenHandler.GetTokens(func);

        #endregion

        #region Auction
        /// <summary>
        /// Takedown items from history sells starcraft
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="region"></param>
        /// <param name="limit"></param>
        /// <param name="offset"></param>
        /// <param name="additional"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<List<SelledItem>> TakeHistoryItemsAsync(string itemId, string region = "ru", int limit = 200, int offset = 0, bool additional = true)
        {
            try
            {
                var token = await _TokenHandler.TakeAsync();

                List<SelledItem> answer = new List<SelledItem>();
                using HttpClient client = new HttpClient();
                string url = _stalcraftUrl + $"{region}/auction/{itemId}/history?additional={additional}&limit={limit}&offset={offset}";

                client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                HttpResponseMessage response = await client.GetAsync(url);

                var update = UpdateLimit(response.Headers, token);
                response.EnsureSuccessStatusCode();

                string responseBody = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(responseBody))
                    return answer;
                HistoryItems? responseObject = JsonConvert.DeserializeObject<HistoryItems>(responseBody);
                if (responseObject is not null && responseObject.Prices.Count > 0)
                {
                    responseObject.ItemId = itemId;
                    answer = responseObject.ToSelledItemList();
                }
                await update;
                return answer;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// Multi takedown items from history sells stalcraft
        /// !!! Warning: there is have chance taken much more token limit
        /// </summary>
        /// <param name="itemsId">test</param>
        /// <param name="region"></param>
        /// <param name="limit"></param>
        /// <param name="offset"></param>
        /// <param name="additional"></param>
        /// <returns></returns>
        public async Task<List<SelledItem>> TakeMultyHistoryItemsAsync(List<string> itemsId, string region = "ru", int limit = 200, int offset = 0, bool additional = true)
        {
            try
            {
                if (itemsId.Count() <= 1)
                {
                    if (itemsId.Count() <= 0)
                        return new List<SelledItem>();

                    bool requestIsFine = false;
                    while (!requestIsFine)
                    {
                        try
                        {
                            return await TakeHistoryItemsAsync(itemsId.First(), region, limit, offset, additional);
                        }
                        catch
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
                }
                IToken token = await _TokenHandler.TakeAsync(longTake: true);
                int weightAllRequest = itemsId.Count * _weightOneRequest;

                List<SelledItem> answer = new List<SelledItem>();

                Task[] tasks = new Task[itemsId.Count];

                object locker = new();

                async Task RequestAsync(string id)
                {
                    bool requestIsFine = false;

                    while (!requestIsFine)
                    {
                        try
                        {
                            string url = _stalcraftUrl + $"{region}/auction/{id}/history?additional={additional}&limit={limit}&offset={offset}";
                            using HttpClient client = new HttpClient();

                            client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                            HttpResponseMessage response = await client.GetAsync(url);

                            var update = UpdateLimit(response.Headers, token);
                            response.EnsureSuccessStatusCode();

                            string responseBody = await response.Content.ReadAsStringAsync();
                            if (string.IsNullOrEmpty(responseBody))
                                return;
                            HistoryItems? responseObject = JsonConvert.DeserializeObject<HistoryItems>(responseBody);
                            if (responseObject is not null && responseObject.Prices.Count > 0)
                            {
                                responseObject.ItemId = id;
                                lock (locker)
                                {
                                    answer.AddRange(responseObject.ToSelledItemList());
                                }
                            }
                            requestIsFine = true;
                            await update;
                            return;
                        }
                        catch
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
                }
                int j = 0;
                var manual = new ManualResetEventSlim(false);
                _tokensLimitUpdateEventDict.TryAdd(token, manual);
                for (int i = 0; i < itemsId.Count; i++)
                {
                    if ((token.TokenLimit - _weightOneRequest * 5) <= _weightOneRequest)
                    {
                        await Task.WhenAll(tasks.Skip(j).Take(i - j));
                        j = i;
                        _tokensLimitUpdateEventDict[token].Wait();
                    }
                    string currentId = itemsId[i];
                    tasks[i] = RequestAsync(currentId);
                }
                _tokensLimitUpdateEventDict.TryRemove(token, out manual);
                if (manual is not null)
                    manual.Dispose();
                Task.WaitAll(tasks);

                token.IsTaked = false;

                return answer;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// Takes more items than in stalcraft api but steal much more token limit
        /// !!! Warning: there is a chance of losing items when using the dungerous method
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="region"></param>
        /// <param name="limit"></param>
        /// <param name="offset"></param>
        /// <param name="additional"></param>
        /// <returns></returns>
        public async Task<List<SelledItem>> TakeLongerHistoryItemsAsync(string itemId, string region = "ru", int limit = 200, int offset = 0, bool additional = true, bool exactMode = false)
        {
            try
            {
                if (limit <= 200)
                {
                    bool requestIsFine = false;
                    while (!requestIsFine)
                    {
                        try
                        {
                            return await TakeHistoryItemsAsync(itemId, region, limit, offset, additional);
                        }
                        catch
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
                }
                IToken token = await _TokenHandler.TakeAsync(longTake: true);
                //Step reduction for minimalization chaos chance and disruption
                double oneStep = _requestLotsLimit / 10 * 8;
                int countRequest = (int)Math.Ceiling(limit / oneStep * 1.2);
                int weightAllRequest = countRequest * _weightOneRequest;

                List<SelledItem> answer = new List<SelledItem>();

                Task[] tasks = new Task[countRequest];

                object locker = new();

                async Task RequestAsync(int stepOffset)
                {
                    bool requestIsFine = false;
                    while (!requestIsFine)
                    {
                        try
                        {
                            string url = _stalcraftUrl + $"{region}/auction/{itemId}/history?additional={additional}&limit={_requestLotsLimit}&offset={stepOffset}";
                            using HttpClient client = new HttpClient();

                            client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                            HttpResponseMessage response = await client.GetAsync(url);

                            var update = UpdateLimit(response.Headers, token);
                            response.EnsureSuccessStatusCode();

                            string responseBody = await response.Content.ReadAsStringAsync();
                            if (string.IsNullOrEmpty(responseBody))
                                return;
                            HistoryItems? responseObject = JsonConvert.DeserializeObject<HistoryItems>(responseBody);

                            if (responseObject?.Total < limit)
                            {
                                if (exactMode)
                                    throw new Exception($"Total items be {responseObject?.Total} you try take {limit}");
                                limit = (int)responseObject?.Total;
                            }

                            //If limit more of total items in history
                            if (responseObject.Total < limit)
                            {
                                lock (locker)
                                {
                                    limit = (int)responseObject.Total;
                                    countRequest = (int)Math.Ceiling(limit / oneStep * 1.2);
                                }
                            }

                            if (responseObject is not null && responseObject.Prices.Count > 0)
                            {
                                responseObject.ItemId = itemId;
                                lock (locker)
                                {
                                    answer.AddRange(responseObject.ToSelledItemList());
                                }
                            }
                            requestIsFine = true;
                            await update;
                            return;
                        }
                        catch
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
                }

                int j = 0;
                var manual = new ManualResetEventSlim(false);
                _tokensLimitUpdateEventDict.TryAdd(token, manual);
                for (int i = 0; i < countRequest; i++)
                {
                    int border = token is ApplicationToken ? _weightOneRequest * 25 : _weightOneRequest * 3;
                    if ((token.TokenLimit - border) <= _weightOneRequest)
                    {
                        await Task.WhenAll(tasks.Skip(j).Take(i - j));
                        j = i;
                        _tokensLimitUpdateEventDict[token].Wait();
                    }
                    var stepOffset = (int)oneStep * i + offset;
                    tasks[i] = RequestAsync(stepOffset);
                    token.TokenLimit -= _weightOneRequest;
                }
                _tokensLimitUpdateEventDict.Remove(token, out manual);
                if (manual is not null)
                    manual.Dispose();

                Task.WaitAll(tasks.Where(x => x is not null).ToArray());

                answer = answer.Distinct(SelledItemComparer.Instance).OrderByDescending(x => x.Time).ToList();
                if (exactMode)
                    answer = answer.Take(limit).ToList();

                token.IsTaked = false;
                return answer;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// Items from auction stalcraft into the moment request
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="region"></param>
        /// <param name="limit"></param>
        /// <param name="offset"></param>
        /// <param name="additional"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<List<AuctionItem>> TakeAuctionItemsAsync(string itemId, string region = "ru", int limit = 200, int offset = 0, bool additional = true)
        {
            try
            {
                IToken token = await _TokenHandler.TakeAsync();

                List<AuctionItem> answer = new List<AuctionItem>();
                using HttpClient client = new HttpClient();
                string url = _stalcraftUrl + $"{region}/auction/{itemId}/lots?additional={additional}&limit={limit}&offset={offset}";

                client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                HttpResponseMessage response = await client.GetAsync(url);

                var update = UpdateLimit(response.Headers, token);
                response.EnsureSuccessStatusCode();


                string responseBody = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(responseBody))
                    return answer;
                var responseObject = JsonConvert.DeserializeObject<LotList>(responseBody);
                if (responseObject is not null && responseObject?.Lots?.Count > 0)
                {
                    answer = responseObject.ToAuctionItemsList();
                }
                await update;
                return answer;
            }
            catch
            {
                throw;
            }
        }
        /// <summary>
        /// Multi taken items from auction stalcraft into the moment start and end method
        /// </summary>
        /// <param name="itemsId"></param>
        /// <param name="region"></param>
        /// <param name="limit"></param>
        /// <param name="offset"></param>
        /// <param name="additional"></param>
        /// <returns></returns>
        public async Task<List<AuctionItem>> TakeMultyAuctionItemsAsync(List<string> itemsId, string region = "ru", int limit = 200, int offset = 0, bool additional = true)
        {
            try
            {
                if (itemsId.Count() <= 1)
                {
                    if (itemsId.Count() <= 0)
                        return new List<AuctionItem>();

                    bool requestIsFine = false;
                    while (!requestIsFine)
                    {
                        try
                        {
                            return await TakeAuctionItemsAsync(itemsId.First(), region, limit, offset, additional);
                        }
                        catch
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
                }

                IToken token = await _TokenHandler.TakeAsync(longTake: true);
                int weightAllRequest = itemsId.Count * _weightOneRequest;

                List<AuctionItem> answer = new List<AuctionItem>();

                Task[] tasks = new Task[itemsId.Count];

                object locker = new();

                async Task RequestAsync(string id)
                {
                    bool requestIsFine = false;
                    while (!requestIsFine)
                    {
                        try
                        {
                            string url = _stalcraftUrl + $"{region}/auction/{id}/lots?additional={additional}&limit={limit}&offset={offset}";
                            using HttpClient client = new HttpClient();

                            client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                            HttpResponseMessage response = await client.GetAsync(url);

                            var update = UpdateLimit(response.Headers, token);
                            response.EnsureSuccessStatusCode();

                            string responseBody = await response.Content.ReadAsStringAsync();
                            if (string.IsNullOrEmpty(responseBody))
                                return;
                            var responseObject = JsonConvert.DeserializeObject<LotList>(responseBody);
                            if (responseObject is not null && responseObject?.Lots?.Count > 0)
                            {
                                lock (locker)
                                {
                                    answer.AddRange(responseObject.ToAuctionItemsList());
                                }
                            }

                            requestIsFine = true;
                            await update;
                            return;
                        }
                        catch
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }

                }

                int j = 0;
                var manual = new ManualResetEventSlim(false);
                _tokensLimitUpdateEventDict.TryAdd(token, new ManualResetEventSlim(false));
                for (int i = 0; i < itemsId.Count; i++)
                {
                    if (token.TokenLimit <= _weightOneRequest)
                    {
                        await Task.WhenAll(tasks.Skip(j).Take(i - j));
                        j = i;
                        _tokensLimitUpdateEventDict[token].Wait();
                    }
                    string currentId = itemsId[i];
                    tasks[i] = RequestAsync(currentId);
                    token.TokenLimit -= _weightOneRequest;
                }
                _tokensLimitUpdateEventDict.Remove(token, out manual);
                if (manual is not null)
                    manual.Dispose();
                Task.WaitAll(tasks);

                return answer;
            }
            catch
            {
                throw;
            }

        }


        #endregion

        #region Private methods

        private void OnTokenLimitUpdated(IEnumerable<IToken> updatedTokens)
        {
            var keysIntokensLimitUpdateEventDict = _tokensLimitUpdateEventDict.Keys.ToList();
            var intersectKeys = updatedTokens.Intersect(keysIntokensLimitUpdateEventDict);
            foreach (var key in intersectKeys)
                _tokensLimitUpdateEventDict[key].Set();
            var status = _TokenHandler.HandlerStatus();
        }
        private async Task<IToken> UpdateLimit(HttpHeaders? header, IToken token)
        {
            if (header is null)
                return token;

            string remaining = header.GetValues("x-ratelimit-remaining").FirstOrDefault() ?? string.Empty;

            if (!string.IsNullOrEmpty(remaining))
                if (int.TryParse(remaining, out int newRemaining))
                    token.TokenLimit = newRemaining;

            string resetTime = header.GetValues("x-ratelimit-reset").FirstOrDefault() ?? string.Empty;

            if (!string.IsNullOrEmpty(resetTime))
                if (long.TryParse(resetTime, out long newResetTime))
                {
                    DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(newResetTime);
                    token.TokenResetTime = dateTimeOffset.LocalDateTime;
                }

            string tokenLimit = header.GetValues("X-Ratelimit-Limit").FirstOrDefault() ?? string.Empty;

            if (!string.IsNullOrEmpty(tokenLimit))
                if (int.TryParse(tokenLimit, out int newTokenLimit))
                    token.MaxTokenLimit = newTokenLimit;
            return token;
        }

        #endregion
    }
}