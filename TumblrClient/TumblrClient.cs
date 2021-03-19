using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;


namespace Tumblr.Client
{
    public class TumblrClient : IDisposable
    {
        private const string TumblrBase = "https://api.tumblr.com";
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger = NullLogger.Instance;
        private readonly TumblrAuth _auth;
        private bool _IsAuthenticatedAsUser;

        /// <summary>Initializes a new instance of the <see cref="T:Tumblr.Client.TumblrClient" /> class.</summary>
        /// <param name="client">The HTTPClient object</param>
        /// <param name="key">OAuth 1.0a Consumer Key.</param>
        /// <param name="secret">OAuth 1.0a Consumer Secret.</param>
        /// <param name="logger">Logger.</param>
        public TumblrClient(HttpClient client, string key, string secret, ILogger logger)
        {
            _httpClient = client;
            if (logger != null) _logger = logger;
            _auth = new TumblrAuth(_httpClient, key, secret, _logger);
            _IsAuthenticatedAsUser = false;
        }

        /// <summary>Initializes a new instance of the <see cref="T:Tumblr.Client.TumblrClient" /> class.</summary>
        /// <param name="client">The HTTPClient object</param>
        /// <param name="key">OAuth 1.0a Consumer Key.</param>
        /// <param name="secret">OAuth 1.0a Consumer Secret.</param>
        public TumblrClient(HttpClient client, string key, string secret)
        {
            _httpClient = client;
            _auth = new TumblrAuth(_httpClient, key, secret, _logger);
            _IsAuthenticatedAsUser = false;
        }

        /// <summary>Gets a single post.</summary>
        /// <param name="targetBlog">Blog Id.</param>
        /// <param name="id">Post identifier.</param>
        /// <param name="requiresUserAuth">if set to <c>true</c> [requires user authentication].</param>
        /// <returns>JToken.</returns>
        public async Task<JToken> GetPost(string targetBlog, long id, bool requiresUserAuth)
        {
            bool authComplete = true;
            if (requiresUserAuth && !_IsAuthenticatedAsUser)
            {
                if (authComplete = await _auth.AuthenticateUser())
                {
                    _IsAuthenticatedAsUser = true;
                }
            }

            JToken post = null;
            if (authComplete)
            {
                string apiKey = requiresUserAuth ? string.Empty : $"?api_key={_auth.ApiKey}";

                Uri requestTokenUri = new Uri($"{TumblrBase}/v2/blog/{targetBlog}/posts/{id}{apiKey}");

                var request = new HttpRequestMessage(HttpMethod.Get, requestTokenUri);
                if (requiresUserAuth)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _auth.CreateOauthHeader(request));
                }
                _logger.LogTrace($"Get Post: {requestTokenUri}");
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    _logger.LogInformation($"Retrieved Post {id}");
                    post = JsonConvert.DeserializeObject<JToken>(json);
                    post = post.SelectToken("response");
                }
                else
                {
                    _logger.LogError($"Request Failed {response.StatusCode}");
                }
            }
            return post;
        }

        /// <summary>Create a new Tumblr post.</summary>
        /// <param name="targetBlog">The target blog.</param>
        /// <param name="post">The post contents</param>
        /// <returns>Id of the newly created blog post.</returns>
        public async Task<long> CreatePost(string targetBlog, JToken post)
        {
            bool authComplete = true;
            long id = 0;
            if (!_IsAuthenticatedAsUser)
            {
                if (authComplete = await _auth.AuthenticateUser())
                {
                    _IsAuthenticatedAsUser = true;
                }
            }

            if (authComplete)
            {
                Uri requestTokenUri = new Uri($"{TumblrBase}/v2/blog/{targetBlog}/posts");

                var request = new HttpRequestMessage(HttpMethod.Post, requestTokenUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _auth.CreateOauthHeader(request));
                request.Content = new StringContent(post.ToString(), System.Text.Encoding.UTF8, "application/json");

                _logger.LogTrace($"Create Post: {requestTokenUri}");
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Created)
                {
                    post = JsonConvert.DeserializeObject<JToken>(json);
                    id = post.SelectToken("response.id").Value<long>();
                    _logger.LogInformation($"Created Post {id}");
                }
                else
                {
                    _logger.LogError($"Create Failed {response.StatusCode}");
                }
            }
            return id;
        }

        /// <summary>Update a Tumblr post.</summary>
        /// <param name="targetBlog">The target blog.</param>
        /// <param name="id">The Id of the post to update.</param>
        /// <param name="post">The post contents</param>
        /// <returns>Id of the updated blog post.</returns>
        public async Task<long> UpdatePost(string targetBlog, long id, JToken post)
        {
            bool authComplete = true;
            if (!_IsAuthenticatedAsUser)
            {
                if (authComplete = await _auth.AuthenticateUser())
                {
                    _IsAuthenticatedAsUser = true;
                }
            }

            if (authComplete)
            {
                Uri requestTokenUri = new Uri($"{TumblrBase}/v2/blog/{targetBlog}/posts/{id}");

                var request = new HttpRequestMessage(HttpMethod.Put, requestTokenUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _auth.CreateOauthHeader(request));
                request.Content = new StringContent(post.ToString(), System.Text.Encoding.UTF8, "application/json");

                _logger.LogTrace($"Update Post: {requestTokenUri}");
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    post = JsonConvert.DeserializeObject<JToken>(json);
                    id = post.SelectToken("response.id").Value<long>();
                    _logger.LogInformation($"Updated Post {id}");
                }
                else
                {
                    _logger.LogError($"Update Failed {response.StatusCode}");
                }
            }
            return id;
        }

        /// <summary>Delete a Tumblr post.</summary>
        /// <param name="targetBlog">The target blog.</param>
        /// <param name="id">The Id of the post to delete.</param>
        /// <returns>
        ///   <c>true</c> if the post was deleted, <c>false</c> otherwise.</returns>
        public async Task<bool> DeletePost(string targetBlog, long id)
        {
            bool authComplete = true;
            bool result = false;
            if (!_IsAuthenticatedAsUser)
            {
                if (authComplete = await _auth.AuthenticateUser())
                {
                    _IsAuthenticatedAsUser = true;
                }
            }

            if (authComplete)
            {
                Uri requestTokenUri = new Uri($"{TumblrBase}/v2/blog/{targetBlog}/post/delete?id={id}");

                var request = new HttpRequestMessage(HttpMethod.Delete, requestTokenUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _auth.CreateOauthHeader(request));

                _logger.LogTrace($"Delete Post: {requestTokenUri}");
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    _logger.LogInformation($"Deleted Post {id}");
                    result = true;
                }
                else
                {
                    _logger.LogError($"Delete Failed {response.StatusCode}");

                }
            }
            return result;
        }

        /// <summary>Gets draft posts from the target blog.</summary>
        /// <param name="targetBlog">Blog Id.</param>
        /// <returns>JArray.</returns>
        public async Task<JArray> GetDrafts(string targetBlog)
        {
            _logger.LogTrace("Getting Drafts");
            return await Posts(targetBlog, "/draft", true);
        }

        /// <summary>Gets the queued posts from the target blog.</summary>
        /// <param name="targetBlog">Blog Id.</param>
        /// <returns>JArray.</returns>
        public async Task<JArray> GetQueue(string targetBlog)
        {
            _logger.LogTrace("Getting Queue");
            return await Posts(targetBlog, "/queue", true);
        }

        /// <summary>Gets the Submissions from the target blog.</summary>
        /// <param name="targetBlog">Blog Id.</param>
        /// <returns>JArray.</returns>
        public async Task<JArray> GetSubmissions(string targetBlog)
        {
            _logger.LogTrace("Getting Submissions");
            return await Posts(targetBlog, "/submission", true);
        }

        /// <summary>Gets the public posts from the target blog. </summary>
        /// <param name="targetBlog">Blog Id.</param>
        /// <param name="requiresUserAuth">if set to <c>true</c> [requires user authentication].</param>
        /// <param name="limit">The limit.</param>
        /// <returns>JArray.</returns>
        public async Task<JArray> GetPosts(string targetBlog, bool requiresUserAuth = false, int limit = 0)
        {
            _logger.LogTrace("Getting Posts");
            return await Posts(targetBlog, string.Empty, requiresUserAuth, limit);
        }


        private async Task<JArray> Posts(string targetBlog, string path, bool requiresUserAuth, int limit = 0)
        {
            bool authComplete = true;
            if (requiresUserAuth && !_IsAuthenticatedAsUser)
            {
                if (authComplete = await _auth.AuthenticateUser())
                {
                    _IsAuthenticatedAsUser = true;
                }
            }

            JArray posts = new JArray();
            if (authComplete)
            {
                string apiKey = requiresUserAuth ? "?npf=true" : $"?api_key={_auth.ApiKey}&npf=true";

                Uri requestTokenUri = new Uri($"{TumblrBase}/v2/blog/{targetBlog}/posts{path}{apiKey}");
                int postCount = 0;
                _logger.LogTrace($"Connecting to {requestTokenUri}");
                while (requestTokenUri != null)
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, requestTokenUri);
                    if (requiresUserAuth)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", _auth.CreateOauthHeader(request));
                    }
                    var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    var jsonString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        var json = JsonConvert.DeserializeObject<JToken>(jsonString);
                        if (json.SelectToken("response.posts") is JArray morePosts)
                        {
                            _logger.LogTrace($"Recieved Posts {postCount} to {postCount += morePosts.Count}");
                            posts.Merge(morePosts);
                        }
                        string href = json.SelectToken("response._links.next.href")?.ToString();
                        apiKey = requiresUserAuth ? string.Empty : $"&api_key={_auth.ApiKey}";
                        requestTokenUri = (href == null) ? null : new Uri($"{TumblrBase}{href}{apiKey}");
                        if (limit != 0 && postCount >= limit)
                        {
                            requestTokenUri = null;
                        }
                    }
                    else
                    {
                        _logger.LogError($"Request Failed {response.StatusCode}");
                        requestTokenUri = null;
                    }
                }
            }
            return posts;
        }

        public void Dispose()
        {
        }
    }
}
