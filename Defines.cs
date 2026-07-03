using System.Collections.Generic;

namespace AutomationTools
{
    class Defines
    {
        public enum Status
        {
            Success,

            Error,
            ViewNotFoundError,
            ViewNotValidForElementIterationError,
            ExportViewError,
        }

        public static Dictionary<string, string> SignalDocsEndpoints = new Dictionary<string, string>()
        {
            {   "TokenEndpoint", "https://hub.sgnl.pro/connect/token"     },
            {   "AuthEndpoint", "https://hub.sgnl.pro/connect/authorize"  }
        };
    }
}
