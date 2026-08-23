using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.ServiceModel;
using WCFServiceInRevit;

namespace AutomationTools
{
    internal class WCFService
    {
        private static List<IWCFService> _ServiceInterfaces;

        public static Defines.Status InitializeСonnection(int NumberOfWorkingCores, 
                                                          int RevitAndNavisworksVersion)
        {
            _ServiceInterfaces = new List<IWCFService>();
            int Port = 9000;

            for (int i = 0; i < NumberOfWorkingCores; i++)
            {
                // Запускаем Revit 
                LoggingSystem.WriteLine("[*] Запуск экземпляра Revit...");

                // Нельзя просто взять и запустить Revit.exe в отдельном процессе из процесса AutomationTools,
                // в таком случае он не найдёт лицензию и не запутится. Не совсем ясно почему так происходит

                // Используем метод с планировщиком Windows и добавляем задачу
                // с выполнением по триггеру. Активируем этот триггер из нашего приложения. Задача запустит Revit.exe
                EventLog.WriteEvent("Application", new EventInstance(RevitAndNavisworksVersion, 0));

                // Подключаемся к WCF сервису, запущенному внутри Revit,
                // чтобы получить возможность выполнять действия с проектом в контексте Revit
                EndpointAddress EndpointAddress = new EndpointAddress(new Uri($"net.tcp://127.0.0.1:{Port.ToString()}/WCFService"));
                NetTcpBinding Binding = new NetTcpBinding();

                LoggingSystem.WriteLine("[*] Подключение к WCF сервису экземпляра Revit...");

                // Ждём когда WCF сервис будет запущен + нам удастся к нему подключиться, а пока блокируем дальнейшее выполнение
                var ChannelFactory = new ChannelFactory<IWCFService>(Binding, EndpointAddress);
                while (true)
                {
                    try
                    {
                        IWCFService ServiceInterface = ChannelFactory.CreateChannel();
                        ((IContextChannel)ServiceInterface).OperationTimeout = new TimeSpan(9, 0, 0);
                        ServiceInterface.IsLoaded();

                        _ServiceInterfaces.Add(ServiceInterface);
                        break;
                    }
                    catch
                    {
                        continue;
                    }
                }

                LoggingSystem.WriteLine("[+] Выполнено подключение к WCF сервису экземпляра Revit!");
                Port++;
            }

            return Defines.Status.Success;
        }

        public static Defines.Status RunTask(int ThreadNumber,
                                             string TaskName,
                                             ref string StatusMessage,
                                             string[] Args)
        {
            Defines.Status Status;

            try
            {
                Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask(TaskName,
                                                                                  ref StatusMessage,
                                                                                  Args);
            }
            catch (Exception ex)
            {
                Status = Defines.Status.Error;
                StatusMessage = ex.Message;
            }

            return Status;
        }
    }
}

