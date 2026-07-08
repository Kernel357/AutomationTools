using System;
using System.Collections.Generic;
using static System.Net.WebRequestMethods;

namespace AutomationTools
{
    public class Defines
    {
        public enum Status
        {
            Success,

            Error,
            ViewNotFoundError,
            ViewNotValidForElementIterationError,
            ExportViewError,
        }

        public static Dictionary<string, (Func<Object, string>, string)> ContentType = new Dictionary<string, (Func<Object, string>, string)>()
        {
            {   "JSON",                 ( HttpService.GetJSONContent,           "application/json"                  )   },
            {   "URLEncoded",           ( HttpService.GetURLEncodedContent,     "application/x-www-form-urlencoded" )   }
        };

        public static Dictionary<string, string> SignalDocsEndpoints = new Dictionary<string, string>()
        {
            {   "TokenEndpoint",            "https://hub.sgnl.pro/connect/token"        },
            {   "AuthEndpoint",             "https://hub.sgnl.pro/connect/authorize"    },
            {   "HUBAPIV1BaseAddress",      "https://hub.sgnl.pro/api/v1"               },
            {   "DOCSPROAPIBaseAddress",    "https://docs.sgnl.pro/api"                 },
            {   "DOCSDEVAPIBaseAddress",    "https://docs.sgnl.dev/api"                 }
        };
    }
}
