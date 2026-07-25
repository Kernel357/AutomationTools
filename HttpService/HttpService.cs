using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace AutomationTools
{
    static public class HttpService
    {
        static private HttpClient _HttpServiceInstance = new HttpClient()
        {
            Timeout = TimeSpan.FromMinutes(10)
        };

        static public HttpStatusCode    StatusCode          { get; private set; }
        static public string            StatusMessage       { get; private set; }

        // Methods of content generation
        static public StringContent GetJSONContent(Object Content) 
        { 
            return new StringContent(JsonConvert.SerializeObject(Content), 
                                     Encoding.UTF8, 
                                     "application/json");
        }

        static public StringContent GetURLEncodedContent(Object Content) 
        { 
            return new StringContent(new FormUrlEncodedContent((Dictionary<string, string>)Content).ReadAsStringAsync().Result,
                                     Encoding.UTF8,
                                     "application/x-www-form-urlencoded"); 
        }

        static public StreamContent GetBinaryContent(Object Content) 
        { 
            return new StreamContent((FileStream)Content); 
        }

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

            return SendRequest(Request);
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

            Request.Content = (HttpContent)Defines.ContentType[ContentType].Item1(Content);
            Request.Content.Headers.ContentType = new MediaTypeHeaderValue(Defines.ContentType[ContentType].Item2);

            return SendRequest(Request);
        }

        public static string PUTSync(string URL,
                                     Dictionary<string, string> Headers,
                                     Object Content,
                                     string ContentType,
                                     long ContentLength)
        {
            var Request = new HttpRequestMessage()
            {
                Method = HttpMethod.Put,
                RequestUri = new Uri(URL),
            };

            foreach (var Header in Headers)
                Request.Headers.Add(Header.Key, Header.Value);

            Request.Content = (HttpContent)Defines.ContentType[ContentType].Item1(Content);
            Request.Content.Headers.ContentType = new MediaTypeHeaderValue(Defines.ContentType[ContentType].Item2);

            if(ContentLength > 0)
                Request.Content.Headers.ContentLength = ContentLength;

            return SendRequest(Request);
        }

        public static string PATCHSync(string URL,
                                       Dictionary<string, string> Headers,
                                       Object Content,
                                       string ContentType)
        {
            var Request = new HttpRequestMessage()
            {
                Method = new HttpMethod("PATCH"),
                RequestUri = new Uri(URL),
            };

            foreach (var Header in Headers)
                Request.Headers.Add(Header.Key, Header.Value);

            Request.Content = (HttpContent)Defines.ContentType[ContentType].Item1(Content);
            Request.Content.Headers.ContentType = new MediaTypeHeaderValue(Defines.ContentType[ContentType].Item2);

            return SendRequest(Request);
        }

        private static string SendRequest(HttpRequestMessage Request)
        {
            HttpResponseMessage Result = null;
            for (int Attempt = 1; Attempt <= 3; Attempt++)
            {
                try
                {
                    Result = _HttpServiceInstance.SendAsync(Request).Result;
                    break;
                }
                catch (Exception ex)
                {
                    if (Attempt < 3)
                    {
                        LoggingSystem.WriteLine("[-] Произошла ошибка при выполнении сетевого запроса... Повторим через 3 сек");
                        Thread.Sleep(3000);
                        continue;
                    }
                    else
                    {
                        LoggingSystem.WriteLine($"[-] Критическая ошибка при выполнении сетевого запроса: {ex.Message}");
                        StatusCode = HttpStatusCode.InternalServerError;

                        return null;
                    }
                }
            }

            StatusCode = Result.StatusCode;
            StatusMessage = Result.ReasonPhrase;

            return Result.Content.ReadAsStringAsync().Result;
        }
    }
}
