using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.IO;

using WebView2 = Microsoft.Web.WebView2.WinForms.WebView2;
using System.Net;


namespace AutomationTools
{
    class AuthAndGetTokensModule
    {
        static public string    AccessToken { get; private set; }

        static private string   _RedirectUri = "https://docs.sgnl.pro/callback";
        static private string   _ClientID = "docs_web";
        static private string   _Scope = "offline_access cl.front cl.sys hub.r hub.e doc.c doc.r doc.u doc.d doc.e doc.x";

        static private string   _Code = string.Empty;

        static private string   _AuthUrl = string.Empty;
        static private string   _CodeVerifier = string.Empty;

        static private Form     _WebView2Form;
        static private WebView2 _WebView2;

        static private string   _AutomationToolsRootPath;

        static public void AuthAndGetTokensModuleMain(string AutomationToolsRootPath)
        {
            _AutomationToolsRootPath = AutomationToolsRootPath;

            // Check if the refresh token file exists
            if (File.Exists($"{_AutomationToolsRootPath}\\RefreshToken.txt") && !File.ReadAllText($"{_AutomationToolsRootPath}\\RefreshToken.txt").Equals(""))
                RefreshToken();
            else
                AuthAndGetTokens(); 
        }

        static private void RefreshToken()
        {
            // Read the refresh token from the file
            string RefreshToken = File.ReadAllText($"{_AutomationToolsRootPath}\\RefreshToken.txt");

            // Build the request URL to exchange the refresh token for a new access token
            var ContentPairs = new Dictionary<string, string>
            {
                { "grant_type",     "refresh_token" },
                { "client_id",      _ClientID },
                { "refresh_token",  RefreshToken },
                { "scope",          _Scope }
            };

            SendRequestAndUpdateTokens(ContentPairs);
        }

        static private void AuthAndGetTokens()
        {
            // Generate CodeVerifier and CodeChallenge for PKCE
            string CodeChallenge = string.Empty;
            CryptoHelper.GenerateCodes(ref _CodeVerifier, ref CodeChallenge);

            // Build the authorization URL
            _AuthUrl = Defines.SignalDocsEndpoints["AuthEndpoint"] +
                       $"?client_id={_ClientID}" +
                       $"&redirect_uri={Uri.EscapeDataString(_RedirectUri)}" +
                       $"&response_type=code" +
                       $"&scope={Uri.EscapeDataString(_Scope)}" +
                       $"&state={_CodeVerifier}" +
                       $"&code_challenge={CodeChallenge}" +
                       $"&code_challenge_method=S256";

            // Create a WebView2 form for user authentication
            _WebView2Form = new Form()
            {
                Width = 700,
                Height = 800,
                Text = "Auth in Signal Docs"
            };

            _WebView2 = new WebView2()
            {
                Dock = DockStyle.Fill
            };

            _WebView2Form.Controls.Add(_WebView2);

            // Handle the form's HandleCreated event to initialize WebView2
            _WebView2Form.HandleCreated += WebView2FormHandleCreatedCallback;

            // Start Auth form and wait
            Application.Run(_WebView2Form);
        }

        static private void SendRequestAndUpdateTokens(Dictionary<string, string> ContentPairs)
        {
            // Send the request to exchange the refresh token for a new access token (It doesn't make sense to use asynchronous functionality here)
            string Response = HttpService.POSTSync(Defines.SignalDocsEndpoints["TokenEndpoint"], 
                                                    new Dictionary<string, string>(), 
                                                    ContentPairs, 
                                                    "URLEncoded");

            if (HttpService.StatusCode != HttpStatusCode.OK)
            {
                Console.WriteLine("Ошибка авторизации Signal Docs, прерывание...");
                return;
            }

            // Deserialize the response to get the access token and refresh token
            JObject JResult = JsonConvert.DeserializeObject<JObject>(Response);

            // Update the access token and refresh token
            AccessToken = JResult["access_token"]?.ToString();
            string RefreshToken = JResult["refresh_token"]?.ToString();

            // Save the new refresh token to a file
            try
            {
                FileStream RefreshTokenFileStream = System.IO.File.Create($"{_AutomationToolsRootPath}\\RefreshToken.txt");
                RefreshTokenFileStream.Write(Encoding.UTF8.GetBytes(RefreshToken), 0, Encoding.UTF8.GetBytes(RefreshToken).Length);
            }
            catch (Exception ex) { Console.WriteLine(ex.Message); }
            
        }

        static private async void WebView2FormHandleCreatedCallback(object Sender, EventArgs EventArgs) {
            try
            {
                // Initialize WebView2
                await _WebView2.EnsureCoreWebView2Async(null);

                // Catching the navigation event in WebView
                _WebView2.NavigationStarting += WebView2NavigationStartingCallback;

                // Navigate to the authorization URL
                _WebView2.Source = new Uri(_AuthUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Критическая ошибка инициализации:\n{ex.Message}\n\nStackTrace:\n{ex.StackTrace}");
                _WebView2Form.Close();
            }
        }

        static private void WebView2NavigationStartingCallback(object Sender, CoreWebView2NavigationStartingEventArgs EventArgs)
        {
            if(EventArgs.Uri.StartsWith(_RedirectUri))
            {
                EventArgs.Cancel = true;

                var Uri = new Uri(EventArgs.Uri);

                // Close the WebView2 form
                Application.ExitThread();

                // Get code from the query string
                _Code = Uri.Query.TrimStart('?')
                            .Split('&')
                            .FirstOrDefault(p => p.StartsWith("code="))?.Split('=')[1];

                // Build the request URL to exchange the authorization code for tokens
                var ContentPairs = new Dictionary<string, string>
                {
                    { "grant_type",     "authorization_code" },
                    { "client_id",      _ClientID },
                    { "redirect_uri",   _RedirectUri },
                    { "code",           _Code },
                    { "code_verifier",  _CodeVerifier }
                };

                SendRequestAndUpdateTokens(ContentPairs);
            }
        }
    }

    class CryptoHelper
    {
        static public void GenerateCodes(ref string CodeVerifier, ref string CodeChallenge)
        {
            var RandomBytes = new byte[32];
            RandomNumberGenerator.Create().GetBytes(RandomBytes);

            CodeVerifier = Base64UrlEncode(RandomBytes);
            CodeChallenge = Base64UrlEncode(SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(CodeVerifier)));
        }

        static public string Base64UrlEncode(byte[] Input)
        {
            return Convert
                .ToBase64String(Input)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }
    }
}
