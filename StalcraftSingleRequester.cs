﻿using Newtonsoft.Json;
using System.Text;
using SNStalcraftRequestLib.Objects.Application;
using SNStalcraftRequestLib.DtoObjects.Application;
using SNStalcraftRequestLib.Objects.Auction;
using SNStalcraftRequestLib.DtoObjects.Auction;
using System.Net.Http.Headers;
using SNStalcraftRequestLib.Objects.Comparers;
using SNStalcraftRequestLib.Interfaces;
using System.Collections.Concurrent;

namespace SNStalcraftRequestLib
{
    public class StalcraftSingleRequester
    {
        public TokenHandler _TokenHandler { get; private set; }
        public string ApplicationSecret { get; private set; }
        public int ApplicationId { get; private set; }
        public string GrantType { get; private set; }
        public const string _exboUrl = "https://exbo.net/";
        public const string _stalcraftUrl = "https://eapi.stalcraft.net/";
        public const int _weightOneRequest = 2;
        public const int _requestLotsLimit = 200;
        public RequesterStatus _status;
        private readonly ConcurrentDictionary<IToken, ManualResetEventSlim> _tokensLimitUpdateEventDict = new ConcurrentDictionary<IToken, ManualResetEventSlim>();
        /// <summary>
        /// Initialization application token
        /// </summary>
        /// <param name="id"></param>
        /// <param name="secret"></param>
        public StalcraftSingleRequester(int id, string secret, string grantType = "client_credentials") 
        {
            ApplicationId = id;
            ApplicationSecret = secret;
            GrantType = grantType;
            var token = ApplicationAuthAsync().GetAwaiter().GetResult();
            token.TokenLimit = 400;
            _TokenHandler = new TokenHandler(new List<IToken> { token }, TimeSpan.FromSeconds(5));
            _TokenHandler.UpdatedTokenLimitNotify += OnTokenLimitUpdated;
            _status = new RequesterStatus();
        }
        public StalcraftSingleRequester(int id, string secret, TimeSpan resetTokenLimitPeriod, string grantType = "client_credentials")
            :this(id, secret, grantType)
        {
            var token = ApplicationAuthAsync().GetAwaiter().GetResult();
            token.TokenLimit = 400;
            _TokenHandler = new TokenHandler(new List<IToken> { token }, resetTokenLimitPeriod);
            _status = new RequesterStatus();
        }
        public RequesterStatus Status()
        {
            return _status;
        }
        /// <summary>
        /// Reset application token
        /// </summary>
        /// <param name="id"></param>
        /// <param name="secret"></param>
        public void ResetApplicationToken(int id, string secret, string grant_type = "client_credentials")
        {
            ResetApplicationTokenAsync(id, secret, grant_type)
                .GetAwaiter().GetResult();
        }
        /// <summary>
        /// Async reset application token
        /// </summary>
        /// <param name="id"></param>
        /// <param name="secret"></param>
        public async Task ResetApplicationTokenAsync(int id, string secret, string grant_type = "client_credentials")
        {
            ApplicationId = id;
            ApplicationSecret = secret;
            var token = _TokenHandler.Take();
            if(token is not null)
                _TokenHandler.Remove(token);

            _TokenHandler.Add(await ApplicationAuthAsync());
        }
        private async Task<ApplicationToken> ApplicationAuthAsync()
        {
            using HttpClient client = new HttpClient();
            string url = _exboUrl + "oauth/token";
            var requestData = new
            {
                client_id = ApplicationId,
                client_secret = ApplicationSecret,
                grant_type = GrantType
            };
            string jsonData = JsonConvert.SerializeObject(requestData);
            StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
            HttpResponseMessage response = await client.PostAsync(url, content);

            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            ExboTokenDto tokenDto = JsonConvert.DeserializeObject<ExboTokenDto>(responseBody)
                ?? throw new InvalidOperationException("Failed to deserialize token response");
            return new ApplicationToken(tokenDto, ApplicationId, ApplicationSecret);
        }
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
            IToken? token = null;
            _status.RequestInTask++;
            _status.RequestInProgress++;

            try
            {
                token = await _TokenHandler.TakeAsync();
                PreChecking(token);

                List<SelledItem> answer = new List<SelledItem>();
                using HttpClient client = new HttpClient();
                string url = _stalcraftUrl + $"{region}/auction/{itemId}/history?additional={additional}&limit={limit}&offset={offset}";

                client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                HttpResponseMessage response = await client.GetAsync(url);

                UpdateLimit(response.Headers, token);
                response.EnsureSuccessStatusCode();

                _status.RequestInProgress--;
                _status.RequestComplete++;

                string responseBody = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(responseBody))
                    return answer;
                HistoryItems? responseObject = JsonConvert.DeserializeObject<HistoryItems>(responseBody);
                if (responseObject is not null && responseObject.Prices.Count > 0)
                {
                    responseObject.ItemId = itemId;
                    answer = responseObject.ToSelledItemList();
                }



                _status.RequestComplete--;
                _status.RequestInTask--;

                return answer;
            }
            catch
            {

                _status.RequestInProgress--;
                _status.RequestInTask--;

                throw;
            }
            finally
            {
                if (token is not null)
                    token.IsTaked = false;
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
            IToken? token = null;

            int additionTaskInTask = itemsId.Count;
            int additionCompleteRequest = 0;
            _status.RequestInTask += additionTaskInTask;

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
                token = await _TokenHandler.TakeAsync(longTake: true);
                PreChecking(token);
                int weightAllRequest = itemsId.Count * _weightOneRequest;

                List<SelledItem> answer = new List<SelledItem>();

                Task[] tasks = new Task[itemsId.Count];

                object locker = new();

                async Task RequestAsync(string id)
                {
                    lock(locker)
                        _status.RequestInProgress++;

                    bool requestIsFine = false;

                    while (!requestIsFine)
                    {
                        try
                        {
                            string url = _stalcraftUrl + $"{region}/auction/{id}/history?additional={additional}&limit={limit}&offset={offset}";
                            using HttpClient client = new HttpClient();

                            client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                            HttpResponseMessage response = await client.GetAsync(url);

                            UpdateLimit(response.Headers, token);
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

                            lock (locker)
                            {
                                _status.RequestComplete++;
                                additionCompleteRequest++;
                                _status.RequestInProgress--;
                            }

                            return;
                        }
                        catch
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10));
                        }
                    }
                    _status.RequestInProgress--;
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
                if(manual is not null)
                    manual.Dispose();
                Task.WaitAll(tasks);

                token.IsTaked = false;

                _status.RequestComplete -= additionCompleteRequest;
                _status.RequestInTask -= additionTaskInTask;

                return answer;
            }
            catch
            {

                _status.RequestComplete -= additionCompleteRequest;
                _status.RequestInTask -= additionTaskInTask;

                throw;
            }
            finally
            {
                if (token is not null)
                    token.IsTaked = false;
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
            IToken? token = null;
            int additionTaskInTask = 0;
            int additionCompleteRequest = 0;
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
                token = await _TokenHandler.TakeAsync(longTake: true);
                PreChecking(token);
                //Step reduction for minimalization chaos chance and disruption
                double oneStep = _requestLotsLimit / 10 * 8;
                int countRequest = (int)Math.Ceiling(limit / oneStep * 1.2);
                int weightAllRequest = countRequest * _weightOneRequest;


                _status.RequestInTask += countRequest;

                additionTaskInTask += countRequest;

                List<SelledItem> answer = new List<SelledItem>();

                Task[] tasks = new Task[countRequest];

                object locker = new();

                async Task RequestAsync(int stepOffset)
                {
                    lock (locker)
                        _status.RequestInProgress++;

                    bool requestIsFine = false;
                    while (!requestIsFine)
                    {
                        try
                        {
                            string url = _stalcraftUrl + $"{region}/auction/{itemId}/history?additional={additional}&limit={_requestLotsLimit}&offset={stepOffset}";
                            using HttpClient client = new HttpClient();

                            client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                            HttpResponseMessage response = await client.GetAsync(url);

                            UpdateLimit(response.Headers, token);
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
                            if(responseObject.Total < limit)
                            {
                                lock (locker)
                                {
                                    _status.RequestInTask -= countRequest;
                                    additionTaskInTask -= countRequest;

                                    limit = (int)responseObject.Total;
                                    countRequest = (int)Math.Ceiling(limit / oneStep * 1.2);
                                    
                                    _status.RequestInTask += countRequest;
                                    additionTaskInTask += countRequest;
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

                            lock (locker)
                            {
                                _status.RequestComplete++;
                                additionCompleteRequest++;
                                _status.RequestInProgress--;
                            }

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

                _status.RequestComplete -= additionCompleteRequest;
                _status.RequestInTask -= additionTaskInTask;

                return answer;
            }
            catch
            {
                _status.RequestComplete -= additionCompleteRequest;
                _status.RequestInTask -= additionTaskInTask;
                throw;
            }
            finally
            {
                if (token is not null)
                    token.IsTaked = false;
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
            IToken? token = null;
            _status.RequestInTask++;
            _status.RequestInProgress++;
            try
            {
                token = await _TokenHandler.TakeAsync();
                PreChecking(token);

                List<AuctionItem> answer = new List<AuctionItem>();
                using HttpClient client = new HttpClient();
                string url = _stalcraftUrl + $"{region}/auction/{itemId}/lots?additional={additional}&limit={limit}&offset={offset}";

                client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                HttpResponseMessage response = await client.GetAsync(url);

                UpdateLimit(response.Headers, token);
                response.EnsureSuccessStatusCode();


                string responseBody = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(responseBody))
                    return answer;
                var responseObject = JsonConvert.DeserializeObject<LotList>(responseBody);
                if (responseObject is not null && responseObject?.Lots?.Count > 0)
                {
                    answer = responseObject.ToAuctionItemsList();
                }

                _status.RequestComplete++;

                _status.RequestInTask--;
                _status.RequestInProgress--;

                _status.RequestComplete--;
                return answer;
            }
            catch
            {

                _status.RequestInTask--;
                _status.RequestInProgress--;

                throw;
            }
            finally
            {
                if (token is not null)
                    token.IsTaked = false;
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
            IToken? token = null;
            int additionTaskInTask = 0;
            int additionCompleteRequest = 0;
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

                token = await _TokenHandler.TakeAsync(longTake: true);
                PreChecking(token);
                int weightAllRequest = itemsId.Count * _weightOneRequest;

                List<AuctionItem> answer = new List<AuctionItem>();

                Task[] tasks = new Task[itemsId.Count];

                object locker = new();

                async Task RequestAsync(string id)
                {
                    lock(locker)
                        _status.RequestInProgress++;

                    bool requestIsFine = false;
                    while (!requestIsFine)
                    {
                        try
                        {
                            string url = _stalcraftUrl + $"{region}/auction/{id}/lots?additional={additional}&limit={limit}&offset={offset}";
                            using HttpClient client = new HttpClient();

                            client.DefaultRequestHeaders.Add("Authorization", $"{token.TokenType} " + token.AccessToken);
                            HttpResponseMessage response = await client.GetAsync(url);

                            UpdateLimit(response.Headers, token);
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

                            lock(locker)
                            {
                                _status.RequestComplete++;
                                additionCompleteRequest++;
                                _status.RequestInProgress--;
                            }

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


                _status.RequestComplete -= additionCompleteRequest;
                _status.RequestInTask -= additionTaskInTask;

                return answer;
            }
            catch
            {
                _status.RequestComplete -= additionCompleteRequest;
                _status.RequestInTask -= additionTaskInTask;

                throw;
            }
            finally
            {
                if (token is not null)
                    token.IsTaked = false;
            }
            
        }
                
        #region Private methods
        private void OnTokenLimitUpdated(IEnumerable<IToken> updatedTokens)
        {
            var keysIntokensLimitUpdateEventDict = _tokensLimitUpdateEventDict.Keys.ToList();
            var intersectKeys = updatedTokens.Intersect(keysIntokensLimitUpdateEventDict);
            foreach (var key in intersectKeys)
                _tokensLimitUpdateEventDict[key].Set();
            var status = _TokenHandler.HandlerStatus();

            _status.SumTokenLimit = status.SumTokenLimit;
            _status.CountFreeToken = status.CountFreeToken;
            _status.CountToken = status.CountToken;
        }
        private void PreChecking(IToken? token)
        {
            if (string.IsNullOrWhiteSpace(token?.AccessToken))
                throw new ArgumentNullException(nameof(token.AccessToken));
        }
        private bool _tokenIsRefreshing = false;
        private void UpdateLimit(HttpHeaders? header, IToken token)
        {
            try
            {
                if (header is null)
                    return;

                string remaining = string.Empty;
                remaining = header.GetValues("x-ratelimit-remaining").FirstOrDefault() ?? string.Empty;

                if (string.IsNullOrEmpty(remaining))
                    return;

                token.TokenLimit = int.Parse(remaining);
                string resetTime = string.Empty;
                resetTime = header.GetValues("x-ratelimit-reset").FirstOrDefault() ?? string.Empty;

                if (string.IsNullOrEmpty(resetTime))
                    return;

                long digitResetTime = long.Parse(resetTime);
                DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(digitResetTime);
                token.TokenResetTime = dateTimeOffset.UtcDateTime;
            }
            catch
            {

            }

        }

        #endregion

    }
}