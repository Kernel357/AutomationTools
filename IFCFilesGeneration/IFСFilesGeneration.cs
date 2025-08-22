using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using WCFServiceInRevit;

namespace AutomationTools
{
    class IFСFilesGeneration
    {
        private static List<IWCFService>    _ServiceInterfaces;
        private static string[,,]           _BufferForModelReferences;
        private static int[]                _ModelIteratorForEachCPUCore;
        private static string               _AutomationToolsCachePath;
        private static string               _BlockName;
        private static int                  _NumberOfWorkingCores;
        private static int                  _RevitAndNavisworksVersion;
        private static Defines.Status       _Status;

        private static void IFCFilesGenerationWorker(object Param)
        {
            Defines.Status Status = Defines.Status.Success;
            string StatusMessage = string.Empty;

            int ThreadNumber = (int)Param;

            for (int i = 0; i < _ModelIteratorForEachCPUCore[ThreadNumber]; i++)
            {
                // Перед началом проверим глобальный статус
                if (_Status != Defines.Status.Success)
                    break;

                // Открываем в Revit .rvt документ 
                Console.WriteLine("[*] Поток {0}: Открытие документа...", ThreadNumber);

                Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask("OpenDocument",
                                                                                  ref StatusMessage,
                                                                                  new string[] { _AutomationToolsCachePath + "\\RVTs\\" + _BlockName + "\\" + _BufferForModelReferences[(int)Param, i, 0] });

                Console.WriteLine("[*] Поток {0}: Статус открытия документа: {1}", ThreadNumber, Status);

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        Console.WriteLine("[-] Сообщение об ошибке: {0}", StatusMessage);

                    _Status = Defines.Status.Error;
                    break;
                }


                // Экспортируем документ в IFC
                Console.WriteLine("[*] Поток {0}: Экспорт в IFC...", ThreadNumber);

                Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask("ExportToIFC",
                                                                                  ref StatusMessage,
                                                                                  new string[] { _AutomationToolsCachePath + "\\IFCs\\" + _BlockName});

                Console.WriteLine("[*] Поток {0}: Статус экспорта в IFC: {1}", ThreadNumber, Status);

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        Console.WriteLine("[-] Сообщение об ошибке: {0}", StatusMessage);

                    _Status = Defines.Status.Error;
                    break;
                }

                // Закрываем документ
                Console.WriteLine("[*] Поток {0}: Закрытие документа...", ThreadNumber);

                Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask("CloseDocument",
                                                                                  ref StatusMessage,
                                                                                  new string[] { });

                Console.WriteLine("[*] Поток {0}: Статус закрытия документа: {1}", ThreadNumber, Status);

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        Console.WriteLine("[-] Сообщение об ошибке: {0}", StatusMessage);

                    _Status = Defines.Status.Error;
                    break;
                }
            }

            // После завершения закрываем Revit
            _ServiceInterfaces[ThreadNumber].RunTask("RevitShutdown",
                                                     ref StatusMessage,
                                                     new string[] { });

            return;
        }

        private static Defines.Status InitRevitAPI()
        {
            int Port = 9000;

            for (int i = 0; i < _NumberOfWorkingCores; i++)
            {
                // Запускаем Revit 

                Console.WriteLine("[*] Запуск экземпляра Revit...");

                // Нельзя просто взять и запустить Revit.exe в отдельном процессе из процесса AutomationTools,
                // в таком случае он не найдёт лицензию и не запутится. Не совсем ясно почему так происходит

                // Используем метод с планировщиком Windows и добавляем задачу
                // с выполнением по триггеру. Активируем этот триггер из нашего приложения. Задача запустит Revit.exe
                EventLog.WriteEvent("Application", new EventInstance(_RevitAndNavisworksVersion, 0));

                // Подключаемся к WCF сервису, запущенному внутри Revit,
                // чтобы получить возможность выполнять действия с проектом в контексте Revit
                EndpointAddress EndpointAddress = new EndpointAddress(new Uri("net.tcp://127.0.0.1:" + Port.ToString() + "/WCFService"));
                NetTcpBinding Binding = new NetTcpBinding();

                Console.WriteLine("[*] Подключение к WCF сервису экземпляра Revit...");

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

                Console.WriteLine("[+] Выполнено подключение к WCF сервису экземпляра Revit!");
                Port++;
            }

            return Defines.Status.Success;
        }

        public static Defines.Status IFСFilesGenerationMain(ref string[,,]                  BufferForModelReferences,
                                                            int                             RevitAndNavisworksVersion,
                                                            int                             NumberOfWorkingCores,
                                                            ref int[]                       ModelIteratorForEachCPUCore,
                                                            string                          BlockName,
                                                            string                          AutomationToolsCachePath,
                                                            Dictionary<string, string>      DestinationPaths)
        {
            Console.WriteLine("[*] Генерация IFC файлов...");

            _ServiceInterfaces = new List<IWCFService>();
            _BufferForModelReferences = BufferForModelReferences;
            _ModelIteratorForEachCPUCore = ModelIteratorForEachCPUCore;
            _AutomationToolsCachePath = AutomationToolsCachePath;
            _BlockName = BlockName;
            _NumberOfWorkingCores = NumberOfWorkingCores;
            _RevitAndNavisworksVersion = RevitAndNavisworksVersion;
            _Status = Defines.Status.Success;

            // Инициализируем систему связи с Revit
            if (InitRevitAPI() != Defines.Status.Success)
                return Defines.Status.Error;

            // Создаём папку для экспорта в ICF
            Directory.CreateDirectory(AutomationToolsCachePath + "\\IFCs\\" + BlockName);

            // Запускаем потоки для работы с экземплярами Revit и передаём в них индекс, по которому они получат доступ к интерфейсу и процессу
            // Это позволит каждому потоку работать со своим экземпляром Revit
            List<Thread> Threads = new List<Thread>();

            for (int i = 0; i < NumberOfWorkingCores; i++)
            {
                Threads.Add(new Thread(IFCFilesGenerationWorker));
                Threads[i].Start(i);
                Console.WriteLine("[+] Поток {0} запущен!", i);
            }

            // Если нужное количество потоков не было запущено, то выходим
            if (Threads.Count() != NumberOfWorkingCores)
            {
                // Завершаем потоки, которые уже успели запуститься
                foreach (var Thread in Threads)
                    Thread.Abort();

                Console.WriteLine("[-] Возникла ошибка при запуске потоков, прерывание...");
                Console.WriteLine("[!] Вероятно, произошла внутренняя ошибка, обратитесь к разработчику.");
                return Defines.Status.Error;
            }

            // Ждём завершения 
            int ThreadIterator = 0;

            while (ThreadIterator < NumberOfWorkingCores)
            {
                if (Threads[ThreadIterator].IsAlive != false)
                    continue;

                Console.WriteLine("[+] Поток {0} завершён!", ThreadIterator);
                ThreadIterator++;
            }

            // Проверяем глобальный статус
            if (_Status != Defines.Status.Success)
                return _Status;


            // Проверяем доступность диска и директории пункта назначения
            string IFCDestinationPath = DestinationPaths["IFC"];

            if (new DirectoryInfo(IFCDestinationPath).Exists == false)
            {
                Console.WriteLine("[-] Директория пункта назначения недоступна...");
                return Defines.Status.Error;
            }

            // Выставляем флаг FirstGeneration, если это первая генерация для указанного блока
            bool FirstGeneration = false;
            if (new DirectoryInfo(IFCDestinationPath + "\\" + BlockName).Exists == false)
            {
                FirstGeneration = true;
            }

            // Если это уже не первая генерация - отчищаем старые файлы
            if (FirstGeneration == false)
                Utils.RecursiveDelete(new DirectoryInfo(IFCDestinationPath + "\\" + BlockName));

            Directory.CreateDirectory(IFCDestinationPath + "\\" + BlockName);


            // Копируем все IFC в пункт назначения
            var Files = new DirectoryInfo(AutomationToolsCachePath + "\\IFCs\\" + BlockName).GetFiles();
            foreach (var File in Files)
            {
                // Revit при экспорте создает логи, поэтому отфильтровываем модели от логов
                if(File.Name.EndsWith(".ifc") == true)
                    System.IO.File.Copy(File.FullName, IFCDestinationPath + "\\" + BlockName + "\\" + File.Name);
            }

            return Defines.Status.Success;
        }
    }
}
