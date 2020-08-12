﻿using System;
using System.Web;
using System.Web.Mvc;
using Inedo.Diagnostics;
using TheDailyWtf.Data;

namespace TheDailyWtf.Controllers
{
    public class AdsController : Controller
    {
        public ActionResult ViewAd(string id)
        {
            var ad = AdRotator.GetAdById(id);
            if (ad != null)
            {
                bool addImpression = this.Request.QueryString["noimpression"] == null;
                if (addImpression)
                    DB.AdImpressions_IncrementCount(ad.FileName, DateTime.Now.Date, 1);
                return File(ad.DiskPath, MimeMapping.GetMimeMapping(ad.DiskPath));
            }

            Logger.Error($"Invalid Ad attempted to be loaded from: /fblast/{id}");
            return HttpNotFound();
        }

        public ActionResult ClickAd(string redirectGuid)
        {
            var url = AdRotator.GetOriginalUrlByRedirectGuid(redirectGuid);
            if (url != null)
            {
                DB.AdRedirectUrls_IncrementClickCount(Guid.Parse(redirectGuid), 1);
                return Redirect(url);
            }

            Logger.Error($"Invalid Ad URL redirect GUID: {redirectGuid}");
            return HttpNotFound();
        }
    }
}
