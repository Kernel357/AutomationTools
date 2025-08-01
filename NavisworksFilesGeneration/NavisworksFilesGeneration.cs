using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.ServiceModel;
using WCFServiceInRevit;
using System.Diagnostics;
using System.Threading;

namespace AutomationTools
{
    class NavisworksFilesGeneration
    {
        private static List<IWCFService>     _ServiceInterfaces;
        private static string[,,]            _BufferForModelReferences;
        private static int[]                 _ModelIteratorForEachCPUCore;
        private static string                _AutomationToolsCachePath;
        private static string                _BlockName;
        private static int                   _NumberOfWorkingCores;
        private static int                   _RevitAndNavisworksVersion;
        private static Defines.Status        _Status;

        private static void NavisworksFilesGenerationWorker(object Param)
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
                                                                                  new string[] { _AutomationToolsCachePath + "\\RVTs\\" + _BlockName + "\\" + _BufferForModelReferences[(int)Param, i, 0]});

                Console.WriteLine("[*] Поток {0}: Статус открытия документа: {1}", ThreadNumber, Status);

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        Console.WriteLine("[-] Сообщение об ошибке: {0}", StatusMessage);

                    _Status = Defines.Status.Error;
                    break;
                }
                    
                    
                // Вытаскиваем из документа Navisworks вид
                Console.WriteLine("[*] Поток {0}: Экспорт вида...", ThreadNumber);

                Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask("ExportViewsToNWC", 
                                                                                  ref StatusMessage, 
                                                                                  new string[] { _AutomationToolsCachePath + "\\NWCs\\" + _BlockName, "Navisworks" });

                Console.WriteLine("[*] Поток {0}: Статус экспорта вида: {1}", ThreadNumber, Status);

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
            _ServiceInterfaces[ThreadNumber].RevitShutdown();

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

        public static Defines.Status NavisworksFilesGenerationMain(ref string[,,]                       BufferForModelReferences,
                                                                   int                                  RevitAndNavisworksVersion,
                                                                   int                                  NumberOfWorkingCores,
                                                                   ref int[]                            ModelIteratorForEachCPUCore,
                                                                   string                               BlockName,
                                                                   string                               AutomationToolsCachePath,
                                                                   string                               FileToolsTaskRunnerPath,
                                                                   Dictionary<string, string>           DestinationPaths,
                                                                   bool                                 ShouldDeleteUPDATEFile)
        {
            Console.WriteLine("[*] Генерация Navisworks файлов...");

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

            // Создадим папки для экспорта NWC, NWD
            Directory.CreateDirectory(AutomationToolsCachePath + "\\NWCs\\" + BlockName);
            Directory.CreateDirectory(AutomationToolsCachePath + "\\NWD\\" + BlockName);

            // Запускаем потоки для работы с экземплярами Revit и передаём в них индекс, по которому они получат доступ к интерфейсу и процессу
            // Это позволит каждому потоку работать со своим экземпляром Revit
            List<Thread> Threads = new List<Thread>();

            for (int i = 0; i < NumberOfWorkingCores; i++)
            {
                Threads.Add(new Thread(NavisworksFilesGenerationWorker));
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
            if(_Status != Defines.Status.Success)
                return _Status;


            // Объединяем все NWC из целевой папки в NWD/NWF
            string NWCDestinationPath = DestinationPaths["NWC"];
            string NWDDestinationPath = DestinationPaths["NWD"];

            // Проверяем доступность диска и директории пункта назначения
            if (new DirectoryInfo(NWCDestinationPath).Exists == false)
            {
                Console.WriteLine("[-] Директория пункта назначения недоступна...");
                return Defines.Status.Error;
            }
            
            // Выставляем флаг FirstGeneration, если это первая генерация для указанного блока
            bool FirstGeneration = false;
            if (new DirectoryInfo(NWCDestinationPath + "\\" + BlockName).Exists == false)
                FirstGeneration = true;

            
            // Если это уже не первая генерация - отчищаем старые файлы; NWF не трогаем, потому что она будет обновляться только по специальному файлу-маркеру.
            if (FirstGeneration == false)
            {
                Utils.RecursiveDelete(new DirectoryInfo(NWCDestinationPath + "\\" + BlockName));
                Utils.RecursiveDelete(new DirectoryInfo(NWDDestinationPath + "\\" + BlockName));
            }

            Directory.CreateDirectory(NWCDestinationPath + "\\" + BlockName);
            Directory.CreateDirectory(NWDDestinationPath + "\\" + BlockName);


            // Копируем все NWC в пункт назначения; делаем это до формирования NWF, чтобы в NWF были актуальные ссылки на NWC
            var Files = new DirectoryInfo(AutomationToolsCachePath + "\\NWCs\\" + BlockName).GetFiles();
            foreach (var File in Files)
                System.IO.File.Copy(File.FullName, NWCDestinationPath + "\\" + BlockName + "\\" + File.Name);
            

            // Составляем список всех NWC
            string NWCListFilePath = NWCDestinationPath + "\\" + BlockName + "\\NWCList.txt";
            StreamWriter StreamWriter = File.CreateText(NWCListFilePath);

            Files = new DirectoryInfo(NWCDestinationPath + "\\" + BlockName).GetFiles();
            foreach (var File in Files) 
                StreamWriter.WriteLine(File.FullName);

            StreamWriter.Close();
            NWCListFilePath = NWCDestinationPath + "\\" + BlockName + "\\NWCList.txt";

            // Запускаем процесс FiletoolsTaskRunner для создания NWD
            Console.WriteLine("[*] Создание NWD...");

            Process GenerationProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = FileToolsTaskRunnerPath + "\\FiletoolsTaskRunner.exe",
                    Arguments = "/i " + "\"" + NWCListFilePath + "\"" + 
                                " /of " + "\"" + NWDDestinationPath + "\\" + BlockName + "\\" + BlockName + ".nwd" + "\"" +
                                " /log " + "\"" + AutomationToolsCachePath + "\\Logs\\" + BlockName + "\\LogNWD.log" + "\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    CreateNoWindow = true
                }
            };

            GenerationProcess.Start();
            GenerationProcess.WaitForExit();

            if (!File.Exists(NWDDestinationPath + "\\" + BlockName + "\\" + BlockName + ".nwd"))
            {
                Console.WriteLine("[-] Возникла ошибка при создании NWD, прерывание...");
                Console.WriteLine("[!] Проверьте лицензию Navisworks. Если проблема сохранится, обратитесь к разработчику.");
                return Defines.Status.Error;
            }

            // Ищем файл-маркер, если он найден - запускаем процесс генерации NWF
            string NWFDestinationPath = DestinationPaths["NWF"];

            if (File.Exists(NWFDestinationPath + "\\" + BlockName + "\\UPDATE"))
            {
                Utils.RecursiveDelete(new DirectoryInfo(NWFDestinationPath + "\\" + BlockName));
                Directory.CreateDirectory(NWFDestinationPath + "\\" + BlockName);

                if (ShouldDeleteUPDATEFile == false)
                    File.Create(NWFDestinationPath + "\\" + BlockName + "\\UPDATE");

                // Запускаем процесс FiletoolsTaskRunner для создания NWF
                Console.WriteLine("[*] Создание NWF...");

                GenerationProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = FileToolsTaskRunnerPath + "\\FiletoolsTaskRunner.exe",
                        Arguments = "/i " + "\"" + NWCListFilePath + "\"" +
                                    " /of " + "\"" + NWFDestinationPath + "\\" + BlockName + "\\" + BlockName + ".nwf" + "\"" +
                                    " /log " + "\"" + AutomationToolsCachePath + "\\Logs\\" + BlockName + "\\LogNWF.log" + "\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = false,
                        CreateNoWindow = true
                    }
                };

                GenerationProcess.Start();
                GenerationProcess.WaitForExit();

                if (!File.Exists(NWFDestinationPath + "\\" + BlockName + "\\" + BlockName + ".nwf"))
                {
                    Console.WriteLine("[-] Возникла ошибка при создании NWF, прерывание...");
                    Console.WriteLine("[!] Проверьте лицензию Navisworks. Если проблема сохранится, обратитесь к разработчику.");
                    return Defines.Status.Error;
                }
            }

            // Возврат в метод Main()
            return Defines.Status.Success;
        }
    }
}
