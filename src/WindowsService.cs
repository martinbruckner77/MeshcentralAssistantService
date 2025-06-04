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
        private System.Threading.Thread pipeThread;
        private const string KILL_PIPE_NAME = "\\\\.\\pipe\\MeshAssistantKillPipe";
        private bool shouldRun = true;
        private const string LOG_DIR = "C:\\Program Files\\Mesh Agent";
        private const string LOG_PATH = "C:\\Program Files\\Mesh Agent\\service.log";
        private const string DEBUG_PATH = "C:\\Program Files\\Mesh Agent\\debug.log";
        
        public MeshService()
        {
            ServiceName = "MeshCentralAssistant";
            this.CanHandlePowerEvent = true;
            this.CanHandleSessionChangeEvent = true;
            this.CanPauseAndContinue = true;
            this.CanShutdown = true;
            this.CanStop = true;
            
            try
            {
                // Ensure we have write access to the log directory
                if (!Directory.Exists(LOG_DIR))
                {
                    Directory.CreateDirectory(LOG_DIR);
                }
                // Test write access
                File.AppendAllText(LOG_PATH, "Service initializing...\r\n");
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
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}\r\n";
                File.AppendAllText(LOG_PATH, logEntry);
                File.AppendAllText(DEBUG_PATH, logEntry); // Also write to debug log
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
                RequestAdditionalTime(30000); // Request 30 seconds for startup

                // Start in a separate thread to avoid blocking
                System.Threading.Thread startupThread = new System.Threading.Thread(() => {
                    try {
                        System.Windows.Forms.Application.EnableVisualStyles();
                        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                        
                        mainForm = new MainForm(args);
                        Log("MainForm created successfully");
                        
                        // Start message pump
                        System.Windows.Forms.Application.Run();
                    }
                    catch (Exception ex) {
                        Log($"Error in startup thread: {ex.Message}\r\nStack trace: {ex.StackTrace}");
                    }
                });
                startupThread.SetApartmentState(System.Threading.ApartmentState.STA);
                startupThread.IsBackground = true;
                startupThread.Start();
            
                // Start listening for kill commands
                pipeThread = new System.Threading.Thread(ListenForKillCommands);
                pipeThread.IsBackground = true;
                pipeThread.Start();
                
                Log("Service started successfully");
            }
            catch (Exception ex)
            {
                Log($"Failed to start service: {ex.Message}\r\nStack trace: {ex.StackTrace}");
                EventLog.WriteEntry("MeshCentralAssistant",
                    $"Failed to start service: {ex.Message}",
                    EventLogEntryType.Error);
                throw;
            }
        }

        protected override void OnStop()
        {
            shouldRun = false;
            Log("Service stopping...");
            
            try {
                if (pipeThread != null && pipeThread.IsAlive)
                {
                    pipeThread.Abort();
                    pipeThread = null;
                    Log("Pipe thread stopped");
                }
            }
            catch (Exception ex) {
                Log($"Error stopping pipe thread: {ex.Message}");
            }
            
            if (mainForm != null)
            {
                try
                {
                    System.Windows.Forms.Application.Exit();
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