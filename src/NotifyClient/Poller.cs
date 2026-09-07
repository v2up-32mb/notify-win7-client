using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

// Lightweight HTTPS poller, .NET 4.0 compatible, Win7 TLS1.2 enabled.
static class Poller
{
    static bool tlsInit = false;

    public static List<Msg> Fetch(string baseUrl, string secret, string since, out string latest, out string err)
    {
        latest = null; err = null;
        List<Msg> list = new List<Msg>(4);
        try
        {
            if (!tlsInit)
            {
                try
                {
                    // 3072 = TLS1.2 (avoid enum dep for old csc), 768 = TLS1.1
                    ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 768 | (int)SecurityProtocolType.Ssl3 | (int)SecurityProtocolType.Tls);
                }
                catch { }
                tlsInit = true;
            }
            string url = baseUrl.TrimEnd('/') + "/api/messages?limit=50" + (string.IsNullOrEmpty(since) ? "" : "&since=" + Uri.EscapeDataString(since));
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.UserAgent = "NotifyClient-Win7/1.0";
            req.KeepAlive = false;
            if (!string.IsNullOrEmpty(secret)) req.Headers["Authorization"] = "Bearer " + secret;
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
            {
                string json = sr.ReadToEnd();
                list = MiniJson.ParseMessages(json, out latest);
                return list;
            }
        }
        catch (WebException we)
        {
            try
            {
                HttpWebResponse r = we.Response as HttpWebResponse;
                if (r != null)
                {
                    using (StreamReader sr = new StreamReader(r.GetResponseStream()))
                    {
                        err = "HTTP " + (int)r.StatusCode + " " + sr.ReadToEnd();
                    }
                }
                else err = we.Message;
            }
            catch { err = we.Message; }
            return list;
        }
        catch (Exception ex) { err = ex.Message; return list; }
    }
}
