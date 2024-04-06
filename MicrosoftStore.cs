using System;
using System.Net.Http;
using System.Xml;
using System.Globalization;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Collections;
using Windows.Management.Deployment;
using Windows.ApplicationModel.Activation;
using System.Security.Principal;
using Windows.ApplicationModel;
using System.Net;
using System.IO;


class MicrosoftStore
{
    readonly WebClient _webClient = new();

    readonly PackageManager _packageManager = new();

    readonly string userSecurityId = WindowsIdentity.GetCurrent().Owner.ToString();

    readonly JavaScriptSerializer _javaScriptSerializer = new();

    readonly Resources _resources = new();

    readonly string requestUri = $"https://storeedgefd.dsx.mp.microsoft.com/v9.0/products/\0?market={CultureInfo.InstalledUICulture.Name.Remove(0, 3).ToUpper()}&locale={CultureInfo.InstalledUICulture.Name.ToLower()}&deviceFamily=Windows.Desktop";

    HttpResponseMessage _httpResponseMessage = null;

    HttpContent _httpContent = null;

    readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        UseCookies = true
    });

    readonly XmlDocument _xmlDocument = new();

    readonly string _cookie = null;

    public MicrosoftStore()
    {
        _httpContent = new StringContent(_resources.GetCookie);
        _httpContent.Headers.ContentType = new("application/soap+xml");
        _httpResponseMessage = _httpClient.PostAsync("https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx", _httpContent).Result;

        _xmlDocument.LoadXml(_httpResponseMessage.Content.ReadAsStringAsync().Result);
        _cookie = _xmlDocument.GetElementsByTagName("EncryptedData")[0].InnerText;

        _httpContent.Dispose();
        _httpResponseMessage.Dispose();
    }

    ~MicrosoftStore()
    {
        _httpClient.Dispose();
        _webClient.Dispose();
    }

    private string GetCategoryID(string appId)
    {
        _httpResponseMessage = _httpClient.GetAsync(requestUri.Replace("\0", appId)).Result;

        string input = _httpResponseMessage.Content.ReadAsStringAsync().Result;
        Dictionary<string, object> keyValuePairs = _javaScriptSerializer.Deserialize<Dictionary<string, object>>(input);
        string categoryId = _javaScriptSerializer.Deserialize<Dictionary<string, string>>(
            (((keyValuePairs["Payload"] as Dictionary<string, object>)["Skus"] as ArrayList)[0] as Dictionary<string, object>)["FulfillmentData"] as string)
            ["WuCategoryId"];

        _httpResponseMessage.Dispose();
        return categoryId;
    }

    private string WUIDRequest(string appId)
    {
        _httpContent = new StringContent(_resources.WUIDRequest.Replace("{1}", _cookie).Replace("{2}", GetCategoryID(appId)).Replace("{3}", "retail"));
        _httpContent.Headers.ContentType = new("application/soap+xml");
        _httpResponseMessage = _httpClient.PostAsync("https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx", _httpContent).Result;
        string result = _httpResponseMessage.Content.ReadAsStringAsync().Result;
        _httpContent.Dispose();
        _httpResponseMessage.Dispose();
        return result;
    }

    public Dictionary<string, Tuple<string, string, bool>> GetPackages(string appId)
    {
        List<string> packageFullNames = [];
        foreach (Package package in _packageManager.FindPackagesForUser(userSecurityId))
            packageFullNames.Add(package.Id.FullName);

        XmlDocument updateInfo = new();
        _xmlDocument.LoadXml(WUIDRequest(appId).Replace("&gt;", ">").Replace("&lt;", "<"));

        Dictionary<string, Tuple<string, string, bool>> packages = [];

        foreach (XmlNode xmlNode in _xmlDocument["s:Envelope"]["s:Body"]["SyncUpdatesResponse"]["SyncUpdatesResult"]["NewUpdates"])
        {
            updateInfo.LoadXml(xmlNode["Xml"].OuterXml);
            if (updateInfo["Xml"]["Properties"].GetElementsByTagName("SecuredFragment").Count == 0)
                continue;

            string packageMoniker = updateInfo["Xml"]["ApplicabilityRules"]["Metadata"]["AppxPackageMetadata"]["AppxMetadata"].GetAttribute("PackageMoniker");
            if (packageFullNames.Contains(packageMoniker))
                continue;
            packages.Add(packageMoniker, new(
                updateInfo["Xml"]["UpdateIdentity"].GetAttribute("UpdateID"),
                updateInfo["Xml"]["UpdateIdentity"].GetAttribute("RevisionNumber"),
                !string.IsNullOrEmpty(updateInfo["Xml"]["Properties"].GetAttribute("IsAppxFramework"))));
        }

        return packages;
    }


    public string GetPackageUrl(Tuple<string, string, bool> package)
    {
        _httpContent = new StringContent(_resources.FE3FileURL.Replace("{1}", package.Item1).Replace("{2}", package.Item2).Replace("{3}", "retail"));
        _httpContent.Headers.ContentType = new("application/soap+xml");
        _httpResponseMessage = _httpClient.PostAsync("https://fe3.delivery.mp.microsoft.com/ClientWebService/client.asmx/secured", _httpContent).Result;
        _xmlDocument.LoadXml(_httpResponseMessage.Content.ReadAsStringAsync().Result);
        string url = null;
        foreach (XmlNode xmlNode in _xmlDocument.GetElementsByTagName("Url"))
            if (xmlNode.InnerText.StartsWith("http://tlu.dl.delivery.mp.microsoft.com"))
            {
                url = xmlNode.InnerText;
                break;
            }
        _httpContent.Dispose();
        _httpResponseMessage.Dispose();

        return url;
    }

    public void InstallPackages(Dictionary<string, Tuple<string, string, bool>> packages)
    {
        foreach (KeyValuePair<string, Tuple<string, string, bool>> package in packages)
            if (package.Value.Item3)
            {
                Console.WriteLine(package.Key);
                string fileName = Path.GetTempFileName();
                _webClient.DownloadFile(GetPackageUrl(package.Value), fileName);
                _packageManager.AddPackageAsync(new(fileName), null, DeploymentOptions.ForceTargetApplicationShutdown).GetResults();
            }

        foreach (KeyValuePair<string, Tuple<string, string, bool>> package in packages)
            if (!package.Value.Item3)
            {
                Console.WriteLine(package.Key);
                string fileName = Path.GetTempFileName();
                _webClient.DownloadFile(GetPackageUrl(package.Value), fileName);
                _packageManager.AddPackageAsync(new(fileName), null, DeploymentOptions.ForceTargetApplicationShutdown).GetResults();
            }
    }
}