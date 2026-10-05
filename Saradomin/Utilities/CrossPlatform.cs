using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Mono.Unix;

namespace Saradomin.Utilities
{
    public static class CrossPlatform
    {
        public static void LaunchURL(string url)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }

        public static void OpenFolder(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
        }

        public static bool IsJavaExecutableValid(string location)
        {
            try
            {
                if (!File.Exists(location))
                {
                    return false;
                }

                using (var fileStream = File.OpenRead(location))
                {
                    var bytes = new byte[4];
                    fileStream.Read(bytes, 0, 4);

                    if (bytes[0] == 0x7F
                        && bytes[1] == 0x45
                        && bytes[2] == 0x4C
                        && bytes[3] == 0x46)
                    {
                        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
                    }

                    if (bytes[0] == 'M'
                        && bytes[1] == 'Z')
                    {
                        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
                    }

                    if ((bytes[0] == 0xCF
                        && bytes[1] == 0xFA)
                        || (bytes[0] == 0xCA
                        && bytes[1] == 0xFE))
                    {
                        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
                    }

                }
            }
            catch
            {
                // Ignore
            }
            
            return false;
        }

        public static string LocateJavaExecutable()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var envPath = Environment.GetEnvironmentVariable("JAVA_HOME");

                if (!string.IsNullOrEmpty(envPath))
                    return Path.Combine(envPath, "bin/java.exe");
                
                using (var rk = Registry.LocalMachine.OpenSubKey("SOFTWARE\\JavaSoft\\Java Runtime Environment\\"))
                {
                    if (rk == null)
                        return null;

                    var currentVersion = rk.GetValue("CurrentVersion")?.ToString();

                    if (currentVersion == null)
                        return null;
                    
                    using (var key = rk.OpenSubKey(currentVersion))
                    {
                        if (key == null)
                            return null;
                        
                        envPath = key.GetValue("JavaHome")?.ToString();
                    }
                }

                if (!string.IsNullOrEmpty(envPath))
                    return Path.Combine(envPath, "bin/java.exe");

                throw new FileNotFoundException("Failed to find Java. Make sure it's installed!");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                     || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            {
                var proc = new Process
                {
                    StartInfo = new("/bin/which")
                    {
                        Arguments = "java",
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    }
                };

                proc.Start();
                proc.WaitForExit();
                var data = proc.StandardOutput.ReadToEnd();

                if (!string.IsNullOrEmpty(data))
                    return UnixPath.GetCompleteRealPath(data.Trim());

                throw new FileNotFoundException("Failed to find Java. Make sure it's installed!");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var proc = new Process
                {
                    StartInfo = new("/usr/bin/which")
                    {
                        Arguments = "java",
                        RedirectStandardOutput = true,
                        UseShellExecute = false
                    }
                };

                proc.Start();
                proc.WaitForExit();
                var data = proc.StandardOutput.ReadToEnd();

                if (!string.IsNullOrEmpty(data))
                    return Path.Combine(UnixPath.GetCompleteRealPath(data.Trim()));

                throw new FileNotFoundException("Failed to find Java. Make sure it's installed!");
            }
            else
            {
                throw new NotSupportedException("Your platform is not supported.");
            }
        }

        public static string LocateUnixUserHome()
        {
            return Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share"
                );
        }

        private const string GameLocationFileName = "game_location.txt";

        public static string GetLauncherDirectory()
        {
            return AppContext.BaseDirectory;
        }

        public static string GetPortable2009scapeHome()
        {
            return Path.Combine(GetLauncherDirectory(), "2009scape");
        }

        private static string GetGameLocationFilePath()
        {
            return Path.Combine(GetLauncherDirectory(), GameLocationFileName);
        }

        private static string GetLegacy2009scapeHome()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
            {
                return Path.Combine(LocateUnixUserHome(), "2009scape");
            }

            var userProfile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "2009scape"
            );
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "2009scape"
            );

            return Directory.Exists(userProfile) ? userProfile : appData;
        }

        public static string Get2009scapeHome()
        {
            var locationFile = GetGameLocationFilePath();
            if (File.Exists(locationFile))
            {
                var configured = File.ReadAllText(locationFile).Trim();
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return Path.IsPathRooted(configured)
                        ? Path.GetFullPath(configured)
                        : Path.GetFullPath(Path.Combine(GetLauncherDirectory(), configured));
                }
            }

            var portable = GetPortable2009scapeHome();
            if (Directory.Exists(portable))
                return portable;

            // Keep existing installs working until the user explicitly moves them.
            var legacy = GetLegacy2009scapeHome();
            if (Directory.Exists(legacy))
                return legacy;

            // Fresh Killer Edition installs are portable by default.
            return portable;
        }

        public static string GetSaradominHome()
        {
            return Path.Combine(Get2009scapeHome(), "saradomin");
        }

        public static void Set2009scapeHome(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var launcherDirectory = Path.GetFullPath(GetLauncherDirectory())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var launcherPrefix = launcherDirectory + Path.DirectorySeparatorChar;

            var storedPath = fullPath.StartsWith(launcherPrefix, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(launcherDirectory, fullPath)
                : fullPath;

            File.WriteAllText(GetGameLocationFilePath(), storedPath);
        }

        public static string GetBundledJavaExecutable(string gameHome = null)
        {
            gameHome ??= Get2009scapeHome();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(gameHome, "jre25", "Contents", "Home", "bin", "java");
            }

            return Path.Combine(
                gameHome,
                "jre11",
                "bin",
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "java.exe" : "java"
            );
        }

        public static string Move2009scapeHome(string destinationParent)
        {
            var source = Path.GetFullPath(Get2009scapeHome());
            var parent = Path.GetFullPath(destinationParent);
            var destination = string.Equals(
                Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                "2009scape",
                StringComparison.OrdinalIgnoreCase
            )
                ? parent
                : Path.Combine(parent, "2009scape");

            destination = Path.GetFullPath(destination);

            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                return destination;

            if (destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The new game location cannot be inside the current game folder.");

            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new IOException("The destination 2009scape folder is not empty.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                Directory.Move(source, destination);
            }
            catch (IOException)
            {
                CopyDirectory(source, destination);
                Directory.Delete(source, true);
            }

            Set2009scapeHome(destination);
            return destination;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            }

            foreach (var directory in Directory.GetDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }

        public static string GetSingleplayerBackupsHome()
        {
            return Path.Combine(Get2009scapeHome(), "singleplayer_backups");
        }

        public static string GetSingleplayerHome()
        {
            return Path.Combine(Get2009scapeHome(), "singleplayer");
        }

        public static string LocateSingleplayerExecutable()
        {
            return Path.Combine(GetSingleplayerHome(), RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "launch.bat" : "launch.sh");
        }
        
        public static string Get2009scapeExecutable()
        {
            return Path.Combine(Get2009scapeHome(), "osrs-client-killer.jar");
        }

        public static string GetServerProfilePath(string baseDirectory)
        {
            baseDirectory ??= Get2009scapeHome();
            return Path.Combine(baseDirectory, "server_profiles.json");
        }

        public static string RunCommandAndGetOutput(string command, Action<string> onOutputReceived = null, Action<string> onErrorReceived = null)
        {
            Process process = new Process();
            StringBuilder output = new StringBuilder();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                process.StartInfo = new ProcessStartInfo("cmd.exe", "/c " + command)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }
            else if (
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            )
            {
                process.StartInfo = new ProcessStartInfo("bash", "-c \"" + command + "\"")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                output.AppendLine(e.Data);
                onOutputReceived?.Invoke(e.Data);
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return; 
                output.AppendLine(e.Data);
                onErrorReceived?.Invoke(e.Data);
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            process.WaitForExit();

            return output.ToString();
        }

        public static string GetJava25DownloadUrl()
        {
            string architecture = GetSystemArchitecture();
            string os;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                os = "windows";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                os = "linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                os = "mac";
            else
                throw new NotSupportedException("Your platform is not supported.");

            return $"https://api.adoptium.net/v3/binary/latest/25/ga/{os}/{architecture}/jre/hotspot/normal/eclipse?project=jdk";
        }

        public static string GetJavaRuntimeArchiveExtension()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".zip" : ".tar.gz";
        }

        private static string GetSystemArchitecture()
        {
            if (RuntimeInformation.OSArchitecture == Architecture.X64)
            {
                return "x64";
            }
            else if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                return "aarch64";
            }
            else
            {
                throw new NotSupportedException("Your architecture is not supported.");
            }
        }
        public static bool IsDirectoryWritable(string directoryPath)
        {
            var testFilePath = Path.Combine(directoryPath, "test");

            try
            {
                File.Create(testFilePath).Dispose();
                File.Delete(testFilePath);

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
        
        public static Process StartJavaProcess(string javaExecutable, string jarPath, string memoryAllocation, Action<string> outputHandler, Action onExit)
        {
            Process process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = javaExecutable,
                Arguments = $"-Xmx{memoryAllocation} -Xms{memoryAllocation} -jar \"{jarPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.Combine(CrossPlatform.GetSingleplayerHome(), "game")
            };

            process.OutputDataReceived += (_, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;
                outputHandler?.Invoke(args.Data);
            };

            process.ErrorDataReceived += (_, args) =>
            {
                if (string.IsNullOrEmpty(args.Data)) return;
                outputHandler?.Invoke(args.Data);
            };

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => onExit?.Invoke();

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }
    }
}