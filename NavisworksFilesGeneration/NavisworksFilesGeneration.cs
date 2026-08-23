using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Threading;

namespace AutomationTools
{
    class NavisworksFilesGeneration
    {
        private static string[,,]               _BufferForModelReferences;
        private static int[]                    _ModelIteratorForEachCPUCore;
        private static string                   _AutomationToolsCachePath;
        private static string                   _BlockName;
        private static string                   _TargetViewName;
        private static Defines.Status           _Status;

        private static void FilesGenerationWorker(object Param)
        {
            Defines.Status Status = Defines.Status.Success;
            string StatusMessage = string.Empty;

            int ThreadNumber = (int)Param;

            // Делаем прогрев графической подсистемы
            LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Прогрев графики экземпляра...");

            Status = WCFService.RunTask(ThreadNumber,
                                        "WarmUp",
                                        ref StatusMessage,
                                        new string[] { });

            LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Статус прогрева: {Status}");

            if (Status != Defines.Status.Success)
            {
                LoggingSystem.WriteLine($"[-] Сообщение об ошибке: {StatusMessage}");
                WCFService.RunTask(ThreadNumber,
                                   "RevitShutdown",
                                   ref StatusMessage,
                                   new string[] { });
                return;
            }    
                
            string DocumentName = string.Empty;

            // Экспортируем виды
            for (int i = 0; i < _ModelIteratorForEachCPUCore[ThreadNumber]; i++)
            {
                // Перед началом проверим глобальный статус
                if (_Status != Defines.Status.Success)
                    break;

                // Получаем название документа
                DocumentName = _BufferForModelReferences[(int)Param, i, 0];

                // Открываем в Revit .rvt документ 
                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Открытие \"{DocumentName}\"...");

                Status = WCFService.RunTask(ThreadNumber,
                                            "OpenDocument", 
                                            ref StatusMessage,
                                            new string[] { $"{_AutomationToolsCachePath}\\RVTs\\{_BlockName}\\{DocumentName}" });

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
                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Экспорт \"{_TargetViewName}\"...");

                Status = WCFService.RunTask(ThreadNumber,
                                            "ExportViewsToNWC", 
                                            ref StatusMessage, 
                                            new string[] { $"{_AutomationToolsCachePath}\\NWCs\\{_BlockName}", _TargetViewName });

                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Статус экспорта вида: {Status}");

                // После окончания операции проверим глобальный и локальный статус
                if ((Status != Defines.Status.Success && Status != Defines.Status.GeometryNotFoundError) || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        LoggingSystem.WriteLine($"[-] Сообщение об ошибке: {StatusMessage}");

                    _Status = Defines.Status.Error;
                    break;
                }

                // Закрываем документ
                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Закрытие \"{DocumentName}\"...");

                Status = WCFService.RunTask(ThreadNumber,
                                            "CloseDocument", 
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
            WCFService.RunTask(ThreadNumber,
                                "RevitShutdown",
                                ref StatusMessage,
                                new string[] { });

            return;
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

            _BufferForModelReferences = BufferForModelReferences;
            _ModelIteratorForEachCPUCore = ModelIteratorForEachCPUCore;
            _AutomationToolsCachePath = AutomationToolsCachePath;
            _BlockName = BlockName;
            _TargetViewName = TargetViewName;
            _Status = Defines.Status.Success;

            // Инициализируем систему связи с Revit
            if (WCFService.InitializeСonnection(NumberOfWorkingCores, RevitAndNavisworksVersion) != Defines.Status.Success)
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
            
            // Если это уже не первая генерация - отчищаем старые файлы; NWF не трогаем, потому что она будет обновляться только по специальному файлу-маркеру.
            if (new DirectoryInfo(Path.Combine(NWCDestinationPath, BlockName)).Exists)
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(NWCDestinationPath, BlockName)));

            if (new DirectoryInfo(Path.Combine(NWDDestinationPath, BlockName)).Exists)
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(NWDDestinationPath, BlockName)));
        
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
