using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace AutomationTools
{
    internal class ExportModelsFromRevitServer
    {
        private static string       _BlockName;
        private static string[,,]   _BufferForModelReferences;
        private static string       _IPAddressOfRevitServer;
        private static string       _RevitServerToolPath;
        private static int[]        _ModelIteratorForEachCPUCore;
        private static string       _AutomationToolsCachePath;

        private static Defines.Status _Status = Defines.Status.Success;

        private static void ExportModelsFromRevitServerWorker(object Param)
        {
            Process ExportProcess;
            int ThreadNumber = (int)Param;
            
            StreamWriter StreamWriter = new StreamWriter($"{_AutomationToolsCachePath}\\Logs\\LogExportFromRevitServer_{ThreadNumber.ToString()}.log", true);

            for (int i = 0; i < _ModelIteratorForEachCPUCore[ThreadNumber]; i++)
            {
                // Check global status
                if (_Status != Defines.Status.Success)
                {
                    StreamWriter.Close();
                    return;
                }

                // Start RevitServerTool process
                ExportProcess = new Process {
                    StartInfo = new ProcessStartInfo {
                        FileName = $"{_RevitServerToolPath}\\RevitServerTool.exe",
                        Arguments = $"L \"{_BufferForModelReferences[ThreadNumber, i, 0]}\" -s {_IPAddressOfRevitServer} -d \" {_AutomationToolsCachePath}\\RVTs\\{_BlockName + _BufferForModelReferences[ThreadNumber, i, 1]}\" -o",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                ExportProcess.Start();
                ExportProcess.WaitForExit();

                StreamWriter.WriteLine("Экспорт с Revit Server, файл: {0}", _BufferForModelReferences[ThreadNumber, i, 0]);
                StreamWriter.WriteLine(ExportProcess.StandardOutput.ReadToEnd());
                StreamWriter.WriteLine();

                // Check if the file exist 
                if (!File.Exists($"{_AutomationToolsCachePath}\\RVTs\\{_BlockName}\\{_BufferForModelReferences[ThreadNumber, i, 0]}"))
                {
                    _Status = Defines.Status.Error;
                    StreamWriter.Close();
                    return;
                }    
            }

            StreamWriter.Close();
            return;
        }

        public static Defines.Status ExportModelsFromRevitServerMain(string         BlockName,
                                                                     string         IPAddressOfRevitServer,
                                                                     string         RevitServerToolPath,
                                                                     string         AutomationToolsCachePath,
                                                                     int            NumberOfWorkingCores,
                                                                     ref string[,,] BufferForModelReferences,
                                                                     ref int[]      ModelIteratorForEachCPUCore)
        {
            LoggingSystem.WriteLine("[*] Загрузка моделей с Revit Server...");
                
            _BlockName = BlockName;
            _BufferForModelReferences = BufferForModelReferences;
            _IPAddressOfRevitServer = IPAddressOfRevitServer;
            _RevitServerToolPath = RevitServerToolPath;
            _AutomationToolsCachePath = AutomationToolsCachePath;
            _ModelIteratorForEachCPUCore = ModelIteratorForEachCPUCore;

            // Start threads for export
            List<Thread> Threads = new List<Thread>();

            for (int i = 0; i < NumberOfWorkingCores; i++)
            {
                Threads.Add(new Thread(ExportModelsFromRevitServerWorker));
                Threads[i].Start(i);
                LoggingSystem.WriteLine($"[+] Поток {i} запущен!");
            }

            // Если нужное количество потоков не было запущено, то выходим
            if (Threads.Count() != NumberOfWorkingCores)
            {
                // Завершаем потоки, которые уже успели запуститься
                foreach (var Thread in Threads)
                {
                    Thread.Abort();
                }

                LoggingSystem.WriteLine("[-] Возникла ошибка при запуске потоков, прерывание...");
                LoggingSystem.WriteLine("[!] Вероятно, произошла внутренняя ошибка, обратитесь к разработчику.");
                return Defines.Status.Error;
            }
                
            // Ждём завершения загрузки моделей
            int ThreadIterator = 0;

            while (ThreadIterator < NumberOfWorkingCores)
            {
                if (Threads[ThreadIterator].IsAlive != false)
                    continue;

                LoggingSystem.WriteLine($"[+] Поток {ThreadIterator} завершён!");
                ThreadIterator++;
            }

            if (_Status != Defines.Status.Success)
            {
                LoggingSystem.WriteLine("[-] Во время загрузки моделей с Revit Server возникла ошибка, прерывание...");
                LoggingSystem.WriteLine("[!] Вы можете найти логи загрузки по пути ..\\Cache\\<GUID>\\Logs\\.");
                return Defines.Status.Error;
            }

            LoggingSystem.WriteLine("[+] Загрузка моделей с Revit Server завершена!");
            return Defines.Status.Success;
        }
    }
}
