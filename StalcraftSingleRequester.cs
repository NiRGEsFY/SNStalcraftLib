using Newtonsoft.Json;
using System.Text;
using SNStalcraftRequestLib.Objects.Application;
using SNStalcraftRequestLib.DtoObjects.Application;
using SNStalcraftRequestLib.Objects.Auction;
using SNStalcraftRequestLib.DtoObjects.Auction;
using System.Net.Http.Headers;
using System.Collections.Generic;
using System.Net.Sockets;
using SNStalcraftRequestLib.Objects.Comparers;

namespace SNStalcraftRequestLib
{
    public class StalcraftSingleRequester
    {
        public ApplicationToken Token { get; private set; }
        public string ApplicationSecret { get; private set; }
        public int ApplicationId { get; private set; }
        public string GrantType { get; private set; }
        public const string _exboUrl = "https://exbo.net/";
        public const string _stalcraftUrl = "https://eapi.stalcraft.net/";
        public readonly int _weightOneRequest = 2;
        public readonly int _requestLotsLimit = 200;

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
            Token = ApplicationAuthAsync().GetAwaiter().GetResult();
            Token.TokenLimit = 50;
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
            Token = await ApplicationAuthAsync();
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
            
            return new ApplicationToken(tokenDto);
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
            PreChecking();

            List<SelledItem> answer = new List<SelledItem>();
            using HttpClient client = new HttpClient();
            string url = _stalcraftUrl + $"{region}/auction/{itemId}/history?additional={additional}&limit={limit}&offset={offset}";

            client.DefaultRequestHeaders.Add("Authorization", $"{Token.TokenType} " + Token.AccessToken);
            HttpResponseMessage response = await client.GetAsync(url);

            UpdateLimit(response.Headers);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseBody))
                return answer;
            var responseObject = JsonConvert.DeserializeObject<HistoryItems>(responseBody);
            if(responseObject is not null && responseObject.Prices.Count > 0)
            {
                answer = responseObject.ToSelledItemList();
            }

            return answer;
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
            if(itemsId.Count() <= 1)
            {
                if (itemsId.Count() <= 0)
                    return new List<SelledItem>();
                return await TakeHistoryItemsAsync(itemsId.First(), region, limit, offset, additional);
            }
            PreChecking();
            int weightAllRequest = itemsId.Count * _weightOneRequest;

            List<SelledItem> answer = new List<SelledItem>();

            Task[] tasks = new Task[itemsId.Count];

            object locker = new();

            async Task RequestAsync(string id)
            {
                try
                {
                    string url = _stalcraftUrl + $"{region}/auction/{id}/history?additional={additional}&limit={limit}&offset={offset}";
                    using HttpClient client = new HttpClient();

                    client.DefaultRequestHeaders.Add("Authorization", $"{Token.TokenType} " + Token.AccessToken);
                    HttpResponseMessage response = await client.GetAsync(url);

                    UpdateLimit(response.Headers);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrEmpty(responseBody))
                        return;
                    var responseObject = JsonConvert.DeserializeObject<HistoryItems>(responseBody);
                    if (responseObject is not null && responseObject.Prices.Count > 0)
                    {
                        lock (locker)
                        {
                            answer.AddRange(responseObject.ToSelledItemList());
                        }
                    }
                    return;
                }
                catch
                {
                    return;
                }
            }
            int j = 0;
            for (int i = 0; i < itemsId.Count; i++)
            {
                if(Token.TokenLimit <= _weightOneRequest)
                {
                    await Task.WhenAll(tasks.Skip(j).Take(i-j));
                    j = i;
                    await RefreshTokenLimit();
                }
                string currentId = itemsId[i];
                tasks[i] = Task.Run(() => RequestAsync(currentId));
                Token.TokenLimit -= _weightOneRequest;
            }

            Task.WaitAll(tasks);

            return answer;
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
            if (limit <= 200)
                return await TakeHistoryItemsAsync(itemId,region,limit,offset,additional);

            PreChecking();
            //Step reduction for minimalization chaos chance and disruption
            double oneStep = _requestLotsLimit / 10 * 8;
            int countRequest = (int)Math.Ceiling(limit / oneStep);
            int weightAllRequest = countRequest * _weightOneRequest;

            List<SelledItem> answer = new List<SelledItem>();

            Task[] tasks = new Task[countRequest];

            object locker = new();

            async Task RequestAsync(int stepOffset)
            {
                try
                {
                    string url = _stalcraftUrl + $"{region}/auction/{itemId}/history?additional={additional}&limit={_requestLotsLimit}&offset={stepOffset}";
                    using HttpClient client = new HttpClient();

                    client.DefaultRequestHeaders.Add("Authorization", $"{Token.TokenType} " + Token.AccessToken);
                    HttpResponseMessage response = await client.GetAsync(url);

                    UpdateLimit(response.Headers);
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrEmpty(responseBody))
                        return;
                    var responseObject = JsonConvert.DeserializeObject<HistoryItems>(responseBody);
                    if (responseObject is not null && responseObject.Prices.Count > 0)
                    {
                        lock (locker)
                        {
                            answer.AddRange(responseObject.ToSelledItemList());
                        }
                    }
                    return;
                }
                catch
                {
                    return;
                }
            }

            int j = 0;
            for (int i = 0; i < countRequest; i++)
            {
                if (Token.TokenLimit <= _weightOneRequest)
                {
                    await Task.WhenAll(tasks.Skip(j).Take(i - j));
                    j = i;
                    await RefreshTokenLimit();
                }
                var stepOffset = (int)oneStep * i + offset;
                tasks[i] = Task.Run(() => RequestAsync(stepOffset));
                Token.TokenLimit -= _weightOneRequest;
            }

            Task.WaitAll(tasks);

            answer = answer.Distinct(SelledItemComparer.Instance).OrderByDescending(x => x.Time).ToList();
            if (exactMode)
                answer = answer.Take(limit).ToList();


            return answer;
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
            PreChecking();

            List<AuctionItem> answer = new List<AuctionItem>();
            using HttpClient client = new HttpClient();
            string url = _stalcraftUrl + $"{region}/auction/{itemId}/lots?additional={additional}&limit={limit}&offset={offset}";

            client.DefaultRequestHeaders.Add("Authorization", $"{Token.TokenType} " + Token.AccessToken);
            HttpResponseMessage response = await client.GetAsync(url);

            UpdateLimit(response.Headers);
            response.EnsureSuccessStatusCode();

            string responseBody = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrEmpty(responseBody))
                return answer;
            var responseObject = JsonConvert.DeserializeObject<LotList>(responseBody);
            if (responseObject is not null && responseObject?.Lots?.Count > 0)
            {
                answer = responseObject.ToAuctionItemsList();
            }

            return answer;
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
            PreChecking();
            int weightAllRequest = itemsId.Count * _weightOneRequest;

            List<AuctionItem> answer = new List<AuctionItem>();

            Task[] tasks = new Task[itemsId.Count];

            object locker = new();

            async Task RequestAsync(string id)
            {
                try
                {
                    string url = _stalcraftUrl + $"{region}/auction/{id}/lots?additional={additional}&limit={limit}&offset={offset}";
                    using HttpClient client = new HttpClient();

                    client.DefaultRequestHeaders.Add("Authorization", $"{Token.TokenType} " + Token.AccessToken);
                    HttpResponseMessage response = await client.GetAsync(url);

                    UpdateLimit(response.Headers);
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
                    return;
                }
                catch
                {
                    return;
                }
            }

            int j = 0;
            for (int i = 0; i < itemsId.Count; i++)
            {
                if (Token.TokenLimit <= _weightOneRequest)
                {
                    await Task.WhenAll(tasks.Skip(j).Take(i - j));
                    j = i;
                    await RefreshTokenLimit();
                }
                string currentId = itemsId[i];
                tasks[i] = Task.Run(() => RequestAsync(currentId));
                Token.TokenLimit -= _weightOneRequest;
            }

            Task.WaitAll(tasks);

            return answer;
        }
        #region Private methods

        private void PreChecking()
        {
            if (string.IsNullOrWhiteSpace(Token.AccessToken))
                throw new ArgumentNullException(nameof(Token.AccessToken));

            if (Token.TokenLimit <= 0)
            {
                if (!_tokenIsRefreshing)
                    Task.Run(RefreshTokenLimit);
                throw new Exception($"Token limit is over, {TimeSpan.FromTicks(Token.TokenResetTime.Ticks - DateTime.Now.Ticks)} remaining");
            }
        }
        private bool _tokenIsRefreshing = false;
        private async Task RefreshTokenLimit()
        {
            _tokenIsRefreshing = true;

            int timeDelay = (int)TimeSpan.FromTicks(Token.TokenResetTime.Ticks - DateTime.Now.Ticks).TotalMilliseconds;
            if(timeDelay > 0)
                await Task.Delay(timeDelay);
            Token.TokenLimit = 400;

            _tokenIsRefreshing = false;
        }
        private void UpdateLimit(HttpHeaders header)
        {
            string remaining = string.Empty;
            remaining = header.GetValues("x-ratelimit-remaining").FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(remaining))
                return;

            Token.TokenLimit = int.Parse(remaining);
            string resetTime = string.Empty;
            resetTime = header.GetValues("x-ratelimit-reset").FirstOrDefault() ?? string.Empty;

            if (string.IsNullOrEmpty(resetTime))
                return;

            long digitResetTime = long.Parse(resetTime);
            DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeMilliseconds(digitResetTime);
            Token.TokenResetTime = dateTimeOffset.LocalDateTime;
        }

        
        #endregion
    }
}