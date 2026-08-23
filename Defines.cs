using System;
using System.Collections.Generic;

namespace AutomationTools
{
    public class Defines
    {
        public enum Status
        {
            Success = 0,

            // DEFAULT
            Error = 10,
            FileDoesNotExistError = 11,


            // Signal Docs Error
            AuthError = 20,
            GetTokenError = 21,
            RefreshTokenError = 22,

            CompanyWasNotFoundError = 23,
            ProjectWasNotFoundError = 24,
            ProjectTreeIsCorrupted = 25,
            FileWasNotFoundError = 26,
            FollowPathError = 27,
            AutodeskForgeProcessingError = 28,
            UploadFileError = 29,

            // Export errors
            ViewNotFoundError = 30,
            GeometryNotFoundError = 31,
            ViewNotValidForElementIterationError = 32,
            ExportViewError = 33,
        }

        public static Dictionary<string, (Func<Object, Object>, string)> ContentType = new Dictionary<string, (Func<Object, Object>, string)>()
        {
            {   "JSON",                 ( HttpService.GetJSONContent,           "application/json"                  )   },
            {   "URLEncoded",           ( HttpService.GetURLEncodedContent,     "application/x-www-form-urlencoded" )   },
            {   "BINARY",               ( HttpService.GetBinaryContent,         "binary/octet-stream"               )   }
        };

        public static Dictionary<string, string> SignalDocsEndpoints = new Dictionary<string, string>()
        {
            {   "TokenEndpoint",            "https://hub.sgnl.pro/connect/token"        },
            {   "AuthEndpoint",             "https://hub.sgnl.pro/connect/authorize"    },
            {   "HUBPROBaseAddress",        "https://hub.sgnl.pro"                      },
            {   "DOCSPROBaseAddress",       "https://docs.sgnl.pro"                     },
            {   "DOCSDEVBaseAddress",       "https://docs.sgnl.dev"                     }
        };
    }
}
