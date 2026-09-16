using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;

namespace EAAPatcher
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "EAA Training Manager - Patch Installer";
            Console.WriteLine("EAA Training Manager - Patch Update to v2.2.7");
            Console.WriteLine("------------------------------------------------------");
            
            string targetDir = AppDomain.CurrentDomain.BaseDirectory;
            
            if (!File.Exists(Path.Combine(targetDir, "EAATrainingManager.exe")))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error: Please place this patcher inside the EAA Training Manager folder.");
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            Process[] processes = Process.GetProcessesByName("EAATrainingManager");
            if (processes.Length > 0)
            {
                Console.WriteLine("EAA Training Manager is running. Closing it safely...");
                foreach (var p in processes)
                {
                    try { p.Kill(); p.WaitForExit(); } catch { }
                }
            }

            Console.WriteLine("Extracting update files...");
            try 
            {
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
                            Console.WriteLine("  -> Replacing " + entry.FullName);
                            entry.ExtractToFile(destinationPath, true);
                        }
                    }
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine();
                Console.WriteLine("Update successful! The patch has been applied.");
                Console.ResetColor();
                
                Console.WriteLine("Starting EAA Training Manager...");
                Process.Start(Path.Combine(targetDir, "EAATrainingManager.exe"));
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("An error occurred: " + ex.Message);
                Console.ResetColor();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }
            
            System.Threading.Thread.Sleep(2000);
        }
    }
}
