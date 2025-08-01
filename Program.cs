using System;
using Microsoft.Win32.TaskScheduler;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using System.Collections.Generic;

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
        private static Config           _CurrentConfig;

        static int Main(string[] args)
        {
            string  IPAddressOfRevitServer;
            int     RevitAndNavisworksVersion;
            Dictionary<string, string> DestinationPaths = new Dictionary<string, string>(); // Содержит пути назначения для NWC, NWD, NWF, IFC, RVT файлов
            string  AutomationToolsRootPath;
            string  RevitPath;
 
            string  FileToolsTaskRunnerPath;
            bool    ShouldDeleteUPDATEFile;

            Defines.Status Status;

            // Стартуем...
            Console.WriteLine("[*] Запуск инструметов автоматизации...");
            Console.InputEncoding = Encoding.GetEncoding(1251);

            // Получаем корневой каталог AutomationTools
            AutomationToolsRootPath = Directory.GetCurrentDirectory();

            // Анализ аргументов
            if (args.Length == 0 || args[0] == "help")
            {
                Console.WriteLine("[!] Режим автоматического первоначального развёртывания: \n \n" +
                    "           .\\AutomationTools.exe AutoDeploy <MODE> <Revit versions>\n \n" +
                    "               <MODE> - определяет то, что следует настроить\n" +
                    "                   DIR - настроить необходимые директории\n" +
                    "                   CONFIG - переместить первоначальные файлы конфигурации в нужную директорию\n" +
                    "                   HELPERTASK - настроить вспомогательные задачи\n \n" +
                    "               <Revit versions> - определяет то, для каких версий Revit будет настроено окружение\n \n" +
                    "           Н-р: .\\AutomationTools.exe AutoDeploy DIR_CONFIG_HELPERTASK 2022 2021\n ");

                Console.WriteLine("[!] Рабочий режим: \n \n" +
                    "           .\\AutomationTools.exe <BlockName> <ConfigFileName> <ExportFormat> \n \n" +
                    "               <BlockName> - определяет имя блока, который нужно обработать\n\n" +
                    "               <ConfigFileName> - определяет имя файла конфигурации\n\n" +
                    "               <ExportFormat> - определяет форматы, в которые нужно экспортировать\n" +
                    "                   NW - экспорт в Navisworks форматы\n" +
                    "                   RVT - загрузка .rvt с сервера\n" +
                    "                   IFC - экспорт в IFC формат\n\n" +
                    //"               <DestinationPath> - необязательный аргумент, определяет путь, куда следует переместить готовые файлы. Если неопределён, то используется путь из конфигурационного файла\n \n" +
                    "           Н-р: .\\AutomationTools.exe Ocean100M Configs\\Config_2022.bconf NW_IFC\n ");

                return 0;
            }

            if (args.Length < 3)
            {
                Console.WriteLine("[-] Недопустимые параметры, прерывание...");
                Console.WriteLine("[!] Проверьте вводимые параметры. Для справки: .\\AutomationTools.exe help");
                return -1;
            }

            // Здесь имеем важное разветвление - режим AutoDeploy
            // Он позволяет инструменту разворачиваться в автоматическом режиме
            if (args[0] == "AutoDeploy")
            {
                if (args.Length < 3)
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

            _BlockName = args[0];

            // Анализ .bconf файла
            if(ConfigFormatFile.ConfigFileLoadAndParse(args[1], ref _CurrentConfig) != Defines.Status.Success)
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

            // Чистим кэш
            Console.WriteLine("[*] Отчистка кэша...");
            ClearCache(AutomationToolsRootPath + "\\Cache");

            // Ищем блок с моделями 
            if (SearchForBlockWithModels() != Defines.Status.Success)
            {
                Console.WriteLine("[-] Подходящий блок с моделями не найден, прерывание...");
                Console.WriteLine("[!] Проверьте, правильно ли написано имя блока, а также существует ли блок с таким именем в конфигурационном файле.");
                return -1;
            }

            Console.WriteLine("[+] Блок с моделями найден!");

            // Выполняем распределение моделей по ядрам для экспорта с Revit Server
            if (DistributeModels() != Defines.Status.Success)
            {
                Console.WriteLine("[-] Не удалось выполнить распределение моделей по ядрам, прерывание...");
                Console.WriteLine("[!] Вероятно, ошибка возникла из-за неправильного значения количества рабочих ядер в конфигурационном файле (н-р: значение 0)");
                return -1;
            }

            Console.WriteLine("[+] Распределение моделей по ядрам выполнено!");

            // Создаём папку под логи
            Directory.CreateDirectory(AutomationToolsRootPath + "\\Cache\\Logs\\" + _BlockName);

            // Экспорт моделей с Revit Server
            Status = ExportModelsFromRevitServer.ExportModelsFromRevitServerMain(_BlockName,
                                                                                 IPAddressOfRevitServer, 
                                                                                 RevitPath + "\\RevitServerToolCommand", 
                                                                                 AutomationToolsRootPath + "\\Cache",
                                                                                 _NumberOfWorkingCores,
                                                                                 ref _BufferForModelReferences,
                                                                                 ref _ModelIteratorForEachCPUCore);
            // Если загрузка не удалась, то выходим
            if (Status != Defines.Status.Success)
                return -1;
            

            if (args[2].Contains("RVT"))
            {
                // Этот режим просто позволяет сохранить загруженные RVT в указанный каталог

                Console.WriteLine("[*] Копируем загруженные .RVT в указанный каталог...");

                var RVTDestinationPath = DestinationPaths["RVT"];

                // Проходимся по массиву, нам нужны только пути к моделям
                string PathToModel = string.Empty;
                
                for (int i = 0; i < _BufferForModelReferences.GetLength(0); i++)
                {
                    for (int j = 0; j < _BufferForModelReferences.GetLength(1); j++)
                    {
                        PathToModel = _BufferForModelReferences[i, j, 0];

                        if (PathToModel == null)
                            continue;

                        System.IO.File.Copy(AutomationToolsRootPath + "\\Cache\\RVTs\\" + PathToModel, RVTDestinationPath + "\\" + _BlockName + "\\" + PathToModel.Substring(PathToModel.LastIndexOf("\\")));
                    }
                }        
            }


            if (args[2].Contains("NW"))
            {
                // Выполним повторное распределение моделей по ядрам,
                // если количество ядер для экспорта моделей с Revit Server не равно количеству ядер для генерации NW файлов

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

                // Извлекаем нужные виды из .rvt в целевой папке и создаём NWD/NWF
                Status = NavisworksFilesGeneration.NavisworksFilesGenerationMain(ref _BufferForModelReferences,
                                                                                 RevitAndNavisworksVersion,
                                                                                 _NumberOfWorkingCores,
                                                                                 ref _ModelIteratorForEachCPUCore,
                                                                                 _BlockName,
                                                                                 AutomationToolsRootPath + "\\Cache",
                                                                                 FileToolsTaskRunnerPath,
                                                                                 DestinationPaths,
                                                                                 ShouldDeleteUPDATEFile);

                // Если генерация не удалась, то выходим
                if (Status != Defines.Status.Success)
                    return -1;
            }


            if (args[2].Contains("IFC"))
            {
                // Выполним повторное распределение моделей по ядрам,
                // если количество ядер для генерации NW файлов не равно количеству ядер для генерации IFC файлов

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

                // Преобразуем .RVT -> .IFC
                Status = IFСFilesGeneration.IFСFilesGenerationMain(ref _BufferForModelReferences,
                                                                   RevitAndNavisworksVersion,
                                                                   _NumberOfWorkingCores,
                                                                   ref _ModelIteratorForEachCPUCore,
                                                                   _BlockName,
                                                                   AutomationToolsRootPath + "\\Cache",
                                                                   DestinationPaths);

                // Если генерация не удалась, то выходим
                if (Status != Defines.Status.Success)
                    return -1;
            }

            Console.WriteLine("[*] Завершение работы инструметов автоматизации...");

            return 0;
        }

        static public Defines.Status DistributeModels()
        {
            // Ловим неверную конфигурацию ядер
            if (_NumberOfWorkingCores <= 0)
            {
                return Defines.Status.Error;
            }

            // Получаем количество моделей в блоке

            // Может произойти неприятная ситуация, когда в конфигурации будет допущена ошибка, будет не хватать пути к модели или пути к пункту назначения
            // Ловим эту ситуацию
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

            // Распределяем модели по рабочим ядрам
            // (NumberOfModelsInBlock умножаем на 2, потому что нам нужно итерироваться не только по моделям, но и по путям назначения, а потом вычитаем 1, чтобы не допустить выход за пределы листа)
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

            // Считаем количество реально задействованных ядер после распределения моделей
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

        static public void ClearCache(string CachePath)
        {
            // Отчищаем кэш
            try
            {
                foreach (string Directory in Directory.GetDirectories(CachePath)) {
                    Utils.RecursiveDelete(new DirectoryInfo(Directory));
                }
            }
            catch
            {
                Directory.CreateDirectory(CachePath);
            }
            

            // Создаём новые папки 
            Directory.CreateDirectory(CachePath + "\\RVTs");
            Directory.CreateDirectory(CachePath + "\\NWCs");
            Directory.CreateDirectory(CachePath + "\\NWD");
            Directory.CreateDirectory(CachePath + "\\NWF");
            Directory.CreateDirectory(CachePath + "\\IFCs");
        }

        public static Defines.Status AutoDeploy(ref string[] args, string AutomationToolsRootPath)
        {
            Console.WriteLine("[*] Режим AutoDeploy...");

            // Удаляем старые папки и создаём новые, если дана такая инструкция
            if (args[1].Contains("DIR"))
            {
                Console.WriteLine("[*] Разбираемся с директориями...");
                Utils.RecursiveDelete(new DirectoryInfo(AutomationToolsRootPath + "\\Cache"));
                Utils.RecursiveDelete(new DirectoryInfo(AutomationToolsRootPath + "\\Configs"));

                Directory.CreateDirectory(AutomationToolsRootPath + "\\Cache");
                Directory.CreateDirectory(AutomationToolsRootPath + "\\Configs");
            }

            // Далее проверим, есть ли в корневой папке инструмента файлы конфигурации. Если есть - перемещаем их в каталог Configs
            if (args[1].Contains("CONFIG"))
            {
                Console.WriteLine("[*] Перемещаем файлы конфигурации...");
                FileInfo FileInfo;
                foreach (string File in Directory.EnumerateFiles(AutomationToolsRootPath))
                {
                    FileInfo = new FileInfo(File);
                    if (FileInfo.Name.Contains("Config_"))
                    {
                        System.IO.File.Move(FileInfo.Name, AutomationToolsRootPath + "\\Configs\\" + FileInfo.Name);
                    }
                }
            }

            // Настроим вспомогательные задачи
            if (args[1].Contains("HELPERTASK"))
            {
                Console.WriteLine("[*] Настраиваем вспомогательные задачи...");

                // Проходимся по каждой версии, указанной в аргументах.
                // Самое важное в этом разделе, что информацию об установленных версиях Revit нужно получать из реестра.
                // Потому что если позволить пользователю указывать путь, где должны находится установленные версии,
                // то недобросовестный пользователь может подменить путь до настоящего файла нелегетимным путём и
                // добится выполнения произвольного кода, когда AutomationTools попытается запустить Revit.
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

            return Defines.Status.Success;
        }
    }
}
