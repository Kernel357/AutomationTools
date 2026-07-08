using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace AutomationTools
{
    static public class HttpService
    {
        static private HttpClient _HttpServiceInstance = new HttpClient();

        static public HttpStatusCode    StatusCode          { get; private set; }
        static public string            StatusMessage       { get; private set; }

        // Methods of content generation
        static public string GetJSONContent(Object Content) { return JsonConvert.SerializeObject(Content); }
        static public string GetURLEncodedContent(Object Content) { return new FormUrlEncodedContent((Dictionary<string, string>)Content).ReadAsStringAsync().Result; }

        // Request methods
        public static string GETSync(string URL,
                                    Dictionary<string, string> Headers)
        {
            var Request = new HttpRequestMessage()
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri(URL),
            };

            foreach (var Header in Headers)
                Request.Headers.Add(Header.Key, Header.Value);

            HttpResponseMessage Result = _HttpServiceInstance.SendAsync(Request).Result;

            StatusCode = Result.StatusCode;
            StatusMessage = Result.ReasonPhrase;

            return Result.Content.ReadAsStringAsync().Result;
        }

        public static string POSTSync(string URL,
                                     Dictionary<string, string> Headers,
                                     Object Content,
                                     string ContentType)
        {
            var Request = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(URL),
            };

            foreach (var Header in Headers)
                Request.Headers.Add(Header.Key, Header.Value);

            Request.Content = new StringContent(Defines.ContentType[ContentType].Item1(Content), Encoding.UTF8, Defines.ContentType[ContentType].Item2);
            Request.Content.Headers.ContentType = new MediaTypeHeaderValue(Defines.ContentType[ContentType].Item2);

            HttpResponseMessage Result = _HttpServiceInstance.SendAsync(Request).Result;

            StatusCode = Result.StatusCode;
            StatusMessage = Result.ReasonPhrase;

            return Result.Content.ReadAsStringAsync().Result;
        }
    }
}
