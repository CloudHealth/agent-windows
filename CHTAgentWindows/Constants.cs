using System.IO;
using System.Reflection;

namespace CloudHealth
{
    public class Constants
    {
        public const int UPDATE_EXIT_CODE = 0x000f1717;
        public const int MISCONFIGURED_EXIT_CODE = 0x000f1718;

        public static string UpdateFilePath()
        {
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(exeDir, "Update.zip");

        }
    }
}
