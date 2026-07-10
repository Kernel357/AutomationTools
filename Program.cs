using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AutomationTools
{
    internal class MainProgram
    {
        private static string[,,]       _BufferForModelReferences = null;
        private static int[]            _ModelIteratorForEachCPUCore = null;
        private static int              _NumberOfWorkingCores;
        private static int              _NumberOfBlocksWithModels;
        private static string           _BlockName;
        private static BlockWithModels  _BlockWithModels;
        private static string           _CompanyName;
        private static string           _ProjectName;
        private static Config           _CurrentConfig;
        

        [STAThread]
        static int Main(string[] args)
        {
            string                      IPAddressOfRevitServer;
            int                         RevitAndNavisworksVersion;
            Dictionary<string, string>  DestinationPaths = new Dictionary<string, string>(); // Destination paths for NWC, NWD, NWF, IFC, RVT
            string                      AutomationToolsRootPath;
            string                      AutomationToolsCachePath;
            string                      RevitPath;

            string                      FileToolsTaskRunnerPath;
            bool                        ShouldDeleteUPDATEFile;

            Defines.Status              Status;

            // Starting...
            Console.WriteLine("[*] Запуск инструметов автоматизации...");
            Console.InputEncoding = Encoding.GetEncoding(1251);

            // Get the root path of the AutomationTools
            AutomationToolsRootPath = Directory.GetCurrentDirectory();

            // Analyze command line arguments
            if (args.Length == 0 || args[0] == "help")
            {
                Console.WriteLine("[!] Режим автоматического первоначального развёртывания: \n \n" +
                    "           .\\AutomationTools.exe AutoDeploy <MODE> <Revit versions>\n \n" +
                    "               <MODE> - определяет режим развёртывания\n" +
                    "                   HELPERTASK - настроить вспомогательные задачи\n" +
                    "                   SIGNALDOCSAUTH - позволяет настроить авторизацию Signal Docs\n" +
                    "               <Revit versions> - определяет версии Revit, для которых будет настроено окружение\n \n" +
                    "           Н-р: .\\AutomationTools.exe AutoDeploy DIR_CONFIG_HELPERTASK 2022 2021\n ");

                Console.WriteLine("[!] Рабочий режим: \n \n" +
                    "           .\\AutomationTools.exe <BlockName> <ConfigFileName> <MODE> \n \n" +
                    "               <BlockName> - определяет имя блока, который нужно обработать\n\n" +
                    "               <ConfigFileName> - определяет имя файла конфигурации\n\n" +
                    "               <MODE> - определяет режим работы\n" +
                    "                   NW - экспорт в Navisworks форматы\n" +
                    "                   RVT - загрузка .rvt с сервера\n" +
                    "                   IFC - экспорт в IFC формат\n" +
                    "                   SD - производит загрузку моделей на сервер и запускает обработку Autodesk Forge\n\n" +
                    "           Н-р: .\\AutomationTools.exe Ocean100M Configs\\Config_2022.bconf NW_IFC\n ");

                return 0;
            }

            // AutoDeploy Mode
            if (args[0] == "AutoDeploy")
            {
                if (args.Length < 3 && !args[1].Equals("SIGNALDOCSAUTH"))
                {
                    Console.WriteLine("[-] Недопустимые параметры, прерывание...");
                    Console.WriteLine("[!] Проверьте вводимые параметры. Для справки: .\\AutomationTools.exe help");
                    return -1;
                }

                if (AutoDeploy(ref args, AutomationToolsRootPath) != Defines.Status.Success)
                {
                    Console.WriteLine("[-] Во время развёртывания произошла ошибка, прерывание...");
                    Console.WriteLine("[!] Вероятно, произошла внутренняя ошибка, обратитесь к разработчику.");
                    return -1;
                }

                Console.WriteLine("[+] Развёртывание завершено!");
                return 0;
            }

            // Check args
            if (args.Length < 3)
            {
                Console.WriteLine("[-] Недопустимые параметры, прерывание...");
                Console.WriteLine("[!] Проверьте вводимые параметры. Для справки: .\\AutomationTools.exe help");
                return -1;
            }

            _BlockName = args[0];

            // Analyze configuration file
            if (ConfigFormatFile.ConfigFileLoadAndParse(args[1], ref _CurrentConfig) != Defines.Status.Success)
            {
                Console.WriteLine("[-] Конфигурационный файл не найден или не является действительным, прерывание...");
                Console.WriteLine("[!] Проверьте правильность пути к файлу конфигураций");
                return -1;
            }

            IPAddressOfRevitServer = _CurrentConfig.IPAddressOfRevitServer;
            RevitAndNavisworksVersion = _CurrentConfig.RevitAndNavisworksVersion;
            RevitPath = _CurrentConfig.RevitPath;
            FileToolsTaskRunnerPath = _CurrentConfig.FileToolsTaskRunnerPath;
            ShouldDeleteUPDATEFile = _CurrentConfig.ShouldDeleteUPDATEFile;

            DestinationPaths.Add("NWC", _CurrentConfig.NWCDestinationPath);
            DestinationPaths.Add("NWD", _CurrentConfig.NWDDestinationPath);
            DestinationPaths.Add("NWF", _CurrentConfig.NWFDestinationPath);
            DestinationPaths.Add("IFC", _CurrentConfig.IFCDestinationPath);
            DestinationPaths.Add("RVT", _CurrentConfig.RVTDestinationPath);

            _NumberOfWorkingCores = _CurrentConfig.NumberOfWorkingCoresToExportFromRevitServer;

            _CompanyName = _CurrentConfig.CompanyName;
            _ProjectName = _CurrentConfig.ProjectName;

            Console.WriteLine("[*] IP адрес Revit Server: {0}", IPAddressOfRevitServer);
            Console.WriteLine("[*] Версия Revit и Navisworks: {0}", RevitAndNavisworksVersion);
            Console.WriteLine("[*] Путь расположения AutomationTools: {0}", AutomationToolsRootPath);
            Console.WriteLine("[*] Путь к Revit: {0}", RevitPath);
            Console.WriteLine("[*] Путь к FileToolsTaskRunner: {0}", FileToolsTaskRunnerPath);
            Console.WriteLine("[*] Путь назначения для NWC файлов: {0}", DestinationPaths["NWC"]);
            Console.WriteLine("[*] Путь назначения для NWD файлов: {0}", DestinationPaths["NWD"]);
            Console.WriteLine("[*] Путь назначения для NWF файлов: {0}", DestinationPaths["NWF"]);
            Console.WriteLine("[*] Путь назначения для IFC файлов: {0}", DestinationPaths["IFC"]);
            Console.WriteLine("[*] Путь назначения для RVT файлов: {0}", DestinationPaths["RVT"]);
            if (ShouldDeleteUPDATEFile == true)
                Console.WriteLine("[*] Что следует делать с файлом UPDATE: удалить");
            else
                Console.WriteLine("[*] Что следует делать с файлом UPDATE: оставить");
            Console.WriteLine("[*] Количество рабочих ядер для экспорта моделей с Revit Server: {0}", _NumberOfWorkingCores);

            _NumberOfBlocksWithModels = _CurrentConfig.BlocksWithModels.Count;


            // Setup cache directory
            AutomationToolsCachePath = Path.Combine(AutomationToolsRootPath, "Cache", Guid.NewGuid().ToString());
            if (SetupCache(AutomationToolsCachePath) != Defines.Status.Success)
            {
                Console.WriteLine("[-] Не удалось настроить кэш, прерывание...");
                return -1;
            }

            // Search for the block with models in the configuration
            if (SearchForBlockWithModels() != Defines.Status.Success)
            {
                Console.WriteLine("[-] Подходящий блок с моделями не найден, прерывание...");
                Console.WriteLine("[!] Проверьте, правильно ли написано имя блока, а также существует ли блок с таким именем в конфигурационном файле.");
                return -1;
            }

            // Distribute models across CPU cores
            if (DistributeModels() != Defines.Status.Success)
            {
                Console.WriteLine("[-] Не удалось выполнить распределение моделей по ядрам, прерывание...");
                Console.WriteLine("[!] Вероятно, ошибка возникла из-за неправильного значения количества рабочих ядер в конфигурационном файле (н-р: значение 0)");
                return -1;
            }

            // Create the folder for logs
            Directory.CreateDirectory(Path.Combine(AutomationToolsCachePath, "Logs", _BlockName));

            // Export models from Revit Server
            Status = ExportModelsFromRevitServer.ExportModelsFromRevitServerMain(_BlockName,
                                                                                 IPAddressOfRevitServer,
                                                                                 $"{RevitPath}\\RevitServerToolCommand",
                                                                                 AutomationToolsCachePath,
                                                                                 _NumberOfWorkingCores,
                                                                                 ref _BufferForModelReferences,
                                                                                 ref _ModelIteratorForEachCPUCore);
            
            // If the status is failure exit
            if (Status != Defines.Status.Success)
                return -1;
            

            if (args[2].Contains("RVT"))
            {
                // This mode moves the RVT files to destination folder
                Console.WriteLine("[*] Копируем загруженные .RVT в указанный каталог...");

                var RVTDestinationPath = DestinationPaths["RVT"];
                string PathToModel = string.Empty;

                try
                {
                    // Create destination folder
                    System.IO.Directory.CreateDirectory($"{RVTDestinationPath}\\{_BlockName}");

                    for (int i = 0; i < _BufferForModelReferences.GetLength(0); i++)
                    {
                        for (int j = 0; j < _BufferForModelReferences.GetLength(1); j++)
                        {
                            PathToModel = _BufferForModelReferences[i, j, 0];

                            if (PathToModel == null)
                                continue;

                            File.Copy($"{AutomationToolsCachePath}\\RVTs\\{_BlockName}\\{PathToModel}",
                                      $"{RVTDestinationPath}\\{_BlockName}\\{PathToModel.Substring(PathToModel.LastIndexOf("\\"))}");
                        }
                    }
                }
                catch
                {
                    Console.WriteLine("[-] Не удалось скопировать файлы, прерывание...");
                    return -1;
                }
            }

            if (args[2].Contains("NW"))
            {
                // Distribute models across CPU cores again if the number of cores for exporting models from Revit Server is not equal to the number of cores for generating NW files
                if (_NumberOfWorkingCores != _CurrentConfig.NumberOfWorkingCoresForGenerationNWFiles)
                {
                    _NumberOfWorkingCores = _CurrentConfig.NumberOfWorkingCoresForGenerationNWFiles;

                    if (DistributeModels() != Defines.Status.Success)
                    {
                        Console.WriteLine("[-] Не удалось выполнить распределение моделей по ядрам, прерывание...");
                        Console.WriteLine("[!] Вероятно, ошибка возникла из-за неправильного значения количества рабочих ядер в конфигурационном файле (н-р: значение 0).");
                        return -1;
                    }
                }

                Console.WriteLine("[*] Количество рабочих ядер для генерации NW файлов: {0}", _NumberOfWorkingCores);

                // RVT -> NWC, NWD, NWF
                Status = NavisworksFilesGeneration.FilesGenerationMain(ref _BufferForModelReferences,
                                                                       RevitAndNavisworksVersion,
                                                                       _NumberOfWorkingCores,
                                                                       ref _ModelIteratorForEachCPUCore,
                                                                       _BlockName,
                                                                       AutomationToolsCachePath,
                                                                       FileToolsTaskRunnerPath,
                                                                       DestinationPaths,
                                                                       ShouldDeleteUPDATEFile);

                // If the generation failed, exit
                if (Status != Defines.Status.Success)
                    return -1;
            }

            if (args[2].Contains("IFC"))
            {
                // Distribute models across CPU cores again if the number of cores for exporting models from Revit Server is not equal to the number of cores for generating IFC files
                if (_NumberOfWorkingCores != _CurrentConfig.NumberOfWorkingCoresForGenerationIFCFiles)
                {
                    _NumberOfWorkingCores = _CurrentConfig.NumberOfWorkingCoresForGenerationIFCFiles;

                    if (DistributeModels() != Defines.Status.Success)
                    {
                        Console.WriteLine("[-] Не удалось выполнить распределение моделей по ядрам, прерывание...");
                        Console.WriteLine("[!] Вероятно, ошибка возникла из-за неправильного значения количества рабочих ядер в конфигурационном файле (н-р: значение 0).");
                        return -1;
                    }
                }

                Console.WriteLine("[*] Количество рабочих ядер для генерации IFC файлов: {0}", _NumberOfWorkingCores);

                // RVT -> IFC
                Status = IFСFilesGeneration.FilesGenerationMain(ref _BufferForModelReferences,
                                                                RevitAndNavisworksVersion,
                                                                _NumberOfWorkingCores,
                                                                ref _ModelIteratorForEachCPUCore,
                                                                _BlockName,
                                                                AutomationToolsCachePath,
                                                                DestinationPaths);

                // If the generation failed, exit
                if (Status != Defines.Status.Success)
                    return -1;
            }

            if (args[2].Contains("SD"))
            {
                AuthAndGetTokensModule.AuthAndGetTokensModuleMain(AutomationToolsRootPath);

                Console.WriteLine("[*] Загружаем NWD файл на сервер Signal Docs...");
                Status = SignalDocsCommands.UploadFilesToServer(AuthAndGetTokensModule.AccessToken, 
                                                                _CompanyName, 
                                                                _ProjectName,
                                                                $"{DestinationPaths["NWD"].Split(new string[] { $"{_ProjectName}\\" }, StringSplitOptions.RemoveEmptyEntries)[1]}\\{_BlockName}",
                                                                $"{DestinationPaths["NWD"]}\\{_BlockName}",
                                                                $"{_BlockName}.nwd");

                Console.WriteLine("[*] Статус загрузки файла: {0}", Status);

                Console.WriteLine("[*] Запускаем Autodesk Forge конвертацию...");
                Status = SignalDocsCommands.StartAutodeskForgeProcessing(AuthAndGetTokensModule.AccessToken, 
                                                                        _CompanyName, 
                                                                        _ProjectName,
                                                                        $"{DestinationPaths["NWD"].Split(new string[] { $"{_ProjectName}\\" }, StringSplitOptions.RemoveEmptyEntries)[1]}\\{_BlockName}",
                                                                        $"{_BlockName}.nwd");
                Console.WriteLine("[*] Статус запуска конвертации: {0}", Status);
            }

            // Clean up the cache directory
            Utils.RecursiveDelete(Directory.GetParent(AutomationToolsCachePath));

            Console.WriteLine("[*] Завершение работы инструметов автоматизации...");

            return 0;
        }

        static public Defines.Status DistributeModels()
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
                Console.WriteLine("[-] В блоке не нашлось ни одной модели!");
                Console.WriteLine("[!] Вам следует добавить в блок с указанным названием пути к моделям и пути пунктов назначения.");
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
                    Console.WriteLine("[*] Количество рабочих ядер: {0}", _NumberOfWorkingCores);
                    break;
                }

                CPUCoreIterator++;
            }

            return Defines.Status.Success;
        }

        static public Defines.Status SearchForBlockWithModels()
        {
            bool BlockFound = _CurrentConfig.BlocksWithModels.Any(Block => Block.Name == _BlockName);

            if (BlockFound != true)
                return Defines.Status.Error;

            _BlockWithModels = _CurrentConfig.BlocksWithModels.Find(Block => Block.Name == _BlockName);

            return Defines.Status.Success;
        }

        static public Defines.Status SetupCache(string CachePath)
        {
            try {
                Directory.CreateDirectory(CachePath);

                // Create necessary directories
                Directory.CreateDirectory(CachePath + "\\RVTs");
                Directory.CreateDirectory(CachePath + "\\NWCs");
                Directory.CreateDirectory(CachePath + "\\NWD");
                Directory.CreateDirectory(CachePath + "\\NWF");
                Directory.CreateDirectory(CachePath + "\\IFCs");
            }
            catch { return Defines.Status.Error; }

            return Defines.Status.Success;
        }

        public static Defines.Status AutoDeploy(ref string[] args, string AutomationToolsRootPath)
        {
            Console.WriteLine("[*] Режим AutoDeploy...");

            // Setup helper tasks for Revit versions
            if (args[1].Contains("HELPERTASK"))
            {
                Console.WriteLine("[*] Настраиваем вспомогательные задачи...");

                // Проходимся по каждой версии, указанной в аргументах.
                // Получаем установленные версии из реестра.
                int RevitVersionIndex = 2;

                // Получаем корневой ключ Revit, здесь хранятся записи обо всех версиях, установленных в системе
                RegistryKey RevitRootKey = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Autodesk\\Revit");

                if (RevitRootKey == null)
                {
                    Console.WriteLine("[-] В реестре не найден SOFTWARE\\Autodesk\\Revit");
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

                    Console.WriteLine("[*] Поиск Revit " + args[RevitVersionIndex] + "...");

                    // Получаем ключ Revit для заданной версии
                    RevitVersionKey = RevitRootKey.OpenSubKey(args[RevitVersionIndex]);

                    // Если его нет в системе - Revit не установлен
                    if (RevitVersionKey == null)
                    {
                        Console.WriteLine("[-] Revit " + args[RevitVersionIndex] + " не установлен!");
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
                        Console.WriteLine("[-] Revit " + args[RevitVersionIndex] + " не установлен!");
                        RevitVersionIndex++;
                        continue;
                    }

                    Console.WriteLine("[+] Revit " + args[RevitVersionIndex] + " установлен!");

                    Console.WriteLine("[*] Добавление вспомогательной задачи...");

                    // Проверяем, есть ли в системе задача с таким именем
                    if (TaskService.Instance.GetTask("Revit" + args[RevitVersionIndex] + "HelperStartTask") != null)
                    {
                        Console.WriteLine("[*] Задача с именем Revit" + args[RevitVersionIndex] + "HelperStartTask уже существует в системе!");
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

                    Console.WriteLine("[+] Задача добавлена!");
                    RevitVersionIndex++;
                }
            }

            // Setup SignalDocs authorization
            if (args[1].Contains("SIGNALDOCSAUTH"))
            {
                Console.WriteLine("[*] Настраиваем авторизацию Signal Docs...");

                if(File.Exists("RefreshToken.txt"))
                    File.Delete("RefreshToken.txt");

                AuthAndGetTokensModule.AuthAndGetTokensModuleMain(AutomationToolsRootPath); 
            }

            return Defines.Status.Success;
        }
    }
}
