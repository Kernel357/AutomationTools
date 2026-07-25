using System.Collections.Generic;
using System.IO;

namespace AutomationTools
{
    class Config
    {
        public string   IPAddressOfRevitServer          { get; set; }
        public int      RevitAndNavisworksVersion       { get; set; }
        public string   RevitPath                       { get; set; }
        public string   FileToolsTaskRunnerPath         { get; set; }
        public string   NWCDestinationPath              { get; set; }
        public string   NWFDestinationPath              { get; set; }
        public string   NWDDestinationPath              { get; set; }
        public string   IFCDestinationPath              { get; set; }
        public string   RVTDestinationPath              { get; set; }
        public bool     ShouldDeleteUPDATEFile          { get; set; }

        public int NumberOfWorkingCoresToExportFromRevitServer  { get; set; }
        public int NumberOfWorkingCoresForGenerationNWFiles     { get; set; }
        public int NumberOfWorkingCoresForGenerationIFCFiles    { get; set; }

        public List<BlockWithModels> BlocksWithModels;
    }

    class BlockWithModels
    {
        public string Name { get; set; }
        public string CompanyName { get; set; }
        public string ProjectName { get; set; }
        public string TargetViewName { get; set; }

        public List<string> PathsToModelsAndDestinationPaths;
    }
}
