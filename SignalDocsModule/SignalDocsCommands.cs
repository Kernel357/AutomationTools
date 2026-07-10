using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace AutomationTools
{
    static class SignalDocsCommands
    {
        public static Defines.Status UploadFilesToServer(string AccessToken,
                                                         string CompanyName,
                                                         string ProjectName,
                                                         string PathToFileOnServer,
                                                         string PathToFileOnDisk,
                                                         string NameOfFile)
        {
            if (!File.Exists(Path.Combine(PathToFileOnDisk, NameOfFile)))
                return Defines.Status.FileDoesNotExistError;

            // Get companies
            JArray Companies = SignalDocsAPI.GetCompanies(AccessToken);
            if (Companies == null || Companies.Count == 0)
                return Defines.Status.CompanyWasNotFoundError;

            string CompanyID = SignalDocsHelper.GetObjectID(Companies, CompanyName);

            // Get all projects
            JArray Projects = SignalDocsAPI.GetProjects(AccessToken, CompanyID);
            if (Projects == null)
                return Defines.Status.ProjectWasNotFoundError;

            string ProjectID = SignalDocsHelper.GetObjectID(Projects, ProjectName);

            return SignalDocsAPI.UploadFile(AccessToken, CompanyID, ProjectID, PathToFileOnServer, PathToFileOnDisk, NameOfFile);
        }

        public static Defines.Status StartAutodeskForgeProcessing(string AccessToken, 
                                                                  string CompanyName, 
                                                                  string ProjectName,
                                                                  string PathToNWDOnServer,
                                                                  string NameOfNWDFile)
        {
            // Get companies
            JArray Companies = SignalDocsAPI.GetCompanies(AccessToken);
            if (Companies == null || Companies.Count == 0)
                return Defines.Status.CompanyWasNotFoundError;
            
            string CompanyID = SignalDocsHelper.GetObjectID(Companies, CompanyName);

            // Get all projects
            JArray Projects = SignalDocsAPI.GetProjects(AccessToken, CompanyID);
            if (Projects == null)
                return Defines.Status.ProjectWasNotFoundError;

            string ProjectID = SignalDocsHelper.GetObjectID(Projects, ProjectName);

            // Get tree of project
            JObject Tree = SignalDocsAPI.GetProjectTree(AccessToken, ProjectID);
            if (Tree == null)
                return Defines.Status.ProjectTreeIsCorrupted;

            // Follow path
            string LastFolderID = SignalDocsHelper.FollowPath(Tree, PathToNWDOnServer);
            if (LastFolderID == null)
                return Defines.Status.FollowPathError;

            // Search NWD file
            JObject NWDFile = SignalDocsAPI.GetFileInFolder(AccessToken, LastFolderID, NameOfNWDFile);
            if(NWDFile == null)
                return Defines.Status.FileWasNotFoundError;

            // Send NWD file to Autodesk Forge processing
            if (SignalDocsAPI.SendNWDItemToAutodeskForgeProcessing(AccessToken, NWDFile) != Defines.Status.Success)
                return Defines.Status.AutodeskForgeProcessingError;

            return Defines.Status.Success;
        }
    }

    static class SignalDocsAPI
    {
        public static JArray GetCompanies(string AccessToken)
        {
            string Response = HttpService.GETSync($"{Defines.SignalDocsEndpoints["HUBPROBaseAddress"]}/api/v1/hub/companies", 
                                                  new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } });

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            return JsonConvert.DeserializeObject<JArray>(Response);
        }

        public static JArray GetProjects(string AccessToken, string CompanyID)
        {
            string Response = HttpService.GETSync($"{Defines.SignalDocsEndpoints["HUBPROBaseAddress"]}/api/v1/hub/companies/{CompanyID}/projects",
                                                  new Dictionary<string, string> { { "Authorization", "Bearer " + AccessToken } });

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            return JsonConvert.DeserializeObject<JArray>(Response);
        }

        public static JObject GetProjectTree(string AccessToken, string ProjectID)
        {
            string Response = HttpService.GETSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/folders/project/{ProjectID}/tree",
                                                  new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } });

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            return JsonConvert.DeserializeObject<JObject>(Response);
        }

        public static JObject GetFileInFolder(string AccessToken, string FolderID, string NameOfFile)
        {
            // Get number of items 
            JObject Content = new JObject()
            {
                { "folderId",   FolderID },
                { "deleted",    false    },
            };

            string Response = HttpService.POSTSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/items/find/count",
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            Content,
                                            "JSON");

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            // Get all items in folder
            Content = new JObject
            {   
                { "folderId",   FolderID                                                            },
                { "deleted",    false                                                               },
                { "skip",       0                                                                   },
                { "take",       JObject.Parse(Response)["data"]                                     },
                { "orderBy",    new JObject(){ { "propertyName", "Name"}, { "direction", "Asc"} }   }
            };

            Response = HttpService.POSTSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/items/find",
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            Content,
                                            "JSON");

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return null;

            // Search file in folder
            foreach (var File in JsonConvert.DeserializeObject<JArray>(Response))
                if (((string)File["name"]).Equals(NameOfFile))
                    return (JObject)File;

            return null;
        }

        public static Defines.Status UploadFile(string AccessToken, string CompanyID, string ProjectID, string PathToFileOnServer, string PathToFileOnDisk, string NameOfFile)
        {
            FileStream UploadFileStream = System.IO.File.OpenRead(Path.Combine(PathToFileOnDisk, NameOfFile).ToString());

            // Get upload link for AWS S3 Cloud
            JObject Content = new JObject()
            {
                {"projectId",   ProjectID               },
                {"mimeType",    "binary/octet-stream"   },
                {"size",        UploadFileStream.Length }
            };

            string Response = HttpService.PUTSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/objects/project",
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            Content,
                                            "JSON",
                                            0);

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return Defines.Status.UploadFileError;


            // Upload file
            string SignedURL = JObject.Parse(Response)["signedUrl"].ToString();
            string ObjectID = JObject.Parse(Response)["objectId"].ToString();

            Response = HttpService.PUTSync(SignedURL,
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            UploadFileStream,
                                            "BINARY",
                                            UploadFileStream.Length);

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return Defines.Status.UploadFileError;


            // Commit
            Response = HttpService.POSTSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/objects/{ObjectID}/uploading/commit",
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            new JObject() { },
                                            "JSON");

            if (HttpService.StatusCode != HttpStatusCode.NoContent)
                return Defines.Status.UploadFileError;


            // Get version
            Content = new JObject()
            {
                {"name",        NameOfFile  },
                {"objectId",    ObjectID    },
                {"projectId",   ProjectID   }
            };

            Response = HttpService.PUTSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/versions",
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            Content,
                                            "JSON",
                                            0);

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return Defines.Status.UploadFileError;


            // Keep version for new file
            string FolderID = SignalDocsHelper.FollowPath(GetProjectTree(AccessToken, ProjectID), PathToFileOnServer);
            string VersionID = JObject.Parse(Response)["data"].ToString();

            if (FolderID == null)
                return Defines.Status.UploadFileError;

            // If the file already exists in the folder, update it
            JObject File = GetFileInFolder(AccessToken, FolderID, NameOfFile);

            if (File == null)
            {
                Content = new JObject()
                {
                    {"folderId",    FolderID        },
                    {"name",        NameOfFile      },
                    {"versionId",   VersionID       }
                };

                Response = HttpService.POSTSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/items/file",
                                                new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                                Content,
                                                "JSON");
            }
            else
            {
                Content = new JObject()
                {
                    {"targetItemId",    File["id"] },
                    {"sourceVersionId", VersionID  }
                };

                Response = HttpService.PUTSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/items/versions/file",
                                               new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                               Content,
                                               "JSON",
                                               0);
            }

            if (HttpService.StatusCode != HttpStatusCode.OK)
                return Defines.Status.UploadFileError;

            // If the file already exists in the folder, upgrade the version
            int VersionNumber = 1;
            if (File != null)
                VersionNumber = (int)(File["topVersion"]["versionNumber"]) + 1;

            // Update full metadata
            string ItemID = string.Empty;
            if (File == null)
                ItemID = JObject.Parse(Response)["itemId"].ToString();
            else
                ItemID = File["id"].ToString();

            Content = new JObject()
            {
                {"itemId",          ItemID          },
                {"versionId",       VersionID       },
                {"versionNumber",   VersionNumber   },
                {"type",            "LastModified"  },
                {"data",            "1783386824000" }
            };

            Response = HttpService.PATCHSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/items/versions/metadata",
                                            new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } },
                                            Content,
                                            "JSON");

            if (HttpService.StatusCode != HttpStatusCode.NoContent)
                return Defines.Status.UploadFileError;

            return Defines.Status.Success;
        }

        public static Defines.Status SendNWDItemToAutodeskForgeProcessing(string AccessToken, JObject NWDFile)
        {
            string VersionID = (string)NWDFile["topVersion"]["versionId"];

            string Response = HttpService.GETSync($"{Defines.SignalDocsEndpoints["DOCSPROBaseAddress"]}/api/versions/{VersionID}/forge/convert",
                                                    new Dictionary<string, string> { { "Authorization", $"Bearer {AccessToken}" } });

            if (HttpService.StatusCode != HttpStatusCode.NoContent)
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

                if (SuperObject.Item1.Equals(string.Empty))
                    return null;

                Parent = SuperObject.Item2;
                Children = (JArray)Parent["children"];
            }

            return (string)Parent["id"];
        }
    }
}
