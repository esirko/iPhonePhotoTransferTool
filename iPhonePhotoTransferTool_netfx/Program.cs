using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
            IgnoreBecauseUserSpecified,
        }

        static void Main(string[] mainArgs)
        {
            Console.Write("> ");
            string input = Console.ReadLine().ToLower().Trim();
            (string command, List<string> args, Dictionary<string, string> options) = ParseInput(input);

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

        static (string command, List<string> args, Dictionary<string, string> options) ParseInput(string input)
        {
            string command;
            List<string> args = new List<string>();
            Dictionary<string, string> options = new Dictionary<string, string>();

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

            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].StartsWith("\"") && args[i].EndsWith("\""))
                {
                    args[i] = args[i].Substring(1, args[i].Length - 2);
                }
            }

            for (int i = args.Count - 1; i >= 0; i--)
            {
                if (args[i].StartsWith("--"))
                {
                    if (args[i].ToLower() == "--ignore")
                    {
                        if (i < args.Count - 1)
                        {
                            options.Add(args[i].ToLower(), args[i + 1]);
                            args.RemoveAt(i + 1);
                            args.RemoveAt(i);
                        }
                    }
                    else
                    {
                        options.Add(args[i].ToLower(), "true");
                        args.RemoveAt(i);
                    }
                }
            }

            return (command, args, options);
        }

        static void Iterate(Command command, List<string> args, Dictionary<string, string> options)
        {
            try
            {
                var devices = MediaDevice.GetDevices();
                using (var device = devices.First(d => d.FriendlyName == "Apple iPhone"))
                {
                    device.Connect();
                    List<MediaDirectoryInfo> dirs = device.GetDirectoryInfo("Internal Storage/DCIM").EnumerateDirectories().OrderBy(d => d.Name).ToList();
                    for (int i = 0; i < dirs.Count; i++)
                    {
                        if (Regex.IsMatch(dirs[i].Name, "^" + Regex.Escape(args[0]).Replace("\\*", ".*") + "$"))
                        //if (args[0] == "*" || args[0] == dirs[i].Name)
                        {
                            Console.WriteLine($"[{i}/{dirs.Count}] {dirs[i].Name}");

                            if (command == Command.Sync || (command == Command.List && (options.ContainsKey("--detail") || options.ContainsKey("--summary"))))
                            {
                                List<MediaFileInfo> files = dirs[i].EnumerateFiles().OrderBy(f => f.Name).ToList();
                                if (command == Command.List)
                                {
                                    if (options.ContainsKey("--detail"))
                                    {
                                        for (int j = 0; j < files.Count; j++)
                                        {
                                            Console.WriteLine($"  [{i}/{dirs.Count}] [{j}/{files.Count}] {files[j].Name} ({files[j].Length})");
                                        }
                                    }
                                    else if (options.ContainsKey("--summary"))
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
                                    bool quiet = options.ContainsKey("--quiet");
                                    bool dryrun = options.ContainsKey("--dryrun") || options.ContainsKey("--dry-run");
                                    bool consolidate = options.ContainsKey("--consolidate");
                                    string dryrunPrefix = dryrun ? "[--dry-run] " : "";
                                    string destinationDir = Path.Combine(args[1], dirs[i].Name);

                                    if (consolidate)
                                    {
                                        if (Regex.IsMatch(dirs[i].Name, @"^\d\d\d\d\d\d_[a-z]$"))
                                        {
                                            destinationDir = Path.Combine(args[1], dirs[i].Name.Substring(0,7) + "_");
                                        }
                                    }

                                    Regex regexToIgnore = null;
                                    if (options.TryGetValue("--ignore", out string ignoreOption))
                                    {
                                        regexToIgnore = new Regex(ignoreOption, RegexOptions.IgnoreCase);
                                    }

                                    if (!Directory.Exists(destinationDir))
                                    {
                                        Console.WriteLine($"{dryrunPrefix}Creating directory {destinationDir}");
                                        if (!dryrun)
                                        {
                                            Directory.CreateDirectory(destinationDir);
                                        }
                                    }

                                    string lastFileName = ""; // to look for dupes
                                    int dupeCounter = 0;
                                    for (int j = 0; j < files.Count; j++)
                                    {
                                        string fileName = files[j].Name;
                                        if (fileName == lastFileName)
                                        {
                                            dupeCounter++;
                                            fileName = Path.GetFileNameWithoutExtension(fileName) + "-" + dupeCounter + Path.GetExtension(fileName);
                                        }
                                        else
                                        {
                                            dupeCounter = 0;
                                        }

                                        string destinationFile = Path.Combine(destinationDir, fileName);
                                        long preExistingFileSize = 0;
                                        FileSyncStatus status = FileSyncStatus.CopyBecauseDoesntExistYet;

                                        if (File.Exists(destinationFile))
                                        {
                                            preExistingFileSize = (new FileInfo(destinationFile)).Length;
                                            status = files[j].Length == (ulong)preExistingFileSize ? FileSyncStatus.DontCopyBecauseFileSizesAreSame : FileSyncStatus.CopyBecauseFileSizesDiffer;
                                        }

                                        if (regexToIgnore != null && regexToIgnore.IsMatch(fileName))
                                        {
                                            status = FileSyncStatus.IgnoreBecauseUserSpecified;
                                        }

                                        if (status != FileSyncStatus.DontCopyBecauseFileSizesAreSame || !quiet)
                                        {
                                            Console.WriteLine($"  [{i}/{dirs.Count}] [{j}/{files.Count}] {status} {fileName} ({files[j].Length}) ({preExistingFileSize})");
                                        }

                                        if (!dryrun && (status == FileSyncStatus.CopyBecauseDoesntExistYet || status == FileSyncStatus.CopyBecauseFileSizesDiffer))
                                        {
                                            try
                                            {
                                                using (FileStream stream = File.OpenWrite(destinationFile))
                                                {
                                                    device.DownloadFileFromPersistentUniqueId(files[j].PersistentUniqueId, stream); // important to use PersistentUniqueId instead of FullName because of dupes
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Console.WriteLine($"EXCEPTION: {ex.Message}");
                                                if (ex.Message.StartsWith("The device is unreachable"))
                                                {
                                                    Console.WriteLine("Breaking - you'll have to retry");
                                                    break;
                                                }
                                            }
                                        }
                                        lastFileName = files[j].Name;
                                    }
                                }
                            }
                        }
                    }

                    device.Disconnect();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"EXCEPTION: {ex.Message}");
            }
        }

        static void PrintHelp()
        {
            Console.WriteLine("Commands:");
            Console.WriteLine("  ls * [--summary] [--detail]");
            Console.WriteLine("  sync * path/to/destination [--ignore regexAgainstFileName] [--consolidate] [--dry-run] [--quiet]");
            Console.WriteLine("  You probably want to use the --consolidate option, which consolidates 202110_a into 202110__");
            Console.WriteLine("  [In the above, * can be a simple wildcard expression, e.g., 2021*__ matches 202101__ and 202102__, etc.]");
            Console.WriteLine("  help");
            Console.WriteLine("  exit");
        }
    }
}
