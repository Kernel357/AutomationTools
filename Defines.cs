using System;
using System.Collections.Generic;

namespace AutomationTools
{
    public class Defines
    {
        public enum Status
        {
            Success,

            // DEFAULT
            Error,
            FileDoesNotExistError,


            // Signal Docs Error
            AuthError,
            GetTokenError,
            RefreshTokenError,

            CompanyWasNotFoundError,
            ProjectWasNotFoundError,
            ProjectTreeIsCorrupted,
            FileWasNotFoundError,
            FollowPathError,
            AutodeskForgeProcessingError,
            UploadFileError,

            // Export errors
            ViewNotFoundError,
            ViewNotValidForElementIterationError,
            ExportViewError,
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
