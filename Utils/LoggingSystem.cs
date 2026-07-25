using System;
using System.IO;
using System.Text;

namespace AutomationTools
{
    class LoggingSystem
    {
        private static FileStream   _LogFileStream;
        private static bool         _IsInit = false;

        public static void Init(string AutomationToolsCachePath)
        {
            _LogFileStream = File.Create(Path.Combine(AutomationToolsCachePath, "Logs", "Log.txt"));
            _IsInit = true;
        }

        public static void Deinit() 
        {
            if(_IsInit)
                _LogFileStream.Close();
        }

        public static void WriteLine(string Message)
        {
            if (_IsInit)
            {
                _LogFileStream.Write(Encoding.UTF8.GetBytes(Message + "\n"), 0, Encoding.UTF8.GetBytes(Message + "\n").Length);
                Console.WriteLine(Message);
            }
            
        }
    }
}
