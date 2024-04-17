using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Query;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace WindowsGSM.Plugins
{
    public class Imposter
    {
        // - Plugin Details
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.Imposter", // WindowsGSM.XXXX
            author = "Raziel7893",
            description = "WindowsGSM plugin for supporting AmongUS Imposter Server",
            version = "1.0",
            url = "https://github.com/Raziel7893/WindowsGSM.Imposter", // Github repository link (Best practice)
            color = "#ffffff" // Color Hex
        };


        // - Standard Constructor and properties
        public Imposter(ServerConfig serverData) => _serverData = serverData;
        private readonly ServerConfig _serverData;
        public string Error, Notice;


        // - Game server Fixed variables
        public string StartPath => "Impostor.Server.exe"; // Game server start path
        public string ConfigPath => "config.json";
        public string FullName = "Impostor: AmongUS Server"; // Game server FullName
        public bool AllowsEmbedConsole = true;  // Does this server support output redirect?
        public int PortIncrements = 1; // This tells WindowsGSM how many ports should skip after installation
        public object QueryMethod = new UT3(); // Query method should be use on current server type. Accepted value: null or new A2S() or new FIVEM() or new UT3()
        public string VersionApi = "https://api.github.com/repos/Impostor/Impostor/releases/latest";
        public string VersionKey = "InstalledVersion";


        // - Game server default values
        public string Port = "22023"; // Default port
        public string QueryPort = "22023"; // Default query port
        public string Defaultmap = "world"; // Default map name
        public string Maxplayers = "99"; // Default maxplayers
        public string Additional = ""; // Additional server start parameter


        // - Create a default cfg for the game server after installation
        public async void CreateServerCFG()
        {
        }

        public void UpdateConfig()
        {
            string configFile = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, ConfigPath);

            var config = JsonConvert.DeserializeObject<ImposterConfig>(File.ReadAllText(configFile));

            config.Server.PublicIp = GetPublicIP();
            config.Server.PublicPort = _serverData.ServerPort;
            config.Server.ListenIp = _serverData.ServerIP;
            config.Server.ListenPort = _serverData.ServerPort;

            config.HttpServer.ListenPort = _serverData.ServerQueryPort;
            config.HttpServer.ListenIp = _serverData.ServerIP;

            File.WriteAllText(configFile, JsonConvert.SerializeObject(config, Formatting.Indented));
        }


        // - Start server function, return its Process to WindowsGSM
        public async Task<Process> Start()
        {
            UpdateConfig();
            // Prepare Process
            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID),
                    FileName = ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath),
                    Arguments = "",
                    WindowStyle = ProcessWindowStyle.Minimized,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            // Set up Redirect Input and Output to WindowsGSM Console if EmbedConsole is on
            if (_serverData.EmbedConsole)
            {
                p.StartInfo.CreateNoWindow = false;
                p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
            }
            // Start Process
            try
            {
                p.Start();
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null; // return null if fail to start
            }
            if (_serverData.EmbedConsole)
            {
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            return p;
        }


        // - Stop server function
        public async Task Stop(Process p)
        {
            Functions.ServerConsole.SetMainWindow(p.MainWindowHandle);
            Functions.ServerConsole.SendWaitToMainWindow("^c");
            p.WaitForExit(10000);
            if (!p.HasExited)
                p.Kill();
        }


        // - Install server function
        public async Task<Process> Install()
        {
            var currentVersion = "";
            try
            {
                using (var webClient = new WebClient())
                {
                    webClient.Headers.Add("User-Agent: Other");
                    var downloadUrl = "";

                    var downloadString = await webClient.DownloadStringTaskAsync(VersionApi);

                    var versionInfo = JObject.Parse(downloadString);
                    currentVersion = versionInfo["tag_name"].ToString();

                    foreach (var asset in versionInfo["assets"])
                    {
                        var currentUrl = asset["browser_download_url"].ToString();
                        if (!string.IsNullOrWhiteSpace(currentUrl))
                        {
                            if (currentUrl.Contains("_win-x64.zip"))
                            {
                                downloadUrl = currentUrl;
                                break;
                            }
                        }
                    };
                    if (string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        await UI.CreateYesNoPromptV1("Error", $"No Windows Executable found in version{currentVersion}", "OK", "OK");
                        Error = $"No Windows Executable found in version{currentVersion}";
                        return null;
                    }

                    var installerFileName = ServerPath.GetServersServerFiles(_serverData.ServerID, "install.zip");
                    await webClient.DownloadFileTaskAsync(downloadUrl, installerFileName);
                    await FileManagement.ExtractZip(installerFileName, ServerPath.GetServersServerFiles(_serverData.ServerID));
                    File.Delete(installerFileName);
                }
            }
            catch (Exception e)
            {
                await UI.CreateYesNoPromptV1("Error", e.Message, "OK", "OK");
                Error = e.Message;
                return null;
            }

            ServerConfig.SetSetting(_serverData.ServerID, VersionKey, currentVersion.Replace("v", ""));
            return null;
        }


        // - Update server function
        public async Task<Process> Update()
        {
            // Delete the old paper.jar
            var configFile = ServerPath.GetServersServerFiles(_serverData.ServerID, ConfigPath);
            ImposterConfig config = null;
            if (File.Exists(configFile))
                config = JsonConvert.DeserializeObject<ImposterConfig>(File.ReadAllText(configFile));

            //just do the normal install process
            await Install();

            //rewrite the config
            if (config != null)
                File.WriteAllText(configFile, JsonConvert.SerializeObject(config, Formatting.Indented));
            return null;
        }


        // - Check if the installation is successful
        public bool IsInstallValid()
        {
            return File.Exists(ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }


        // - Check if the directory contains paper.jar for import
        public bool IsImportValid(string path)
        {
            var exePath = Path.Combine(path, StartPath);
            Error = $"Invalid Path! Fail to find {StartPath}";
            return File.Exists(exePath);
        }


        // - Get Local server version
        public string GetLocalBuild()
        {
            return ServerConfig.GetSetting(_serverData.ServerID, VersionKey);
        }


        // - Get Latest server version
        public async Task<string> GetRemoteBuild()
        {
            try
            {
                using (var webClient = new WebClient())
                {
                    webClient.Headers.Add("User-Agent: Other");
                    var versionInfo = JObject.Parse(await webClient.DownloadStringTaskAsync(VersionApi));
                    return versionInfo["tagName"].ToString().Replace("v", "");
                }
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }
        }

        private string GetPublicIP()
        {
            try
            {
                using (var webClient = new WebClient())
                {
                    return webClient.DownloadString("https://ipinfo.io/ip").Replace("\n", string.Empty);
                }
            }
            catch
            {
                return null;
            }
        }


        public class ImposterServer
        {
            public string PublicIp = "127.0.0.1";
            public string PublicPort = "22023";
            public string ListenIp = "0.0.0.0";
            public string ListenPort = "22023";
        }
        public class HttpServer
        {
            public bool Enabled = true;
            public string ListenIp = "0.0.0.0";
            public string ListenPort = "22023";
        }
        public class AntiCheat
        {
            public bool Enabled = true;
            public bool BanIpFromGame = true;
        }
        public class Timeout
        {
            public int SpawnTimeout = 2500;
            public int ConnectionTimeout = 2500;
        }
        public class Compatibility
        {
            public bool AllowFutureGameVersions = false;
            public bool AllowVersionMixing = false;
        }
        public class Debug
        {
            public bool GameRecorderEnabled = false;
            public string GameRecorderPath = "";
        }
        public class ImposterConfig
        {
            public ImposterServer Server;
            public HttpServer HttpServer;
            public AntiCheat AntiCheat;
            public Timeout Timeout;
            public Compatibility Compatibility;
            public Debug Debug;
        }
    }
}
