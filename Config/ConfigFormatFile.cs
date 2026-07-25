using Newtonsoft.Json;
using System;
using System.IO;

namespace AutomationTools
{
    class ConfigFormatFile
    {
        static public Defines.Status ConfigFileLoadAndParse(string ConfigFilePath,
                                                            ref Config CurrentConfig) 
        {
            try
            {
                BinaryReader BinaryReader = new BinaryReader(File.Open(ConfigFilePath, FileMode.Open));
                BinaryReader.ReadBoolean();
                BinaryReader.ReadBoolean();
                BinaryReader.ReadString();
                CurrentConfig = JsonConvert.DeserializeObject<Config>(BinaryReader.ReadString());
                BinaryReader.Close();
            }
            catch(Exception e)
            {
                LoggingSystem.WriteLine(e.Message);
                return Defines.Status.Error;
            }

            return Defines.Status.Success;
        }
    }
}
