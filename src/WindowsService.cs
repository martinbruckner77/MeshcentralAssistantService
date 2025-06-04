using System;
using System.ServiceProcess;
using System.Configuration.Install;
using System.ComponentModel;
using System.IO;
using System.Diagnostics;

namespace MeshAssistant
{
    [RunInstaller(true)]
    public class MeshServiceInstaller : Installer
    {
        private ServiceInstaller serviceInstaller;
        private ServiceProcessInstaller processInstaller;

        public MeshServiceInstaller()
        {
            processInstaller = new ServiceProcessInstaller();
            serviceInstaller = new ServiceInstaller();

            processInstaller.Account = ServiceAccount.LocalSystem;
            serviceInstaller.StartType = ServiceStartMode.Automatic;
            serviceInstaller.ServiceName = "MeshCentralAssistant";
            serviceInstaller.Description = "MeshCentral Assistant Service for remote management and support";
            
            Installers.Add(serviceInstaller);
            Installers.Add(processInstaller);
        }
    }

    public class MeshService : ServiceBase
    {
        private MainForm mainForm;
        private const string KILL_PIPE_NAME = "\\\\.\\pipe\\MeshAssistantKillPipe";
        private bool shouldRun = true;
        private const string LOG_PATH = "C:\\ProgramData\\MeshCentralAssistant\\service.log";
        
        public MeshService()
        {
            ServiceName = "MeshCentralAssistant";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LOG_PATH));
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("MeshCentralAssistant", 
                    "Failed to create log directory: " + ex.Message,
                    EventLogEntryType.Error);
            }
        }

        private void Log(string message)
        {
            try
            {
                File.AppendAllText(LOG_PATH, 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}\r\n");
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("MeshCentralAssistant",
                    "Failed to write to log: " + ex.Message,
                    EventLogEntryType.Error); 
            }
        }

        private void ListenForKillCommands()
        {
            while (shouldRun)
            {
                try
                {
                    Log("Starting named pipe server...");
                    using (var pipeServer = new NamedPipeServerStream(KILL_PIPE_NAME, PipeDirection.In))
                    {
                        pipeServer.WaitForConnection();
                        Log("Client connected to named pipe");
                        
                        var reader = new StreamReader(pipeServer);
                        string command = reader.ReadLine();
                        Log($"Received command: {command}");
                        
                        if (command == "KILL")
                        {
                            var processes = System.Diagnostics.Process.GetProcessesByName("MeshAgent");
                            Log($"Found {processes.Length} MeshAgent processes to terminate");
                            foreach (var proc in processes)
                            {
                                try 
                                { 
                                    proc.Kill();
                                    Log($"Successfully terminated process {proc.Id}");
                                }
                                catch (Exception ex)
                                {
                                    Log($"Failed to kill process {proc.Id}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"Error in pipe server: {ex.Message}");
                    System.Threading.Thread.Sleep(1000); // Prevent tight loop on error
                }
            }
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                Log("Service starting...");

            // Create main form in service context
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            mainForm = new MainForm(args);
            
            // Start listening for kill commands
            System.Threading.Thread pipeThread = new System.Threading.Thread(ListenForKillCommands);
            pipeThread.IsBackground = true;
            pipeThread.Start();
        }

        protected override void OnStop()
        {
            shouldRun = false;
            if (mainForm != null)
            {
                try
                {
                    mainForm.Dispose();
                    mainForm = null;
                    Log("MainForm disposed successfully");
                }
                catch (Exception ex)
                {
                    Log($"Error disposing MainForm: {ex.Message}");
                }
            }
            Log("Service stopped");
        }
    }
}