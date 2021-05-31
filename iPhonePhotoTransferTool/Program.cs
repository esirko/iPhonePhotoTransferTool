using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaDevices;

namespace iPhonePhotoTransferTool
{
    class Program
    {
        enum Command
        {
            Null,
            List,
            Sync,
        }

        enum FileSyncStatus
        {
            Null,
            CopyBecauseDoesntExistYet,
            CopyBecauseFileSizesDiffer,
            DontCopyBecauseFileSizesAreSame,
        }

        static void Main(string[] mainArgs)
        {
            Console.Write("> ");
            string input = Console.ReadLine().ToLower().Trim();
            (string command, List<string> args, List<string> options) = ParseInput(input);

            while (command != "exit")
            {
                DateTime executionStartTime = DateTime.UtcNow;
                switch (command)
                {
                    case "ls":
                        if (args.Count == 1)
                        {
                            Iterate(Command.List, args, options);
                        }
                        else
                        {
                            Console.WriteLine("Expected 1 argument; try `help`");
                        }
                        break;

                    case "sync":
                        if (args.Count == 2)
                        {
                            Iterate(Command.Sync, args, options);
                        }
                        else
                        {
                            Console.WriteLine("Expected 2 arguments; try `help`");
                        }
                        break;

                    case "help":
                        PrintHelp();
                        break;

                    case "":
                        break;

                    default:
                        Console.WriteLine("Unrecognized command; try `help`");
                        break;
                }

                Console.Write($"[{(DateTime.UtcNow - executionStartTime).TotalSeconds.ToString("0.000")}] > ");
                input = Console.ReadLine();
                (command, args, options) = ParseInput(input);
            } 
        }

        static (string command, List<string> args, List<string> options) ParseInput(string input)
        {
            string command;
            List<string> args = new List<string>();
            List<string> options = new List<string>();

            int spacepos = input.IndexOf(' ');
            if (spacepos > 0)
            {
                command = input.Substring(0, spacepos);
                string restOfLine = input.Substring(spacepos + 1).Trim();
                while (!string.IsNullOrWhiteSpace(restOfLine))
                {
                    if (restOfLine.Substring(0, 1) == "\"")
                    {
                        int endQuote = restOfLine.IndexOf("\"");
                        if (endQuote < 0)
                        {
                            args.Add(restOfLine); // User forgot to terminate the quote, so just take it to the end of the line as a whole argument
                            restOfLine = "";
                        }
                        else
                        {
                            args.Add(restOfLine.Substring(0, endQuote));
                            restOfLine = restOfLine.Substring(endQuote + 1).Trim();
                        }
                    }
                    else
                    {
                        spacepos = restOfLine.IndexOf(" ");
                        if (spacepos < 0)
                        {
                            args.Add(restOfLine);
                            restOfLine = "";
                        }
                        else
                        {
                            args.Add(restOfLine.Substring(0, spacepos));
                            restOfLine = restOfLine.Substring(spacepos + 1).Trim();
                        }
                    }
                }
            }
            else
            {
                command = input;
            }

            for (int i = args.Count - 1; i >= 0; i--)
            {
                if (args[i].StartsWith("--"))
                {
                    options.Add(args[i].ToLower());
                    args.RemoveAt(i);
                }
            }

            return (command, args, options);
        }

        static void Iterate(Command command, List<string> args, List<string> options)
        {
            var devices = MediaDevice.GetDevices();
            using (var device = devices.First(d => d.FriendlyName == "Apple iPhone"))
            {
                device.Connect();
                List<MediaDirectoryInfo> dirs = device.GetDirectoryInfo("Internal Storage/DCIM").EnumerateDirectories().OrderBy(d => d.Name).ToList();
                for (int i = 0; i < dirs.Count; i++)
                {
                    if (args[0] == "*" || args[0] == dirs[i].Name)
                    {
                        Console.WriteLine($"[{i}/{dirs.Count}] {dirs[i].Name}");

                        if (command == Command.Sync || (command == Command.List && (options.Contains("--detail") || options.Contains("--summary"))))
                        {
                            List<MediaFileInfo> files = dirs[i].EnumerateFiles().OrderBy(f => f.Name).ToList();
                            if (command == Command.List)
                            {
                                if (options.Contains("--detail"))
                                {
                                    for (int j = 0; j < files.Count; j++)
                                    {
                                        Console.WriteLine($"  [{i}/{dirs.Count}] [{j}/{files.Count}] {files[j].Name} ({files[j].Length})");
                                    }
                                }
                                else if (options.Contains("--summary"))
                                {
                                    foreach (var group in files.GroupBy(f => Path.GetExtension(f.FullName)))
                                    {
                                        Console.WriteLine($"  {group.Key} {group.Count()} ({group.Sum(f => (decimal)f.Length)})");
                                    }
                                    Console.WriteLine($"  (Total) {files.Count} ({files.Sum(f => (decimal)f.Length)})");
                                }
                            }
                            else if (command == Command.Sync)
                            {
                                //string sourceDir = $"Internal Storage/DCIM/{dirs[i].Name}";
                                bool dryrun = options.Contains("--dryrun") || options.Contains("--dry-run");
                                string dryrunPrefix = dryrun ? "[--dryrun] " : "";
                                string destinationDir = Path.Combine(args[1], dirs[i].Name);

                                if (!Directory.Exists(destinationDir))
                                {
                                    Console.WriteLine($"{dryrunPrefix}Creating directory {destinationDir}");
                                    if (!dryrun)
                                    {
                                        Directory.CreateDirectory(destinationDir);
                                    }
                                }

                                for (int j = 0; j < files.Count; j++)
                                {
                                    string destinationFile = Path.Combine(destinationDir, files[j].Name);
                                    long preExistingFileSize = 0;
                                    FileSyncStatus status = FileSyncStatus.CopyBecauseDoesntExistYet;

                                    if (File.Exists(destinationFile))
                                    {
                                        preExistingFileSize = (new FileInfo(destinationFile)).Length;
                                        status = files[j].Length == (ulong)preExistingFileSize ? FileSyncStatus.DontCopyBecauseFileSizesAreSame : FileSyncStatus.CopyBecauseFileSizesDiffer;
                                    }
                                    Console.WriteLine($"  [{i}/{dirs.Count}] [{j}/{files.Count}] {status} {files[j].Name} ({files[j].Length}) ({preExistingFileSize})");

                                    if (!dryrun && (status == FileSyncStatus.CopyBecauseDoesntExistYet || status == FileSyncStatus.CopyBecauseFileSizesDiffer))
                                    {
                                        using (FileStream stream = File.OpenWrite(destinationFile))
                                        {
                                            device.DownloadFile(files[j].FullName, stream);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                //                device.CreateDirectory(@"\Phone\Documents\Temp");
                //                using (FileStream stream = File.OpenRead(@"C:/Temp/Test.txt"))
                //                {
                //                    device.UploadFile(stream, @"\Phone\Documents\Temp\Test.txt");
                //                }
                device.Disconnect();
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("Commands:");
            Console.WriteLine("  ls * [--summary] [--detail]");
            Console.WriteLine("  sync * path/to/destination [--dry-run]");
            Console.WriteLine("  [In the above, * can be a literal star or the name of a single directory, e.g., 112APPLE");
            Console.WriteLine("  help");
            Console.WriteLine("  exit");
        }
    }
}
