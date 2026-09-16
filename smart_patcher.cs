using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;
using System.Net;
using System.Threading;

namespace EAAPatcher
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "EAA Training Manager - Smart Installer";
            Console.WriteLine("======================================================");
            Console.WriteLine("    EAA Training Manager - Smart Update (v2.2.7)      ");
            Console.WriteLine("======================================================");
            
            string targetDir = AppDomain.CurrentDomain.BaseDirectory;
            string exePath = Path.Combine(targetDir, "EAATrainingManager.exe");
            
            if (!File.Exists(exePath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[X] Error: Please place this patcher inside the EAA Training Manager folder.");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            // Safely close the app if it is running
            Process[] processes = Process.GetProcessesByName("EAATrainingManager");
            if (processes.Length > 0)
            {
                Console.WriteLine("\n[*] EAA Training Manager is currently running. Closing it safely...");
                foreach (var p in processes)
                {
                    try { p.Kill(); p.WaitForExit(); } catch { }
                }
                Thread.Sleep(1000);
            }

            // Check if it is the Standalone (90MB+) or Modular (< 1MB) version
            long fileSize = new FileInfo(exePath).Length;
            bool isStandalone = fileSize > 50000000; // > 50MB

            try 
            {
                if (isStandalone)
                {
                    Console.WriteLine("\n[*] Detected Standalone Version. Initializing smart background download...");
                    string downloadUrl = "https://github.com/michaelmagdy15/EAA-TrainingManager/releases/latest/download/EAATrainingManager.exe";
                    string tempFile = Path.Combine(targetDir, "EAATrainingManager_New.exe");

                    using (WebClient client = new WebClient())
                    {
                        client.DownloadProgressChanged += (s, e) =>
                        {
                            Console.Write("\r    -> Downloading Update: {0}% ({1} MB / {2} MB)", 
                                e.ProgressPercentage, 
                                e.BytesReceived / 1024 / 1024, 
                                e.TotalBytesToReceive / 1024 / 1024);
                        };
                        client.DownloadFileTaskAsync(new Uri(downloadUrl), tempFile).Wait();
                    }
                    
                    Console.WriteLine("\n\n[*] Download complete. Applying update...");
                    File.Delete(exePath);
                    File.Move(tempFile, exePath);
                }
                else
                {
                    Console.WriteLine("\n[*] Detected Modular Version. Applying lightweight delta patch...");
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Patch.zip"))
                    {
                        if (stream == null) 
                        {
                            Console.WriteLine("Error: Could not find embedded patch data.");
                            Console.ReadKey();
                            return;
                        }
                        
                        using (ZipArchive archive = new ZipArchive(stream))
                        {
                            foreach (ZipArchiveEntry entry in archive.Entries)
                            {
                                string destinationPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                                Console.WriteLine("    -> Updating " + entry.FullName);
                                entry.ExtractToFile(destinationPath, true);
                            }
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[SUCCESS] Update applied successfully!");
                Console.ResetColor();
                
                Console.WriteLine("\n[*] Starting EAA Training Manager...");
                Process.Start(exePath);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[X] An error occurred: " + ex.Message);
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }
            
            Thread.Sleep(2500);
        }
    }
}
