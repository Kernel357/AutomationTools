using System.ServiceModel;

namespace WCFServiceInRevit
{
    [ServiceContract]
    public interface IWCFService
    {
        [OperationContract]
        int IsLoaded();

        // Launching a new task in the Revit context (locking)
        [OperationContract]
        int RunTask(string TaskName, ref string StatusMessage, string[] Args);
    }
}
