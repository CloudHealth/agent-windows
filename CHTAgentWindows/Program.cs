using System;
using System.Configuration.Install;
using System.Reflection;
using System.ServiceProcess;
using System.Threading;

namespace CHTAgentWindows
{
    static class Program
    {
        static void Main(string[] args)
        {
            var servicesToRun = new ServiceBase[] 
            {
                new CHTAgentStub()
            };

            if (Environment.UserInteractive)
            {
                RunInteractive(servicesToRun, args);
            }
            else
            {
                ServiceBase.Run(servicesToRun);
            }
        }

        static void RunInteractive(ServiceBase[] servicesToRun, string[] args)
        {
            var parameter = string.Concat(args);
            switch (parameter)
            {
                case "--install":
                    ManagedInstallerClass.InstallHelper(new[] { Assembly.GetExecutingAssembly().Location });
                    break;
                case "--uninstall":
                    ManagedInstallerClass.InstallHelper(new[] { "/u", Assembly.GetExecutingAssembly().Location });
                    break;
                default:
                    Console.WriteLine("Services running in interactive mode.");
                    Console.WriteLine();

                    var onStartMethod = typeof(ServiceBase).GetMethod("OnStart",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (var service in servicesToRun)
                    {
                        Console.Write("Starting {0}...", service.ServiceName);
                        onStartMethod.Invoke(service, new object[] { new string[] { } });
                        Console.Write("Started");
                    }

                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine("Press any key to stop the services and end the process...");
                    Console.ReadKey();
                    Console.WriteLine();

                    var onStopMethod = typeof(ServiceBase).GetMethod("OnStop",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    foreach (var service in servicesToRun)
                    {
                        Console.Write("Stopping {0}...", service.ServiceName);
                        onStopMethod.Invoke(service, null);
                        Console.WriteLine("Stopped");
                    }

                    Console.WriteLine("All services stopped.");
                    // Keep the console alive for a second to allow the user to see the message.
                    Thread.Sleep(1000);
                    break;
            }
        }
    }
}
