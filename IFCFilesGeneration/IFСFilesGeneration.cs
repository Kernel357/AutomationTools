using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AutomationTools
{
    class IFСFilesGeneration 
    {
        private static string[,,]           _BufferForModelReferences;
        private static int[]                _ModelIteratorForEachCPUCore;
        private static string               _AutomationToolsCachePath;
        private static string               _BlockName;
        private static Defines.Status       _Status;

        private static void FilesGenerationWorker(object Param)
        {
            Defines.Status Status = Defines.Status.Success;
            string StatusMessage = string.Empty;

            int ThreadNumber = (int)Param;

            string DocumentName = string.Empty;

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

                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Статус открытия документа: {ThreadNumber}");

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
                {
                    if (Status != Defines.Status.Success)
                        LoggingSystem.WriteLine($"[-] Сообщение об ошибке: {StatusMessage}");

                    _Status = Defines.Status.Error;
                    break;
                }


                // Экспортируем документ в IFC
                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Экспорт в IFC...");

                Status = WCFService.RunTask(ThreadNumber,
                                            "ExportToIFC",
                                            ref StatusMessage,
                                            new string[] { $"{_AutomationToolsCachePath}\\IFCs\\{_BlockName}" });

                LoggingSystem.WriteLine($"[*] Поток {ThreadNumber}: Статус экспорта в IFC: {Status}");

                // После окончания операции проверим глобальный и локальный статус
                if (Status != Defines.Status.Success || _Status != Defines.Status.Success)
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

        public static Defines.Status FilesGenerationMain(ref string[,,]                  BufferForModelReferences,
                                                         int                             RevitAndNavisworksVersion,
                                                         int                             NumberOfWorkingCores,
                                                         ref int[]                       ModelIteratorForEachCPUCore,
                                                         string                          BlockName,
                                                         string                          AutomationToolsCachePath,
                                                         Dictionary<string, string>      DestinationPaths)
        {
            LoggingSystem.WriteLine("[*] Генерация IFC файлов...");

            _BufferForModelReferences = BufferForModelReferences;
            _ModelIteratorForEachCPUCore = ModelIteratorForEachCPUCore;
            _AutomationToolsCachePath = AutomationToolsCachePath;
            _BlockName = BlockName;
            _Status = Defines.Status.Success;

            // Инициализируем систему связи с Revit
            if (WCFService.InitializeСonnection(NumberOfWorkingCores, RevitAndNavisworksVersion) != Defines.Status.Success)
                return Defines.Status.Error;

            // Создаём папку для экспорта в ICF
            Directory.CreateDirectory(Path.Combine(AutomationToolsCachePath, "IFCs", BlockName));

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
            if (_Status != Defines.Status.Success)
                return _Status;


            // Проверяем доступность диска и директории пункта назначения
            string IFCDestinationPath = DestinationPaths["IFC"];
            if(string.IsNullOrEmpty(IFCDestinationPath))
            {
                LoggingSystem.WriteLine("[-] Директория для IFC не задана, прерывание...");
                return Defines.Status.Error;
            }

            if (new DirectoryInfo(IFCDestinationPath).Exists == false)
            {
                LoggingSystem.WriteLine("[-] Директория пункта назначения недоступна...");
                return Defines.Status.Error;
            }

            // Выставляем флаг FirstGeneration, если это первая генерация для указанного блока
            bool FirstGeneration = false;
            if (new DirectoryInfo(Path.Combine(IFCDestinationPath, BlockName)).Exists == false)
                FirstGeneration = true;

            // Если это уже не первая генерация - отчищаем старые файлы
            if (FirstGeneration == false)
                Utils.RecursiveDelete(new DirectoryInfo(Path.Combine(IFCDestinationPath, BlockName)));

            Directory.CreateDirectory(Path.Combine(IFCDestinationPath, BlockName));


            // Копируем все IFC в пункт назначения
            var Files = new DirectoryInfo(Path.Combine(AutomationToolsCachePath, "IFCs", BlockName)).GetFiles();
            foreach (var File in Files)
            {
                if(File.Name.EndsWith(".ifc") == true)
                    System.IO.File.Copy(File.FullName, Path.Combine(IFCDestinationPath, BlockName, File.Name));
            }

            return Defines.Status.Success;
        }
    }
}
