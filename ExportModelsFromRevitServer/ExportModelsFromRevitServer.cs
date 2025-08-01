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
            StreamWriter StreamWriter = new StreamWriter(_AutomationToolsCachePath + "\\Logs\\" + _BlockName + "\\LogExportFromRevitServer_" + ThreadNumber.ToString() + ".log", true);

            for (int i = 0; i < _ModelIteratorForEachCPUCore[ThreadNumber]; i++)
            {
                // Проверяем глобальный статус экспорта, вдруг другой поток изменил статус, тогда нам нет смысла продолжать загрузку моделей
                if (_Status != Defines.Status.Success)
                {
                    StreamWriter.Close();
                    return;
                }
                    
                // Запускаем процесс RevitServerTool для загрузки модели
                ExportProcess = new Process{
                    StartInfo = new ProcessStartInfo{
                        FileName = _RevitServerToolPath + "\\RevitServerTool.exe",
                        Arguments = "L " + "\"" + _BufferForModelReferences[ThreadNumber, i, 0] + "\"" + " -s " + _IPAddressOfRevitServer + " -d " + "\"" + _AutomationToolsCachePath + "\\RVTs\\" + _BlockName + _BufferForModelReferences[ThreadNumber, i, 1] + "\"" + " -o",
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

                // Проверяем наличие файла 
                if (!File.Exists(_AutomationToolsCachePath + "\\RVTs\\" + _BlockName + "\\" + _BufferForModelReferences[ThreadNumber, i, 0]))
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
            // Инициализация...
            Console.WriteLine("[*] Загрузка моделей с Revit Server...");
                
            _BlockName = BlockName;
            _BufferForModelReferences = BufferForModelReferences;
            _IPAddressOfRevitServer = IPAddressOfRevitServer;
            _RevitServerToolPath = RevitServerToolPath;
            _AutomationToolsCachePath = AutomationToolsCachePath;
            _ModelIteratorForEachCPUCore = ModelIteratorForEachCPUCore;

            // Запуск потоков экспорта
            List<Thread> Threads = new List<Thread>();

            for (int i = 0; i < NumberOfWorkingCores; i++)
            {
                Threads.Add(new Thread(ExportModelsFromRevitServerWorker));
                Threads[i].Start(i);
                Console.WriteLine("[+] Поток {0} запущен!", i);
            }

            // Если нужное количество потоков не было запущено, то выходим
            if (Threads.Count() != NumberOfWorkingCores)
            {
                // Завершаем потоки, которые уже успели запуститься
                foreach (var Thread in Threads)
                {
                    Thread.Abort();
                }

                Console.WriteLine("[-] Возникла ошибка при запуске потоков, прерывание...");
                Console.WriteLine("[!] Вероятно, произошла внутренняя ошибка, обратитесь к разработчику.");
                return Defines.Status.Error;
            }
                
            // Ждём завершения загрузки моделей
            int ThreadIterator = 0;

            while (ThreadIterator < NumberOfWorkingCores)
            {
                if (Threads[ThreadIterator].IsAlive != false)
                    continue;

                Console.WriteLine("[+] Поток {0} завершён!", ThreadIterator);
                ThreadIterator++;
            }

            if (_Status != Defines.Status.Success)
            {
                Console.WriteLine("[-] Во время загрузки моделей с Revit Server возникла ошибка, прерывание...");
                Console.WriteLine("[!] Вы можете найти логи загрузки по пути ..\\Cache\\Logs\\<имя блока>\\.");
                return Defines.Status.Error;
            }

            Console.WriteLine("[+] Загрузка моделей с Revit Server завершена!");
            return Defines.Status.Success;
        }
    }
}
