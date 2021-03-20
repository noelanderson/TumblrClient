using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Text;
using System.Net;
using System.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;

namespace Tumblr.Client
{
    public class TumblrAuth
    {
        public const string RequestKey = "https://www.tumblr.com/oauth/request_token";
        public const string Authorize = "https://www.tumblr.com/oauth/authorize";
        public const string AccessKey = "https://www.tumblr.com/oauth/access_token";

        private const string _OAuthVersion = "1.0a";
        private const string _OAuthConsumerKeyKey = "oauth_consumer_key";
        private const string _OAuthCallbackKey = "oauth_callback";
        private const string _OAuthVersionKey = "oauth_version";
        private const string _OAuthSignatureMethodKey = "oauth_signature_method";
        private const string _OAuthSignatureKey = "oauth_signature";
        private const string _OAuthTimestampKey = "oauth_timestamp";
        private const string _OAuthNonceKey = "oauth_nonce";
        private const string _OAuthTokenKey = "oauth_token";
        private const string _HMACSHA1SignatureType = "HMAC-SHA1";

        private readonly string _consumerKey;
        private readonly string _consumerSecret;

        private readonly SignatureParameters _signatureParameters = new SignatureParameters();
        private readonly HttpClient _client;
        private readonly ILogger _logger;

        private string Token { get; set; }
        private string TokenSecret { get; set; }

        public string ApiKey 
        {
            get {return _consumerKey;}
        }

        /// <summary>Initializes a new instance of the <see cref="T:Tumblr.Client.TumblrAuth" /> class.</summary>
        /// <param name="client">The client.</param>
        /// <param name="key">The key.</param>
        /// <param name="secret">The secret.</param>
        public TumblrAuth(HttpClient client, string key, string secret, ILogger logger)
        {
            _client = client;
            _consumerKey = key;
            _consumerSecret = secret;
            ResetSignatureParameters();
            _logger = logger;
        }

        /// <summary>Creates a OAuth Authorization header</summary>
        /// <param name="requestMessage"></param>
        public string CreateOauthHeader(HttpRequestMessage requestMessage)
        {
            return CreateOauthHeaderContent(requestMessage.Method.ToString(), requestMessage.RequestUri);
        }

        public async Task<bool> AuthenticateUser()
        {
            bool authenticationSucessful = false;

            // Reset all our state
            ResetSignatureParameters();
            Token = null;
            TokenSecret = null;

            // Create a local redirect URL for the Authentication sequence
            // uses TCPLister created with port 0 to find an unused port for our http operations
            TcpListener tcpListener = new TcpListener(IPAddress.Loopback, 0);
            tcpListener.Start();
            string redirectUri = $"http://localhost:{((IPEndPoint)tcpListener.LocalEndpoint).Port}/authenticationredirect/";
            tcpListener.Stop();

            // Request Oauth initial token
            Uri requestTokenUri = new Uri($"{TumblrAuth.RequestKey}");
            string redirectBody = $"{_OAuthCallbackKey}={Uri.EscapeDataString(redirectUri)}";
            _logger.LogTrace($"Auth: Request Oauth initial token: {requestTokenUri}");
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, requestTokenUri)
            {
                Content = new StringContent(redirectBody, Encoding.Default, "application/x-www-form-requestTokenUri")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", CreateOauthHeaderContent("POST", requestTokenUri, redirectBody));
            var response = await _client.SendAsync(req);
            var reply = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                SignatureParameters elements = new SignatureParameters(reply);
                Token = elements.Get("oauth_token");
                TokenSecret = elements.Get("oauth_token_secret");

                // Authorize URL
                string verifier = AuthorizeInBrowser(redirectUri);
                if (verifier != null)
                {
                    // Access Token (get the final token/secret we can use to sign API requests)
                    Uri accessTokenUri = new Uri($"{TumblrAuth.AccessKey}?oauth_verifier={verifier}");
                    _logger.LogTrace($"Auth: Request Oauth final token: {accessTokenUri}");
                    req = new HttpRequestMessage(HttpMethod.Post, accessTokenUri);
                    req.Headers.Authorization = new AuthenticationHeaderValue("OAuth", CreateOauthHeader(req));
                    response = await _client.SendAsync(req);
                    reply = await response.Content.ReadAsStringAsync();
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        _logger.LogInformation($"Auth: Authentication Succeeded");
                        elements = new SignatureParameters(reply);
                        Token = elements.Get("oauth_token");
                        TokenSecret = elements.Get("oauth_token_secret");
                        authenticationSucessful = true;
                    }
                    else
                    {
                        _logger.LogCritical($"Auth: Authentication Failed (final token): {response.StatusCode}: {reply}");
                    }
                }
                else
                {
                    _logger.LogCritical($"Auth: Authentication Failed (user declined)");
                }
            }
            else
            {
                _logger.LogCritical($"Auth: Authentication Failed (initial token): {response.StatusCode}: {reply}");
            }
            if(!authenticationSucessful)
            {
                // Reset all our state
                Token = null;
                TokenSecret = null;
                _signatureParameters.Clear();
            }
            return authenticationSucessful;
        }

        private void ResetSignatureParameters()
        {
            _signatureParameters.Clear();
            _signatureParameters.Set(_OAuthVersionKey, _OAuthVersion);
            _signatureParameters.Set(_OAuthSignatureMethodKey, _HMACSHA1SignatureType);
            _signatureParameters.Set(_OAuthConsumerKeyKey, _consumerKey);
        }

        private string CreateSignature(string httpMethod, Uri url)
        {
            string signatureBase = ConstructBaseStringForSigning(httpMethod, url);
            byte[] buffKeyMaterial = System.Text.Encoding.UTF8.GetBytes(string.Format("{0}&{1}", Rfc5849UrlEncode(_consumerSecret), string.IsNullOrEmpty(TokenSecret) ? string.Empty : Rfc5849UrlEncode(TokenSecret)));
            using (HMACSHA1 hmac = new HMACSHA1(buffKeyMaterial))
            {
                byte[] dataBuffer = System.Text.Encoding.UTF8.GetBytes(signatureBase);
                byte[] computedHash = hmac.ComputeHash(dataBuffer);
                return Rfc5849UrlEncode(System.Convert.ToBase64String(computedHash));
            }
        }

        // Constructs the base string used to produce the signature hash
        private string ConstructBaseStringForSigning(string httpMethod, Uri url)
        {
            string normalizedUrl = string.Format("{0}://{1}", url.Scheme, url.Host);
            if (!((url.Scheme == "http" && url.Port == 80) || (url.Scheme == "https" && url.Port == 443)))
            {
                normalizedUrl += ":" + url.Port;
            }
            normalizedUrl += url.AbsolutePath;
            return $"{httpMethod.ToUpper()}&{Rfc5849UrlEncode(normalizedUrl)}&{Rfc5849UrlEncode(_signatureParameters.ToString('&'))}";
        }

        private string Rfc5849UrlEncode(string value)
        {
            const string unreservedCharacterSet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~";
            StringBuilder result = new StringBuilder();
            foreach (char symbol in value)
            {
                result.Append(unreservedCharacterSet.IndexOf(symbol) != -1 ? symbol.ToString() : $"%{(int)symbol:X2}");
            }
            return result.ToString();
        }

        private string CreateOauthHeaderContent(string httpMethod, Uri url, string body = null)
        {
            ResetSignatureParameters();
            _signatureParameters.ParseString(url.Query);
            _signatureParameters.Set(_OAuthNonceKey, Guid.NewGuid().ToString("N"));
            _signatureParameters.Set(_OAuthTimestampKey, DateTimeOffset.Now.ToUnixTimeSeconds().ToString());
            if (!string.IsNullOrEmpty(Token))
            {
                _signatureParameters.Set(_OAuthTokenKey, Token);
            }
            if (!string.IsNullOrEmpty(body))
            {
                _signatureParameters.ParseString(body);
            }
            _signatureParameters.Set(_OAuthSignatureKey, CreateSignature(httpMethod, url));
            return _signatureParameters.ToString(',');
        }

        private string AuthorizeInBrowser(string redirectUri)
        {
            // Creates an HttpListener to listen for requests on that redirect URI.
            using (var httpListener = new HttpListener())
            {
                httpListener.Prefixes.Add(redirectUri);
                httpListener.Start();
                _logger.LogTrace($"Auth: Started HTTP Listener: {redirectUri}");

                // Creates the authorization request.
                string authorizationRequest = $"{TumblrAuth.Authorize}?oauth_token={Token}";

                // Opens request in the browser.
                _logger.LogTrace($"Auth: Open browser for user permission grant: {authorizationRequest}");
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    authorizationRequest = authorizationRequest.Replace("&", "^&");
                    Process.Start("cmd", $"/c start {authorizationRequest}");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", authorizationRequest); // Not tested
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", authorizationRequest); // Not tested
                }

                HttpListenerContext context = null;
                string verifier = null;
                // Wait for the OAuth authorization redirect. Wait for one minute max
                if (Task.Run(async () => context = await httpListener.GetContextAsync()).Wait(60000))
                {
                    verifier = context.Request.QueryString.Get("oauth_verifier");
                    string token = context.Request.QueryString.Get("oauth_token");

                    // Checks for errors.
                    string responseMessage = $"<!DOCTYPE html><html><head></head><body><h1>Tumblr Authorization failed</h1><p>You can close this window now</p></body></html>";
                    if (verifier != null)
                    {
                        _logger.LogInformation($"Auth: Permissions Grant allowed by user");
                        responseMessage = $"<!DOCTYPE html><html><head></head><body><h1>Tumblr Authorization Successfull</h1><p>Token: {token}</p><p>Verifier: {verifier}</p><p>You can close this window now</p></body></html>";
                    }
                    else
                    {
                        _logger.LogCritical($"Auth: Permissions Grant denied by user");
                    }

                    // Sends an HTTP response to the browser.
                    var response = context.Response;
                    var buffer = System.Text.Encoding.UTF8.GetBytes(responseMessage);
                    response.ContentLength64 = buffer.Length;
                    var responseOutput = response.OutputStream;
                    responseOutput.Write(buffer, 0, buffer.Length);
                    responseOutput.Close();
                }
                else
                {
                    _logger.LogCritical($"Auth: Permissions Grant Timed out");
                }

                _logger.LogTrace($"Auth: Stopped HTTP Listener: {redirectUri}");
                httpListener.Stop();
                return verifier;
            }
        }
    }

    // Class manages all the elements of the OAuth signature in a dictionary
    public class SignatureParameters
    {
        private readonly Dictionary<string, string> _parameters = new Dictionary<string, string>();

        public SignatureParameters()
        {
        }

        public SignatureParameters(string initialValues)
        {
            ParseString(initialValues);
        }

        public string Get(string key)
        {
            string value = null;
            _parameters.TryGetValue(key, out value);
            return value;
        }

        public void Set(string name, string value)
        {
            _parameters.TryAdd(name, value);
        }

        public void Clear()
        {
            _parameters.Clear();
        }

        public void ParseString(string inputString)
        {
            if (!string.IsNullOrEmpty(inputString))
            {
                if (inputString.StartsWith('?'))
                {
                    inputString = inputString.Remove(0, 1);
                }
                string[] p = inputString.Split('&');
                foreach (string s in p)
                {
                    if (!string.IsNullOrEmpty(s))
                    {
                        if (s.Contains("="))
                        {
                            string[] temp = s.Split('=');
                            _parameters.TryAdd(temp[0], temp[1]);
                        }
                        else
                        {
                            _parameters.TryAdd(s, string.Empty);
                        }
                    }
                }
            }
        }

        public string ToString(char listSeperator)
        {
            List<string> items = new List<string>();
            foreach (var item in _parameters.Keys.OrderBy(k => k))
            {
                items.Add($"{item}={_parameters[item]}");
            }
            return string.Join(listSeperator, items.ToArray());
        }
    }

}
