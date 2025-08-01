using System.ServiceModel;

namespace WCFServiceInRevit
{
    [ServiceContract]
    public interface IWCFService
    {
        // Метод, используемый для проверки загрузки WCF Service
        [OperationContract]
        int IsLoaded();

        // Запускаем новую задачу в контексте Revit и дожидаемся завершения (блокирующий способ)
        [OperationContract]
        int RunTask(string TaskName, ref string StatusMessage, string[] Args);

        // Закрываем Revit
        [OperationContract]
        void RevitShutdown();
    }
}
