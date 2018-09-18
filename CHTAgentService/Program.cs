using CloudHealth;

namespace CHTAgentService
{
    class Program
    {
        static int Main(string[] args)
        {
          try {
            var apiKey = AgentConfig.GetAPIKey();
            if (apiKey == null) {
                return Constants.MISCONFIGURED_EXIT_CODE;
            }
            return new Agent(apiKey).Run();
          } catch (System.Exception ex) {
                string dirLogPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
                string fileLogPath = dirLogPath + "/install_err.log";
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(fileLogPath, true)) {
                    writer.WriteLine("Message :" + ex.Message + System.Environment.NewLine + "StackTrace :" +
                        ex.StackTrace + "" + System.Environment.NewLine + "Date :" + System.DateTime.Now.ToString());
                    writer.Close();
                }
                return 1;
            }
        }
    }
}
