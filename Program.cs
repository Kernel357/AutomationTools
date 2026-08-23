using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AutomationTools
{
    internal class MainProgram
    {
        private static string[,,]                   _BufferForModelReferences = null;
        private static int[]                        _ModelIteratorForEachCPUCore = null;
        private static int                          _NumberOfWorkingCores;
        private static int                          _NumberOfBlocksWithModels;
        private static string                       _BlockName;
        private static BlockWithModels              _BlockWithModels;
        private static Config                       _CurrentConfig;
        private static string                       _IPAddressOfRevitServer;
        private static int                          _RevitAndNavisworksVersion;
        private static Dictionary<string, string>   _DestinationPaths;
        private static string                       _AutomationToolsRootPath;
        private static string                       _AutomationToolsCachePath;
        private static string                       _RevitPath;
        private static string                       _TargetViewName;
        private static string                       _FileToolsTaskRunnerPath;
        private static bool                         _ShouldDeleteUPDATEFile;

        private static Mutex                        _IsRunning;

        [STAThread]
        static int Main(string[] args)
        {
            Defines.Status Status;

            // Check if the application is already running
            _IsRunning = new Mutex(false, "AutomationToolsMutex");

            // Wait for the mutex to be released or timeout after 20 minutes
            try
            {
                if (!_IsRunning.WaitOne(TimeSpan.FromMinutes(20)))
                    return -1;
            }
            catch { }

            Console.InputEncoding = Encoding.GetEncoding(1251);

            // Get the root path of the AutomationTools
            _AutomationToolsRootPath = Directory.GetCurrentDirectory();

            // Setup cache directory
            _AutomationToolsCachePath = Path.Combine(_AutomationToolsRootPath, "Cache", Guid.NewGuid().ToString());
            if (SetupCache() != Defines.Status.Success)
            {
                Exit();
                return -1;
            }

            // Create the folder for logs
            Directory.CreateDirectory(Path.Combine(_AutomationToolsCachePath, "Logs"));

            // Init logging system
            LoggingSystem.Init(_AutomationToolsCachePath);

            // Register global exception handler
            AppDomain.CurrentDomain.UnhandledException += (Sender, Args) =>
            {
                LoggingSystem.WriteLine($"[-] Необработанное исключение: {Args.ExceptionObject as Exception}");
                Exit();
            };

            LoggingSystem.WriteLine("[*] Запуск инструментов автоматизации...");

            // Analyze command line arguments
            if (args.Length == 0 || args[0] == "help")
            {
                PrintInfo();
                Exit();
                return 0;
            }

            // AutoDeploy Mode
            if (args[0] == "AutoDeploy")
            {
                if (args.Length < 3 && !args[1].Equals("SIGNALDOCSAUTH"))
                {
                    LoggingSystem.WriteLine("[-] Недопустимые параметры, прерывание...");
                    LoggingSystem.WriteLine("[!] Проверьте вводимые параметры. Для справки: .\\AutomationTools.exe help");
                    Exit();
                    return -1;
                }

                if (AutoDeploy(ref args, _AutomationToolsRootPath) != Defines.Status.Success)
                {
                    LoggingSystem.WriteLine("[-] Во время развёртывания произошла ошибка, прерывание...");
                    LoggingSystem.WriteLine("[!] Вероятно, произошла внутренняя ошибка, обратитесь к разработчику.");
                    Exit();
                    return -1;
                }

                LoggingSystem.WriteLine("[+] Развёртывание завершено!");
                Exit();
                return 0;
            }

            // Check args
            if (args.Length < 3)
            {
                LoggingSystem.WriteLine("[-] Недопустимые параметры, прерывание...");
                LoggingSystem.WriteLine("[!] Проверьте вводимые параметры. Для справки: .\\AutomationTools.exe help");
                Exit();
                return -1;
            }

            _BlockName = args[0];

            // Analyze configuration file
            if (ConfigFormatFile.ConfigFileLoadAndParse(args[1], ref _CurrentConfig) != Defines.Status.Success)
            {
                LoggingSystem.WriteLine("[-] Конфигурационный файл не найден или не является действительным, прерывание...");
                LoggingSystem.WriteLine("[!] Проверьте правильность пути к файлу конфигураций");
                Exit();
                return -1;
            }

            _IPAddressOfRevitServer = _CurrentConfig.IPAddressOfRevitServer;
            _RevitAndNavisworksVersion = _CurrentConfig.RevitAndNavisworksVersion;
            _RevitPath = _CurrentConfig.RevitPath;
            _FileToolsTaskRunnerPath = _CurrentConfig.FileToolsTaskRunnerPath;
            _ShouldDeleteUPDATEFile = _CurrentConfig.ShouldDeleteUPDATEFile;
            
            _DestinationPaths = new Dictionary<string, string>
            {
                { "NWC", _CurrentConfig.NWCDestinationPath },
                { "NWD", _CurrentConfig.NWDDestinationPath },
                { "NWF", _CurrentConfig.NWFDestinationPath },
                { "IFC", _CurrentConfig.IFCDestinationPath },
                { "RVT", _CurrentConfig.RVTDestinationPath }
            };

            _NumberOfWorkingCores = _CurrentConfig.NumberOfWorkingCoresToExportFromRevitServer;

            LoggingSystem.WriteLine($"[*] IP адрес Revit Server: {_IPAddressOfRevitServer}");
            LoggingSystem.WriteLine($"[*] Версия Revit и Navisworks: {_RevitAndNavisworksVersion}");
            LoggingSystem.WriteLine($"[*] Путь расположения AutomationTools: {_AutomationToolsRootPath}");
            LoggingSystem.WriteLine($"[*] Путь к Revit: {_RevitPath}");
            LoggingSystem.WriteLine($"[*] Путь к FileToolsTaskRunner: {_FileToolsTaskRunnerPath}");
            LoggingSystem.WriteLine($"[*] Путь назначения для NWC файлов: {_DestinationPaths["NWC"]}");
            LoggingSystem.WriteLine($"[*] Путь назначения для NWD файлов: {_DestinationPaths["NWD"]}");
            LoggingSystem.WriteLine($"[*] Путь назначения для NWF файлов: {_DestinationPaths["NWF"]}");
            LoggingSystem.WriteLine($"[*] Путь назначения для IFC файлов: {_DestinationPaths["IFC"]}");
            LoggingSystem.WriteLine($"[*] Путь назначения для RVT файлов: {_DestinationPaths["RVT"]}");
            if (_ShouldDeleteUPDATEFile == true)
                LoggingSystem.WriteLine("[*] Что следует делать с файлом UPDATE: удалить");
            else
                LoggingSystem.WriteLine("[*] Что следует делать с файлом UPDATE: оставить");
            LoggingSystem.WriteLine($"[*] Количество рабочих ядер для экспорта моделей с Revit Server: {_NumberOfWorkingCores}");

            _NumberOfBlocksWithModels = _CurrentConfig.BlocksWithModels.Count;


            // Search for the block with models in the configuration
            if (SearchForBlockWithModels() != Defines.Status.Success)
            {
                LoggingSystem.WriteLine("[-] Подходящий блок с моделями не найден, прерывание...");
                LoggingSystem.WriteLine("[!] Проверьте, правильно ли написано имя блока, а также существует ли блок с таким именем в конфигурационном файле.");
                Exit();
                return -1;
            }

            // Set target view
            _TargetViewName = _BlockWithModels.TargetViewName;

            // Distribute models across CPU cores
            if (DistributeModels() != Defines.Status.Success)
            {
                LoggingSystem.WriteLine("[-] Не удалось выполнить распределение моделей по ядрам, прерывание...");
                LoggingSystem.WriteLine("[!] Вероятно, ошибка возникла из-за неправильного значения количества рабочих ядер в конфигурационном файле (н-р: значение 0)");
                Exit();
                return -1;
            }

            // Export models from Revit Server
            Status = ExportModelsFromRevitServer.ExportModelsFromRevitServerMain(_BlockName,
                                                                                 _IPAddressOfRevitServer,
                                                                                 $"{_RevitPath}\\RevitServerToolCommand",
                                                                                 _AutomationToolsCachePath,
                                                                                 _NumberOfWorkingCores,
                                                                                 ref _BufferForModelReferences,
                                                                                 ref _ModelIteratorForEachCPUCore);
            
            // If the status is failure exit
            if (Status != Defines.Status.Success)
                return -1;
            

            // Execute tasks
            if (args[2].Contains("RVT"))
            {
                if(MoveRVTFilesTask() != Defines.Status.Success)
                {
                    Exit();
                    return -1;
                }     
            }

            if (args[2].Contains("NW"))
            {
                if (NavisworksTask() != Defines.Status.Success)
                {
                    Exit();
                    return -1;
                }    
            }

            if (args[2].Contains("IFC"))
            {
                if (IFCTask() != Defines.Status.Success)
                {
                    Exit();
                    return -1;
                }
            }

            if (args[2].Contains("SD") && args[2].Contains("NW"))
            {
                if (SignalDocsTask() != Defines.Status.Success)
                {
                    Exit();
                    return -1;
                }
            }

            LoggingSystem.WriteLine("[*] Завершение работы инструметов автоматизации...");
            Exit();

            return 0;
        }
        
        static private void ReleaseMutex()
        {
            if (_IsRunning != null)
            {
                _IsRunning.ReleaseMutex();
                _IsRunning.Close();
            }
        }

        static private Defines.Status MoveRVTFilesTask()
        {
            // This mode moves the RVT files to destination folder
            LoggingSystem.WriteLine("[*] Копируем загруженные .RVT в указанный каталог...");

            var RVTDestinationPath = _DestinationPaths["RVT"];
            string PathToModel = string.Empty;

            if (new DirectoryInfo(Path.Combine(RVTDestinationPath, _BlockName)).Exists)
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(RVTDestinationPath, _BlockName)));

            try
            {
                Directory.CreateDirectory($"{RVTDestinationPath}\\{_BlockName}");

                for (int i = 0; i < _BufferForModelReferences.GetLength(0); i++)
                {
                    for (int j = 0; j < _BufferForModelReferences.GetLength(1); j++)
                    {
                        PathToModel = _BufferForModelReferences[i, j, 0];

                        if (PathToModel == null)
                            continue;

                        File.Copy($"{_AutomationToolsCachePath}\\RVTs\\{_BlockName}\\{PathToModel}",
                                  $"{RVTDestinationPath}\\{_BlockName}\\{PathToModel.Substring(PathToModel.LastIndexOf("\\"))}");
                    }
                }
            }
            catch
            {
                LoggingSystem.WriteLine("[-] Не удалось скопировать файлы, прерывание...");
                return Defines.Status.Error;
            }

            return Defines.Status.Success;
        }

        static private Defines.Status NavisworksTask()
        {
            // Distribute models across CPU cores again if the number of cores for exporting models from Revit Server is not equal to the number of cores for generating NW files
            if (_NumberOfWorkingCores != _CurrentConfig.NumberOfWorkingCoresForGenerationNWFiles)
            {
                _NumberOfWorkingCores = _CurrentConfig.NumberOfWorkingCoresForGenerationNWFiles;

                if (DistributeModels() != Defines.Status.Success)
                {
                    LoggingSystem.WriteLine("[-] Не удалось выполнить распределение моделей по ядрам, прерывание...");
                    LoggingSystem.WriteLine("[!] Вероятно, ошибка возникла из-за неправильного значения количества рабочих ядер в конфигурационном файле (н-р: значение 0).");
                    return Defines.Status.Error;
                }
            }

            LoggingSystem.WriteLine($"[*] Количество рабочих ядер для генерации NW файлов: {_NumberOfWorkingCores}");

            if(string.IsNullOrEmpty(_TargetViewName))
            {
                LoggingSystem.WriteLine("[-] Целевой вид не задан, прерывание...");
                return Defines.Status.Error;
            }

            // RVT -> NWC, NWD, NWF
            return NavisworksFilesGeneration.FilesGenerationMain(ref _BufferForModelReferences,
                                                                   _RevitAndNavisworksVersion,
                                                                   _NumberOfWorkingCores,
                                                                   ref _ModelIteratorForEachCPUCore,
                                                                   _BlockName,
                                                                   _TargetViewName,
                                                                   _AutomationToolsCachePath,
                                                                   _FileToolsTaskRunnerPath,
                                                                   _DestinationPaths,
                                                                   _ShouldDeleteUPDATEFile);
        }

        static private Defines.Status IFCTask()
        {
            // Distribute models across CPU cores again if the number of cores for exporting models from Revit Server is not equal to the number of cores for generating IFC files
            if (_NumberOfWorkingCores != _CurrentConfig.NumberOfWorkingCoresForGenerationIFCFiles)
            {
                _NumberOfWorkingCores = _CurrentConfig.NumberOfWorkingCoresForGenerationIFCFiles;

                if (DistributeModels() != Defines.Status.Success)
                {
                    LoggingSystem.WriteLine("[-] Не удалось выполнить распределение моделей по ядрам, прерывание...");
                    LoggingSystem.WriteLine("[!] Вероятно, ошибка возникла из-за неправильного значения количества рабочих ядер в конфигурационном файле (н-р: значение 0).");
                    return Defines.Status.Error;
                }
            }

            LoggingSystem.WriteLine($"[*] Количество рабочих ядер для генерации IFC файлов: {_NumberOfWorkingCores}");

            // RVT -> IFC
            return IFСFilesGeneration.FilesGenerationMain(ref _BufferForModelReferences,
                                                          _RevitAndNavisworksVersion,
                                                          _NumberOfWorkingCores,
                                                          ref _ModelIteratorForEachCPUCore,
                                                          _BlockName,
                                                          _AutomationToolsCachePath,
                                                          _DestinationPaths);
        }

        static private Defines.Status SignalDocsTask()
        {
            if(AuthAndGetTokensModule.AuthAndGetTokensModuleMain(_AutomationToolsRootPath) != Defines.Status.Success)
            {
                LoggingSystem.WriteLine("[-] Ошибка авторизации Signal Docs, прерывание...");
                return Defines.Status.Error;
            }

            Defines.Status Status = Defines.Status.Success;

            string CompanyName = _BlockWithModels.CompanyName;
            string ProjectName = _BlockWithModels.ProjectName;

            if(string.IsNullOrEmpty(CompanyName) || string.IsNullOrEmpty(ProjectName))
            {
                LoggingSystem.WriteLine("[-] Название компании/проекта не задано, прерывание...");
                return Defines.Status.Error;
            }

            LoggingSystem.WriteLine("[*] Загружаем NWD файл на сервер Signal Docs...");
            Status = SignalDocsCommands.UploadFilesToServer(AuthAndGetTokensModule.AccessToken,
                                                            CompanyName,
                                                            ProjectName,
                                                            $"{_DestinationPaths["NWD"].Split(new string[] { $"{ProjectName}\\" }, StringSplitOptions.RemoveEmptyEntries)[1]}\\{_BlockName}",
                                                            $"{_DestinationPaths["NWD"]}\\{_BlockName}",
                                                            $"{_BlockName}.nwd");

            LoggingSystem.WriteLine($"[*] Статус загрузки файла: {Status}");

            if (Status != Defines.Status.Success)
                return Status;

            LoggingSystem.WriteLine("[*] Запускаем Autodesk Forge конвертацию...");
            Status = SignalDocsCommands.StartAutodeskForgeProcessing(AuthAndGetTokensModule.AccessToken,
                                                                    CompanyName,
                                                                    ProjectName,
                                                                    $"{_DestinationPaths["NWD"].Split(new string[] { $"{ProjectName}\\" }, StringSplitOptions.RemoveEmptyEntries)[1]}\\{_BlockName}",
                                                                    $"{_BlockName}.nwd");

            LoggingSystem.WriteLine($"[*] Статус запуска конвертации: {Status}");

            return Status;
        }

        static private void PrintInfo()
        {
            LoggingSystem.WriteLine("[!] Режим автоматического первоначального развёртывания: \n \n" +
                    "           .\\AutomationTools.exe AutoDeploy <MODE> <Revit versions>\n \n" +
                    "               <MODE> - определяет режим развёртывания\n" +
                    "                   HELPERTASK - настроить вспомогательные задачи\n" +
                    "                   SIGNALDOCSAUTH - позволяет настроить авторизацию Signal Docs\n" +
                    "               <Revit versions> - определяет версии Revit, для которых будет настроено окружение\n \n" +
                    "           Н-р: .\\AutomationTools.exe AutoDeploy DIR_CONFIG_HELPERTASK 2022 2021\n ");

            LoggingSystem.WriteLine("[!] Рабочий режим: \n \n" +
                "           .\\AutomationTools.exe <BlockName> <ConfigFileName> <MODE> \n \n" +
                "               <BlockName> - определяет имя блока, который нужно обработать\n\n" +
                "               <ConfigFileName> - определяет имя файла конфигурации\n\n" +
                "               <MODE> - определяет режим работы\n" +
                "                   NW - экспорт в Navisworks форматы\n" +
                "                   RVT - загрузка .rvt с сервера\n" +
                "                   IFC - экспорт в IFC формат\n" +
                "                   SD - производит загрузку моделей на сервер и запускает обработку Autodesk Forge\n\n" +
                "           Н-р: .\\AutomationTools.exe Ocean100M Configs\\Config_2022.bconf NW_IFC\n ");
        }

        static private Defines.Status DistributeModels()
        {
            // Check if the number of working cores is valid
            if (_NumberOfWorkingCores <= 0)
                return Defines.Status.Error;

            // Check if the number of paths to models and destination paths is even
            if (_BlockWithModels.PathsToModelsAndDestinationPaths.Count % 2 != 0)
                return Defines.Status.Error;

            int NumberOfModelsInBlock = _BlockWithModels.PathsToModelsAndDestinationPaths.Count / 2;

            if (NumberOfModelsInBlock <= 0)
            {
                LoggingSystem.WriteLine("[-] В блоке не нашлось ни одной модели!");
                LoggingSystem.WriteLine("[!] Вам следует добавить в блок с указанным названием пути к моделям и пути пунктов назначения.");
                return Defines.Status.Error;
            }

            _BufferForModelReferences = new string[_NumberOfWorkingCores, NumberOfModelsInBlock / _NumberOfWorkingCores + 1, 2];
            _ModelIteratorForEachCPUCore = new int[_NumberOfWorkingCores];

            int CPUCoreIterator = 0;

            // Distribute models across CPU cores
            for (int i = 0; i < (NumberOfModelsInBlock * 2 - 1); i += 2)
            {
                _BufferForModelReferences[CPUCoreIterator, _ModelIteratorForEachCPUCore[CPUCoreIterator], 0] = _BlockWithModels.PathsToModelsAndDestinationPaths.ElementAt(i);
                _BufferForModelReferences[CPUCoreIterator, _ModelIteratorForEachCPUCore[CPUCoreIterator], 1] = _BlockWithModels.PathsToModelsAndDestinationPaths.ElementAt(i + 1);

                _ModelIteratorForEachCPUCore[CPUCoreIterator]++;

                if (CPUCoreIterator == (_NumberOfWorkingCores - 1))
                {
                    CPUCoreIterator = 0;
                    continue;
                }

                CPUCoreIterator++;
            }

            // Check if any of the CPU cores have no models assigned to them
            CPUCoreIterator = 0;
            while (CPUCoreIterator < _NumberOfWorkingCores)
            {
                if (_ModelIteratorForEachCPUCore[CPUCoreIterator] == 0)
                {
                    _NumberOfWorkingCores = CPUCoreIterator;
                    LoggingSystem.WriteLine($"[*] Количество рабочих ядер: {_NumberOfWorkingCores}");
                    break;
                }

                CPUCoreIterator++;
            }

            return Defines.Status.Success;
        }

        static private Defines.Status SearchForBlockWithModels()
        {
            bool BlockFound = _CurrentConfig.BlocksWithModels.Any(Block => Block.Name == _BlockName);

            if (BlockFound != true)
                return Defines.Status.Error;

            _BlockWithModels = _CurrentConfig.BlocksWithModels.Find(Block => Block.Name == _BlockName);

            return Defines.Status.Success;
        }

        static private Defines.Status SetupCache()
        {
            try {
                Directory.CreateDirectory(_AutomationToolsCachePath);

                // Create necessary directories
                Directory.CreateDirectory(Path.Combine(_AutomationToolsCachePath, "RVTs"));
                Directory.CreateDirectory(Path.Combine(_AutomationToolsCachePath, "NWCs"));
                Directory.CreateDirectory(Path.Combine(_AutomationToolsCachePath, "NWD"));
                Directory.CreateDirectory(Path.Combine(_AutomationToolsCachePath, "NWF"));
                Directory.CreateDirectory(Path.Combine(_AutomationToolsCachePath, "IFCs"));
            }
            catch { return Defines.Status.Error; }

            return Defines.Status.Success;
        }

        static private void CleanUpCache()
        {
            try
            {
                // Clean up the cache directory
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(_AutomationToolsCachePath, "IFCs")));
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(_AutomationToolsCachePath, "NWCs")));
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(_AutomationToolsCachePath, "NWD")));
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(_AutomationToolsCachePath, "NWF")));
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(_AutomationToolsCachePath, "RVTs")));
            }
            catch { }
        }

        static private void Exit()
        {
            LoggingSystem.Deinit();
            CleanUpCache();
            ReleaseMutex();
        }

        static private Defines.Status AutoDeploy(ref string[] args, string AutomationToolsRootPath)
        {
            LoggingSystem.WriteLine("[*] Режим AutoDeploy...");

            // Setup helper tasks for Revit versions
            if (args[1].Contains("HELPERTASK"))
            {
                LoggingSystem.WriteLine("[*] Настраиваем вспомогательные задачи...");

                // Проходимся по каждой версии, указанной в аргументах.
                // Получаем установленные версии из реестра.
                int RevitVersionIndex = 2;

                // Получаем корневой ключ Revit, здесь хранятся записи обо всех версиях, установленных в системе
                RegistryKey RevitRootKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Autodesk\\Revit");

                if (RevitRootKey == null)
                {
                    LoggingSystem.WriteLine("[-] В реестре не найден SOFTWARE\\Autodesk\\Revit");
                    return Defines.Status.Error;
                }
                    

                RegistryKey RevitVersionKey;
                string Path = "";
                object KeyValue;
                bool IsFound = false;

                while (RevitVersionIndex < args.Length)
                {
                    Path = "";
                    IsFound = false;

                    LoggingSystem.WriteLine("[*] Поиск Revit " + args[RevitVersionIndex] + "...");

                    // Получаем ключ Revit для заданной версии
                    RevitVersionKey = RevitRootKey.OpenSubKey(args[RevitVersionIndex]);

                    // Если его нет в системе - Revit не установлен
                    if (RevitVersionKey == null)
                    {
                        LoggingSystem.WriteLine("[-] Revit " + args[RevitVersionIndex] + " не установлен!");
                        RevitVersionIndex++;
                        continue;
                    }

                    foreach (string Key in RevitVersionKey.GetSubKeyNames())
                    {
                        if (Key.Contains("REVIT"))
                        {
                            KeyValue = RevitVersionKey.OpenSubKey(Key).GetValue("InstallationLocation");

                            if (KeyValue != null)
                            {
                                Path = (string)KeyValue;
                                IsFound = true;
                                break;
                            }
                        }
                    }

                    if (!IsFound)
                    {
                        LoggingSystem.WriteLine("[-] Revit " + args[RevitVersionIndex] + " не установлен!");
                        RevitVersionIndex++;
                        continue;
                    }

                    LoggingSystem.WriteLine("[+] Revit " + args[RevitVersionIndex] + " установлен!");

                    LoggingSystem.WriteLine("[*] Добавление вспомогательной задачи...");

                    // Проверяем, есть ли в системе задача с таким именем
                    if (TaskService.Instance.GetTask("Revit" + args[RevitVersionIndex] + "HelperStartTask") != null)
                    {
                        LoggingSystem.WriteLine("[*] Задача с именем Revit" + args[RevitVersionIndex] + "HelperStartTask уже существует в системе!");
                        RevitVersionIndex++;
                        continue;
                    }

                    TaskDefinition TaskDefinition = TaskService.Instance.NewTask();

                    TaskDefinition.RegistrationInfo.Description = "Запускает Revit " + args[RevitVersionIndex];
                    TaskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;
                    TaskDefinition.Actions.Add(new ExecAction(Path + "Revit.exe"));
                    TaskDefinition.Settings.MultipleInstances = TaskInstancesPolicy.Parallel;

                    EventTrigger EventTrigger = new EventTrigger();
                    EventTrigger.SetBasic("Application", "Application", Int32.Parse(args[RevitVersionIndex]));
                    TaskDefinition.Triggers.Add(EventTrigger);

                    TaskService.Instance.RootFolder.RegisterTaskDefinition("Revit" + args[RevitVersionIndex] + "HelperStartTask", TaskDefinition);

                    LoggingSystem.WriteLine("[+] Задача добавлена!");
                    RevitVersionIndex++;
                }
            }

            // Setup SignalDocs authorization
            if (args[1].Contains("SIGNALDOCSAUTH"))
            {
                LoggingSystem.WriteLine("[*] Настраиваем авторизацию Signal Docs...");

                if(File.Exists("RefreshToken.txt"))
                    File.Delete("RefreshToken.txt");

                AuthAndGetTokensModule.AuthAndGetTokensModuleMain(AutomationToolsRootPath); 
            }


            return Defines.Status.Success;
        }
    }
}
