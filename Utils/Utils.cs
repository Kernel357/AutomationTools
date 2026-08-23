using System.IO;

namespace AutomationTools
{
    class Utils
    {
        public static void RecursiveDelete(DirectoryInfo BaseDirectory)
        {
            if (!BaseDirectory.Exists)
                return;

            foreach (var Directory in BaseDirectory.EnumerateDirectories())
                RecursiveDelete(Directory);

            var Files = BaseDirectory.GetFiles();

            try
            {
                foreach (var File in Files)
                {
                    File.IsReadOnly = false;
                    File.Delete();
                }

                BaseDirectory.Delete();
            }
            catch { }
            
        }
    }
}
