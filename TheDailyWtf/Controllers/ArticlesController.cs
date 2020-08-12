﻿using CommonMark;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;
using TheDailyWtf.Data;
using TheDailyWtf.Forum;
using TheDailyWtf.Legacy;
using TheDailyWtf.Models;
using TheDailyWtf.ViewModels;

namespace TheDailyWtf.Controllers
{
    public class ArticlesController : WtfControllerBase
    {
        public static readonly TimeSpan CommentEditTimeout = TimeSpan.FromDays(2);

        //
        // GET: /Articles/

        [OutputCache(CacheProfile = CacheProfile.Timed1Minute)]
        public ActionResult Index()
        {
            return View(new ArticlesIndexViewModel());
        }

        [OutputCache(CacheProfile = CacheProfile.Timed5Minutes)]
        public ActionResult ViewArticleById(int id)
        {
            var article = ArticleModel.GetArticleById(id);
            if (article == null)
                return HttpNotFound();

            return Redirect(article.Url);
        }

        [OutputCache(CacheProfile = CacheProfile.Timed1Minute)]
        public ActionResult ViewArticle(string articleSlug)
        {
            var article = ArticleModel.GetArticleBySlug(articleSlug);
            if (article == null)
                return HttpNotFound();

            return View(new ViewArticleViewModel(article));
        }

        [OutputCache(CacheProfile = CacheProfile.Timed1Minute)]
        public ActionResult ViewArticleComments(string articleSlug, int page, int? parent)
        {
            var article = ArticleModel.GetArticleBySlug(articleSlug);
            if (article == null)
                return HttpNotFound();

            if (parent.HasValue)
            {
                return View(new ViewCommentsViewModel(article, page) { Comment = { Parent = parent } });
            }

            return View(new ViewCommentsViewModel(article, page));
        }

        [RequireHttps]
        public ActionResult Login()
        {
            string name = null;
            string token = null;
            var cookie = Request.Cookies["tdwtf_token"];
            if (cookie != null)
            {
                try
                {
                    var ticket = FormsAuthentication.Decrypt(cookie.Value);
                    if (!ticket.Expired)
                    {
                        name = ticket.Name;
                        token = ticket.UserData;
                    }
                }
                catch (HttpException)
                {
                    // cookie was invalid, ignore it.
                }
            }
            return View(new CommentsLoginViewModel(name, token));
        }

        private ActionResult SetLoginCookie(string name, string token)
        {
            var issued = DateTime.Now;
            var expiration = issued.AddYears(2);
            var ticket = new FormsAuthenticationTicket(1, name, issued, expiration, true, token);

            Response.SetCookie(new HttpCookie("tdwtf_token", FormsAuthentication.Encrypt(ticket))
            {
                HttpOnly = true,
                Expires = expiration,
                Path = FormsAuthentication.FormsCookiePath
            });
            Response.SetCookie(new HttpCookie("tdwtf_token_name", name)
            {
                HttpOnly = false,
                Expires = expiration,
                Path = FormsAuthentication.FormsCookiePath
            });

            // Log out of the admin panel to avoid confusion between accounts. Leave IS_ADMIN set.
            FormsAuthentication.SignOut();

            return Redirect("/login");
        }

        [RequireHttps]
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult Login(string mode)
        {
            if (mode == "logout")
            {
                var expiration = DateTime.Today.AddDays(-1);
                Response.SetCookie(new HttpCookie("tdwtf_token", "")
                {
                    HttpOnly = true,
                    Expires = expiration,
                    Path = FormsAuthentication.FormsCookiePath,
                });
                Response.SetCookie(new HttpCookie("tdwtf_token_name", "")
                {
                    HttpOnly = false,
                    Expires = expiration,
                    Path = FormsAuthentication.FormsCookiePath,
                });
                return Redirect("/login");
            }

            if (mode == "login")
            {
                return Redirect(NodeBBCustomAuth.GenerateAuthUrl(this.HttpContext));
            }

            return Redirect("/login");
        }

        public ActionResult LoginNodeBB()
        {
            if (Request.QueryString["token"] == null)
            {
                return Redirect("/login");
            }

            var result = NodeBBCustomAuth.VerifyAuth(this.HttpContext);
            return SetLoginCookie(result.Name, result.Token);
        }

        class GoogleUser
        {
            [JsonProperty(PropertyName = "email", Required = Required.Always)]
            public string Email { get; set; }
            [JsonProperty(PropertyName = "name", Required = Required.Always)]
            public string Name { get; set; }
        }

        public Task<ActionResult> LoginGoogle()
        {
            return this.OAuth2LoginAsync(OAuth2.Google, async (client, token) =>
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
                var user = JsonConvert.DeserializeObject<GoogleUser>(await client.GetStringAsync("https://www.googleapis.com/oauth2/v2/userinfo"));
                return SetLoginCookie(user.Name, "google:" + user.Email);
            });
        }

        class GitHubUser
        {
            [JsonProperty(PropertyName = "login", Required = Required.Always)]
            public string Login { get; set; }
            [JsonProperty(PropertyName = "name", Required = Required.AllowNull)]
            public string Name { get; set; }
        }

        public Task<ActionResult> LoginGitHub()
        {
            return this.OAuth2LoginAsync(OAuth2.GitHub, async (client, token) =>
            {
                client.DefaultRequestHeaders.Add("Authorization", "token " + token);
                var user = JsonConvert.DeserializeObject<GitHubUser>(await client.GetStringAsync("https://api.github.com/user"));
                return SetLoginCookie(user.Name ?? user.Login, "github:" + user.Login);
            });
        }

        class FacebookUser
        {
            [JsonProperty(PropertyName = "email", Required = Required.Always)]
            public string Email { get; set; }
            [JsonProperty(PropertyName = "name", Required = Required.Always)]
            public string Name { get; set; }
        }

        public Task<ActionResult> LoginFacebook()
        {
            return this.OAuth2LoginAsync(OAuth2.Facebook, async (client, token) =>
            {
                client.DefaultRequestHeaders.Add("Authorization", "OAuth " + token);
                var user = JsonConvert.DeserializeObject<FacebookUser>(await client.GetStringAsync("https://graph.facebook.com/me?fields=name,email"));
                return SetLoginCookie(user.Name, "facebook:" + user.Email);
            });
        }

        // not cached
        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> ViewArticleComments(string articleSlug, int page, CommentFormModel form)
        {
            var article = ArticleModel.GetArticleBySlug(articleSlug);
            if (article == null)
                return HttpNotFound();

            string token = null;
            var cookie = Request.Cookies["tdwtf_token"];
            if (cookie != null)
            {
                try
                {
                    var ticket = FormsAuthentication.Decrypt(cookie.Value);
                    if (!ticket.Expired)
                    {
                        form.Name = ticket.Name;
                        token = ticket.UserData;
                    }
                }
                catch (HttpException)
                {
                    // cookie was invalid, redirect to login page
                    return Redirect("/login");
                }
            }

            if (token == null)
            {
                await this.CheckRecaptchaAsync();
            }

            var ip = Request.ServerVariables["REMOTE_ADDR"];

            if (string.IsNullOrWhiteSpace(form.Name))
                ModelState.AddModelError(string.Empty, "A name is required.");
            if (string.IsNullOrWhiteSpace(form.Body))
                ModelState.AddModelError(string.Empty, "A comment is required.");
            if (form.Parent.HasValue && CommentModel.GetCommentById(form.Parent.Value) == null)
                ModelState.AddModelError(string.Empty, "Invalid parent comment.");
            if (form.Body.Length > CommentFormModel.MaxBodyLength)
                ModelState.AddModelError(string.Empty, "Comment too long.");
            if (ModelState.IsValid)
            {
                var containsLinks = CommonMarkConverter.Parse(form.Body).AsEnumerable().Any(n => n.Inline?.Tag == CommonMark.Syntax.InlineTag.Link || n.Inline?.Tag == CommonMark.Syntax.InlineTag.Image || n.Inline?.Tag == CommonMark.Syntax.InlineTag.RawHtml || n.Block?.Tag == CommonMark.Syntax.BlockTag.HtmlBlock);
                var shouldHide = containsLinks || DB.Comments_UserHasApprovedComment(ip, token) != true;

                int commentId = DB.Comments_CreateOrUpdateComment(
                    Article_Id: article.Id, 
                    Body_Html: form.Body,
                    User_Name: form.Name,
                    Posted_Date: DateTime.Now,
                    User_IP: ip,
                    User_Token: token,
                    Parent_Comment_Id: form.Parent,
                    Hidden_Indicator: shouldHide
                ).Value;

                return Redirect(string.Format("{0}/{1}#comment-{2}", article.CommentsUrl, article.CachedCommentCount / ViewCommentsViewModel.CommentsPerPage + 1, commentId));
            }

            return View(new ViewCommentsViewModel(article, page) { Comment = form });
        }

        public ActionResult Addendum(string articleSlug, int id)
        {
            var article = ArticleModel.GetArticleBySlug(articleSlug);
            if (article == null)
                return HttpNotFound();

            var comment = CommentModel.GetCommentById(id);
            if (comment == null || comment.ArticleId != article.Id)
                return HttpNotFound();

            if (comment.UserToken == null || comment.PublishedDate.Add(CommentEditTimeout) <= DateTime.Now)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            var cookie = Request.Cookies["tdwtf_token"];
            if (cookie == null)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            try
            {
                var ticket = FormsAuthentication.Decrypt(cookie.Value);
                if (ticket.Expired || comment.UserToken != ticket.UserData)
                    return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
            }
            catch (HttpException)
            {
                // cookie was invalid, redirect to login
                return Redirect("/login");
            }

            return View(new AddendumViewModel(article, comment));
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public ActionResult Addendum(string articleSlug, int id, CommentFormModel post)
        {
            var article = ArticleModel.GetArticleBySlug(articleSlug);
            if (article == null)
                return HttpNotFound();

            if (string.IsNullOrWhiteSpace(post.Body))
                return Redirect(article.Url);

            var comment = CommentModel.GetCommentById(id);
            if (comment == null || comment.ArticleId != article.Id)
                return HttpNotFound();

            if (comment.UserToken == null || comment.PublishedDate.Add(CommentEditTimeout) <= DateTime.Now)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            var cookie = Request.Cookies["tdwtf_token"];
            if (cookie == null)
                return new HttpStatusCodeResult(HttpStatusCode.Forbidden);

            try
            {
                var ticket = FormsAuthentication.Decrypt(cookie.Value);
                if (ticket.Expired || comment.UserToken != ticket.UserData)
                    return new HttpStatusCodeResult(HttpStatusCode.Forbidden);
            }
            catch (HttpException)
            {
                return Redirect("/login");
            }

            var addendumModel = new AddendumViewModel(article, comment) { Body = post.Body };
            if (post.Body.Length > addendumModel.MaxBodyLength)
                ModelState.AddModelError(string.Empty, "Comment too long.");
            if (ModelState.IsValid)
            {
                DB.Comments_CreateOrUpdateComment(
                    Comment_Id: comment.Id,
                    Article_Id: article.Id,
                    Body_Html: $"{comment.BodyRaw}\n\n**Addendum {DateTime.Now}:**\n{post.Body}",
                    User_Name: comment.Username,
                    Posted_Date: comment.PublishedDate,
                    User_IP: comment.UserIP,
                    User_Token: comment.UserToken,
                    Parent_Comment_Id: comment.ParentCommentId
                );

                return Redirect(article.Url);
            }

            return View(addendumModel);
        }

        public ActionResult ViewLegacyArticle(string articleSlug)
        {
            return RedirectToActionPermanent("ViewArticle", new { articleSlug });
        }

        [OutputCache(CacheProfile = CacheProfile.Timed5Minutes)]
        public ActionResult ViewLegacyPost(int? postId)
        {
            if (postId == null)
                return HttpNotFound();

            var article = ArticleModel.GetArticleByLegacyPost((int)postId);
            if (article == null)
                return HttpNotFound();

            return RedirectToActionPermanent("ViewArticle", new { articleSlug = article.Slug });
        }

        [OutputCache(CacheProfile = CacheProfile.Timed5Minutes)]
        public ActionResult ViewLegacyAttachment(int? postId)
        {
            if (postId == null)
                return HttpNotFound();

            var article = ArticleModel.GetArticleByLegacyPost((int)postId);
            if (article == null)
                return RedirectPermanent(string.Format("https://{0}/forums/{1}/PostAttachment.aspx", Config.NodeBB.Host, postId));

            return RedirectToActionPermanent("ViewArticle", new { articleSlug = article.Slug });
        }

        public ActionResult ViewLegacyArticleComments(string articleSlug)
        {
            return RedirectToActionPermanent("ViewArticleComments", new { articleSlug });
        }

        [OutputCache(CacheProfile = CacheProfile.Timed1Minute)]
        public ActionResult ViewArticlesByMonth(int year, int month)
        {
            var date = new DateTime(year, month, 1);
            return View(Views.Articles.Index, new ArticlesIndexViewModel() { ReferenceDate = new ArticlesIndexViewModel.DateInfo(date) });
        }

        public ActionResult ViewLegacySeries(string legacySeries)
        {
            var legacyPart = LegacyEncodedUrlPart.CreateFromEncodedUrl(legacySeries);

            SeriesModel series;
            if (SeriesModel.LegacySeriesMap.TryGetValue(legacyPart.DecodedValue, out series))
                return RedirectToActionPermanent("ViewArticlesBySeries", new { seriesSlug = series.Slug });

            return HttpNotFound();
        }

        [OutputCache(CacheProfile = CacheProfile.Timed5Minutes)]
        public ActionResult ViewArticlesBySeries(string seriesSlug)
        {
            var series = SeriesModel.GetSeriesBySlug(seriesSlug);
            if (series == null)
                return HttpNotFound();

            return View(Views.Articles.Index, new ArticlesIndexViewModel(series));
        }

        [OutputCache(CacheProfile = CacheProfile.Timed5Minutes)]
        public ActionResult ViewArticlesBySeriesAndMonth(int year, int month, string seriesSlug)
        {
            var date = new DateTime(year, month, 1);
            var series = SeriesModel.GetSeriesBySlug(seriesSlug);
            if (series == null)
                return HttpNotFound();

            return View(Views.Articles.Index, new ArticlesIndexViewModel(series, date));
        }

        public ActionResult RandomArticle()
        {
            var article = this.GetRandomArticleInternal();
            return RedirectToAction("ViewArticle", new { articleSlug = article.Article_Slug });
        }
    }
}