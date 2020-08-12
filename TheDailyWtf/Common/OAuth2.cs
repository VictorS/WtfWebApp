﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Configuration;
using System.Web.Mvc;
using TheDailyWtf.Controllers;

namespace TheDailyWtf
{
    public static class OAuth2
    {
        public class Endpoint
        {
            public string Auth { get; internal set; }
            public string Token { get; internal set; }
            public string RedirectUrl { get { return "http://" + Config.Wtf.Host + "/login/" + ConfigPrefix.ToLowerInvariant(); } }
            internal string ConfigPrefix { get; set; }
            internal string ClientId { get { return WebConfigurationManager.AppSettings[ConfigPrefix + "ClientId"]; } }
            internal string Secret { get { return WebConfigurationManager.AppSettings[ConfigPrefix + "Secret"]; } }
            internal string Scopes { get; set; }
        }

        public static readonly Endpoint Google = new Endpoint { Auth = "https://accounts.google.com/o/oauth2/auth", Token = "https://www.googleapis.com/oauth2/v3/token", ConfigPrefix = "Google", Scopes = "https://www.googleapis.com/auth/userinfo.profile https://www.googleapis.com/auth/userinfo.email" };
        public static readonly Endpoint GitHub = new Endpoint { Auth = "https://github.com/login/oauth/authorize", Token = "https://github.com/login/oauth/access_token", ConfigPrefix = "GitHub", Scopes = "" };
        public static readonly Endpoint Facebook = new Endpoint { Auth = "https://www.facebook.com/dialog/oauth", Token = "https://graph.facebook.com/oauth/access_token", ConfigPrefix = "Facebook", Scopes = "public_profile email" };

        public static string OAuth2Url(this HtmlHelper html, Endpoint endpoint)
        {
            return string.Format("{0}?client_id={1}&redirect_uri={2}&scope={3}&response_type=code", endpoint.Auth, Uri.EscapeDataString(endpoint.ClientId), Uri.EscapeDataString(endpoint.RedirectUrl), Uri.EscapeDataString(endpoint.Scopes));
        }

        internal class TokenResponse
        {
            [JsonProperty(PropertyName = "access_token", Required = Required.Always)]
            public string AccessToken { get; set; }
        }

        public static async Task<ActionResult> OAuth2LoginAsync(this ArticlesController controller, Endpoint endpoint, Func<HttpClient, string, Task<ActionResult>> login)
        {
            string code = controller.Request.QueryString["code"];

            if (string.IsNullOrEmpty(code))
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "TheDailyWTF");
                client.DefaultRequestHeaders.Add("Accept", "application/json");

                string accessToken;
                using (var response = await client.PostAsync(endpoint.Token, new FormUrlEncodedContent(new Dictionary<string, string>()
                    {
                        { "client_id", endpoint.ClientId },
                        { "client_secret", endpoint.Secret },
                        { "code", code },
                        { "grant_type", "authorization_code" },
                        { "redirect_uri", endpoint.RedirectUrl }
                    })))
                {
                    response.EnsureSuccessStatusCode();

                    var content = await response.Content.ReadAsStringAsync();

                    if (content[0] != '{')
                    {
                        // work around a bug in Facebook: http://stackoverflow.com/a/10351702
                        accessToken = HttpUtility.ParseQueryString(content)["access_token"];
                    }
                    else
                    {
                        accessToken = JsonConvert.DeserializeObject<TokenResponse>(content).AccessToken;
                    }
                }

                return await login(client, accessToken);
            }
        }
    }
}