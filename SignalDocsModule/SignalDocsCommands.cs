using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;

namespace AutomationTools
{
    static class SignalDocsCommands
    {
        public static Defines.Status UploadFilesToServer(string AccessToken)
        {
            return Defines.Status.Success;
        }

        public static Defines.Status StartAutodeskForgeProcessing(string AccessToken, 
                                                                    string CompanyName, 
                                                                    string ProjectName,
                                                                    string PathToNWD,
                                                                    string NameOfNWDItem)
        {
            // Get companies
            JArray Companies = SignalDocsAPI.GetCompanies(AccessToken);
            if (Companies == null || Companies.Count == 0)
                return Defines.Status.Error;
            
            string CompanyID = SignalDocsHelper.GetObjectID(Companies, CompanyName);

            // Get all projects
            JArray Projects = SignalDocsAPI.GetProjects(AccessToken, CompanyID);
            if (Projects == null)
                return Defines.Status.Error;

            string ProjectID = SignalDocsHelper.GetObjectID(Projects, ProjectName);

            // Get tree of project
            JObject Tree = SignalDocsAPI.GetProjectTree(AccessToken, ProjectID);
            if (Tree == null)
                return Defines.Status.Error;

            string LastFolderID = SignalDocsHelper.FollowPath(Tree, PathToNWD);

            // Search NWD item
            JObject NWDItem = SignalDocsAPI.GetNWDItemInFolder(AccessToken, LastFolderID, NameOfNWDItem);
            if(NWDItem == null)
                return Defines.Status.Error;

            // Send NWD items to Autodesk Forge processing
            if (SignalDocsAPI.SendNWDItemToAutodeskForgeProcessing(AccessToken, NWDItem) != Defines.Status.Success)
                return Defines.Status.Error;

            return Defines.Status.Success;
        }
    }

    static class SignalDocsAPI
    {
        public static JArray GetCompanies(string AccessToken)
        {
            string Response = HttpService.GETSync($"{Defines.SignalDocsEndpoints["HUBAPIV1BaseAddress"]}/hub/companies", 
                                                  new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } });

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            return JsonConvert.DeserializeObject<JArray>(Response);
        }

        public static JArray GetProjects(string AccessToken, string CompanyID)
        {
            string Response = HttpService.GETSync($"{Defines.SignalDocsEndpoints["HUBAPIV1BaseAddress"]}/hub/companies/{CompanyID}/projects",
                                                  new Dictionary<string, string> { { "Authorization", "Bearer " + AccessToken } });

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            return JsonConvert.DeserializeObject<JArray>(Response);
        }

        public static JObject GetProjectTree(string AccessToken, string ProjectID)
        {
            string Response = HttpService.GETSync($"{Defines.SignalDocsEndpoints["DOCSPROAPIBaseAddress"]}/folders/project/{ProjectID}/tree",
                                                  new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } });

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            return JsonConvert.DeserializeObject<JObject>(Response);
        }

        public static JObject GetNWDItemInFolder(string AccessToken, string FolderID, string NameOfNWDItem)
        {
            // Get number of items 
            JObject Content = new JObject()
            {
                { "folderId",   FolderID                            },
                { "deleted",    false                               },
            };

            string Response = HttpService.POSTSync($"{Defines.SignalDocsEndpoints["DOCSPROAPIBaseAddress"]}/items/find/count",
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            Content,
                                            "JSON");

            // Get all items in folder
            Content = new JObject
            {   
                { "folderId",   FolderID                                                            },
                { "deleted",    false                                                               },
                { "skip",       0                                                                   },
                { "take",       JObject.Parse(Response)["data"]                                     },
                { "orderBy",    new JObject(){ { "propertyName", "Name"}, { "direction", "Asc"} }   }
            };

            Response = HttpService.POSTSync($"{Defines.SignalDocsEndpoints["DOCSPROAPIBaseAddress"]}/items/find",
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            Content,
                                            "JSON");

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            // Search NWD item in folder
            foreach (var Item in JsonConvert.DeserializeObject<JArray>(Response))
                if (((string)Item["name"]).Equals(NameOfNWDItem))
                    return (JObject)Item;

            return null;
        }

        public static Defines.Status UploadFile(string AccessToken, string PathToFile)
        {
            return Defines.Status.Success;
        }

        public static Defines.Status SendNWDItemToAutodeskForgeProcessing(string AccessToken, JObject NWDItem)
        {
            string VersionID = (string)NWDItem["topVersion"]["versionId"];

            string Response = HttpService.GETSync($"https://docs.sgnl.pro/api/versions/{VersionID}/forge/convert",
                                                    new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } });

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return Defines.Status.Error;

            return Defines.Status.Success;
        }
    }

    static class SignalDocsHelper
    {
        public static string GetObjectID(JArray Objects, string ObjectName)
        {
            string ID = string.Empty;

            foreach (var Object in Objects)
            {
                if (((string)Object["name"]).Equals(ObjectName))
                {
                    ID = (string)Object["id"];
                    break;
                }
            }

            return ID;
        }

        public static (string, JObject) GetObjectAndID(JArray Objects, string ObjectName)
        {
            string ID = string.Empty;
            JObject JObject = null;

            foreach (var Object in Objects)
            {
                if (((string)Object["name"]).Equals(ObjectName))
                {
                    ID = (string)Object["id"];
                    JObject = (JObject)Object;
                    break;
                }
            }

            return (ID, JObject);
        }

        public static string FollowPath(JObject Tree, string Path)
        {
            JObject Parent = (JObject)Tree.Root;
            JArray Children = (JArray)Parent["children"];

            (string, JObject) SuperObject;

            foreach (string PartOfPath in Path.Split('\\'))
            {
                SuperObject = GetObjectAndID(Children, PartOfPath);

                if(!SuperObject.Item1.Equals(string.Empty))
                {
                    Parent = SuperObject.Item2;
                    Children = (JArray)Parent["children"];
                }
            }

            return (string)Parent["id"];
        }
    }
}
