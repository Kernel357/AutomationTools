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
        private static List<IWCFService>        _ServiceInterfaces;
        private static string[,,]               _BufferForModelReferences;
        private static int[]                    _ModelIteratorForEachCPUCore;
        private static string                   _AutomationToolsCachePath;
        private static string                   _BlockName;
        private static string                   _TargetViewName;
        private static int                      _NumberOfWorkingCores;
        private static int                      _RevitAndNavisworksVersion;
        private static Defines.Status           _Status;

        private static void FilesGenerationWorker(object Param)
        {
            Defines.Status Status = Defines.Status.Success;
            string StatusMessage = string.Empty;

            int ThreadNumber = (int)Param;

            // Делаем прогрев графической подсистемы
            LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Прогрев графики экземпляра...");

            Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask("WarmUp",
                                                                                ref StatusMessage,
                                                                                new string[] { });

            LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Статус прогрева: {Status}");

            if (Status != Defines.Status.Success)
            {
                LoggingSystem.WriteLine($"[-] Сообщение об ошибке: {StatusMessage}");
                _ServiceInterfaces[ThreadNumber].RunTask("RevitShutdown",
                                                     ref StatusMessage,
                                                     new string[] { });
                return;
            }    
                

            // Экспортируем виды
            for (int i = 0; i < _ModelIteratorForEachCPUCore[ThreadNumber]; i++)
            {
                // Перед началом проверим глобальный статус
                if (_Status != Defines.Status.Success)
                    break;

                // Открываем в Revit .rvt документ 
                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Открытие документа...");

                Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask("OpenDocument", 
                                                                                  ref StatusMessage,
                                                                                  new string[] { $"{_AutomationToolsCachePath}\\RVTs\\{_BlockName}\\{_BufferForModelReferences[(int)Param, i, 0]}" });

                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Статус открытия документа: {Status}");

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        LoggingSystem.WriteLine($"[-] Сообщение об ошибке: {StatusMessage}");

                    _Status = Defines.Status.Error;
                    break;
                }

                // Вытаскиваем из документа целевой вид
                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Экспорт вида...");

                Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask("ExportViewsToNWC", 
                                                                                  ref StatusMessage, 
                                                                                  new string[] { $"{_AutomationToolsCachePath}\\NWCs\\{_BlockName}", _TargetViewName });

                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Статус экспорта вида: {Status}");

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        LoggingSystem.WriteLine($"[-] Сообщение об ошибке: {StatusMessage}");

                    _Status = Defines.Status.Error;
                    break;
                }

                // Закрываем документ
                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Закрытие документа...");

                Status = (Defines.Status)_ServiceInterfaces[ThreadNumber].RunTask("CloseDocument", 
                                                                                  ref StatusMessage, 
                                                                                  new string[] { });

                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Статус закрытия документа: {Status}");

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        LoggingSystem.WriteLine($"[-] Сообщение об ошибке: {StatusMessage}");

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

                LoggingSystem.WriteLine("[*] Запуск экземпляра Revit...");

                // Нельзя просто взять и запустить Revit.exe в отдельном процессе из процесса AutomationTools,
                // в таком случае он не найдёт лицензию и не запутится. Не совсем ясно почему так происходит

                // Используем метод с планировщиком Windows и добавляем задачу
                // с выполнением по триггеру. Активируем этот триггер из нашего приложения. Задача запустит Revit.exe
                EventLog.WriteEvent("Application", new EventInstance(_RevitAndNavisworksVersion, 0));

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

        public static Defines.Status FilesGenerationMain(ref string[,,]                       BufferForModelReferences,
                                                         int                                  RevitAndNavisworksVersion,
                                                         int                                  NumberOfWorkingCores,
                                                         ref int[]                            ModelIteratorForEachCPUCore,
                                                         string                               BlockName,
                                                         string                               TargetViewName,
                                                         string                               AutomationToolsCachePath,
                                                         string                               FileToolsTaskRunnerPath,
                                                         Dictionary<string, string>           DestinationPaths,
                                                         bool                                 ShouldDeleteUPDATEFile)
        {
            LoggingSystem.WriteLine("[*] Генерация Navisworks файлов...");

            _ServiceInterfaces = new List<IWCFService>();
            _BufferForModelReferences = BufferForModelReferences;
            _ModelIteratorForEachCPUCore = ModelIteratorForEachCPUCore;
            _AutomationToolsCachePath = AutomationToolsCachePath;
            _BlockName = BlockName;
            _TargetViewName = TargetViewName;
            _NumberOfWorkingCores = NumberOfWorkingCores;
            _RevitAndNavisworksVersion = RevitAndNavisworksVersion;
            _Status = Defines.Status.Success;

            // Инициализируем систему связи с Revit
            if (InitRevitAPI() != Defines.Status.Success)
                return Defines.Status.Error;

            // Создадим папки для экспорта NWC, NWD
            Directory.CreateDirectory(Path.Combine(AutomationToolsCachePath, "NWCs", BlockName));
            Directory.CreateDirectory(Path.Combine(AutomationToolsCachePath, "NWD", BlockName));

            // Запускаем потоки для работы с экземплярами Revit и передаём в них индекс, по которому они получат доступ к интерфейсу и процессу
            // Это позволит каждому потоку работать со своим экземпляром Revit
            List<Thread> Threads = new List<Thread>();

            for (int i = 0; i < NumberOfWorkingCores; i++)
            {
                Threads.Add(new Thread(FilesGenerationWorker));
                Threads[i].Start(i);
                LoggingSystem.WriteLine($"[+] Поток {i} запущен!");
            }

            // Если нужное количество потоков не было запущено, то выходим
            if (Threads.Count() != NumberOfWorkingCores)
            {
                // Завершаем потоки, которые уже успели запуститься
                foreach (var Thread in Threads)
                    Thread.Abort();

                LoggingSystem.WriteLine("[-] Возникла ошибка при запуске потоков, прерывание...");
                LoggingSystem.WriteLine("[!] Вероятно, произошла внутренняя ошибка, обратитесь к разработчику.");
                return Defines.Status.Error;
            }

            // Ждём завершения 
            int ThreadIterator = 0;

            while (ThreadIterator < NumberOfWorkingCores)
            {
                if (Threads[ThreadIterator].IsAlive != false)
                    continue;

                LoggingSystem.WriteLine($"[+] Поток {ThreadIterator} завершён!");
                ThreadIterator++;
            }

            // Проверяем глобальный статус
            if(_Status != Defines.Status.Success)
                return _Status;


            // Объединяем все NWC из целевой папки в NWD/NWF
            string NWCDestinationPath = DestinationPaths["NWC"];
            string NWDDestinationPath = DestinationPaths["NWD"];

            if(string.IsNullOrEmpty(NWCDestinationPath) || string.IsNullOrEmpty(NWDDestinationPath))
            {
                LoggingSystem.WriteLine("[-] Путь для NWD или NWCs не задан, прерывание...");
                return Defines.Status.Error;
            }

            // Проверяем доступность диска и директории пункта назначения
            if (new DirectoryInfo(NWCDestinationPath).Exists == false)
            {
                LoggingSystem.WriteLine("[-] Директория пункта назначения недоступна...");
                return Defines.Status.Error;
            }
            
            // Выставляем флаг FirstGeneration, если это первая генерация для указанного блока
            bool FirstGeneration = false;
            if (new DirectoryInfo(Path.Combine(NWCDestinationPath, BlockName)).Exists == false)
                FirstGeneration = true;
 
            // Если это уже не первая генерация - отчищаем старые файлы; NWF не трогаем, потому что она будет обновляться только по специальному файлу-маркеру.
            if (FirstGeneration == false)
            {
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(NWCDestinationPath, BlockName)));
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(NWDDestinationPath, BlockName)));
            }

            Directory.CreateDirectory(Path.Combine(NWCDestinationPath, BlockName));
            Directory.CreateDirectory(Path.Combine(NWDDestinationPath, BlockName));

            // Копируем все NWC в пункт назначения; делаем это до формирования NWF, чтобы в NWF были актуальные ссылки на NWC
            var Files = new DirectoryInfo(Path.Combine(AutomationToolsCachePath, "NWCs", BlockName)).GetFiles();
            foreach (var File in Files)
                System.IO.File.Copy(File.FullName, Path.Combine(NWCDestinationPath, BlockName, File.Name));

            // Составляем список всех NWC
            string NWCListFilePath = Path.Combine(NWCDestinationPath, BlockName, "NWCList.txt");
            StreamWriter StreamWriter = File.CreateText(NWCListFilePath);

            Files = new DirectoryInfo(Path.Combine(NWCDestinationPath, BlockName)).GetFiles();
            foreach (var File in Files) 
                StreamWriter.WriteLine(File.FullName);

            StreamWriter.Close();

            // Запускаем процесс FiletoolsTaskRunner для создания NWD
            LoggingSystem.WriteLine("[*] Создание NWD...");

            Process GenerationProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(FileToolsTaskRunnerPath, "FiletoolsTaskRunner.exe"),
                    Arguments = $"/i \"{ NWCListFilePath }\" " + 
                                $"/of \"{ Path.Combine(NWDDestinationPath, BlockName, $"{BlockName}.nwd") }\" " +
                                $"/log \"{ Path.Combine(AutomationToolsCachePath, "Logs", "LogNWD.log") }\" ",
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    CreateNoWindow = true
                }
            };

            GenerationProcess.Start();
            GenerationProcess.WaitForExit();

            if (!File.Exists(Path.Combine(NWDDestinationPath, BlockName, $"{BlockName}.nwd")))
            {
                LoggingSystem.WriteLine("[-] Возникла ошибка при создании NWD, прерывание...");
                LoggingSystem.WriteLine("[!] Проверьте лицензию Navisworks. Если проблема сохранится, обратитесь к разработчику.");
                return Defines.Status.Error;
            }


            // Ищем файл-маркер, если он найден - запускаем процесс генерации NWF
            string NWFDestinationPath = DestinationPaths["NWF"];

            if (string.IsNullOrEmpty(NWFDestinationPath))
                return Defines.Status.Success;

            if (File.Exists(Path.Combine(NWFDestinationPath, BlockName, "UPDATE")))
            {
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(NWFDestinationPath, BlockName)));
                Directory.CreateDirectory(Path.Combine(NWFDestinationPath, BlockName));

                if (ShouldDeleteUPDATEFile == false)
                    File.Create(Path.Combine(NWFDestinationPath, BlockName, "UPDATE"));

                // Запускаем процесс FiletoolsTaskRunner для создания NWF
                LoggingSystem.WriteLine("[*] Создание NWF...");

                GenerationProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(FileToolsTaskRunnerPath, "FiletoolsTaskRunner.exe"),
                        Arguments = $"/i \"{NWCListFilePath}\" " +
                                    $"/of \"{Path.Combine(NWFDestinationPath, BlockName, $"{BlockName}.nwf")}\" " +
                                    $"/log \"{Path.Combine(AutomationToolsCachePath, "Logs", "LogNWF.log")}\" ",
                        UseShellExecute = false,
                        RedirectStandardOutput = false,
                        CreateNoWindow = true
                    }
                };

                GenerationProcess.Start();
                GenerationProcess.WaitForExit();

                if (!File.Exists(Path.Combine(NWFDestinationPath, BlockName, $"{BlockName}.nwf")))
                {
                    LoggingSystem.WriteLine("[-] Возникла ошибка при создании NWF, прерывание...");
                    LoggingSystem.WriteLine("[!] Проверьте лицензию Navisworks. Если проблема сохранится, обратитесь к разработчику.");
                    return Defines.Status.Error;
                }
            }

            // Возврат в метод Main()
            return Defines.Status.Success;
        }
    }
}
