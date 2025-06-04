using System;
using System.ServiceProcess;
using System.Configuration.Install;
using System.ComponentModel;

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
        
        public MeshService()
        {
            ServiceName = "MeshCentralAssistant";
        }

        private void ListenForKillCommands()
        {
            while (shouldRun)
            {
                try
                {
                    using (var pipeServer = new NamedPipeServerStream(KILL_PIPE_NAME, PipeDirection.In))
                    {
                        pipeServer.WaitForConnection();
                        
                        var reader = new StreamReader(pipeServer);
                        string command = reader.ReadLine();
                        
                        if (command == "KILL")
                        {
                            var processes = System.Diagnostics.Process.GetProcessesByName("MeshAgent");
                            foreach (var proc in processes)
                            {
                                try { proc.Kill(); }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        protected override void OnStart(string[] args)
        {
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
                mainForm.Dispose();
                mainForm = null;
            }
        }
    }
}