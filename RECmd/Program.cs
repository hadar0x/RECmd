﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Fclp;
using NLog;
using NLog.Config;
using NLog.Targets;
using Registry;
using Registry.Abstractions;
using Registry.Other;

namespace RECmd
{
    internal class Program
    {
        private static Stopwatch _sw;
        private static Logger _logger;

        private static void Main(string[] args)
        {
            //TODO Live Registry support

            var dumpWarning = false;

            var nlogPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "nlog.config");

            if (File.Exists(nlogPath) == false)
            {
                var config = new LoggingConfiguration();
                var loglevel = LogLevel.Info;

                var layout = @"${message}";

                var consoleTarget = new ColoredConsoleTarget();

                config.AddTarget("console", consoleTarget);

                consoleTarget.Layout = layout;

                var rule1 = new LoggingRule("*", loglevel, consoleTarget);
                config.LoggingRules.Add(rule1);

                LogManager.Configuration = config;
                dumpWarning = true;
            }

            _logger = LogManager.GetCurrentClassLogger();

            if (dumpWarning)
            {
                _logger.Warn("Nlog.config missing! Using default values...");
            }

            var p = new FluentCommandLineParser<ApplicationArguments>
            {
                IsCaseSensitive = false
            };

            p.Setup(arg => arg.HiveFile)
                .As("Hive")
                .WithDescription("\tHive to search.").Required();
            // If this option is not specified, the live Registry will be used

            p.Setup(arg => arg.Literal)
                .As("Literal")
                .WithDescription(
                    "\tIf present, --sd and --ss search value will not be interpreted as ASCII or Unicode byte strings");

            p.Setup(arg => arg.RecoverDeleted)
                .As("Recover")
                .WithDescription("\tIf present, recover deleted keys/values");

            p.Setup(arg => arg.Recursive)
                .As("Recursive")
                .WithDescription(
                    "Dump keys/values recursively (ignored if --ValueName used). This option provides FULL details about keys and values");

            p.Setup(arg => arg.RegEx)
                .As("RegEx")
                .WithDescription("\tIf present, treat <string> in --sk, --sv, --sd, and --ss as a regular expression")
                .SetDefault(false);

            p.Setup(arg => arg.Sort)
                .As("Sort")
                .WithDescription(
                    "\tIf present, sort the output").SetDefault(false);

            p.Setup(arg => arg.SuppressData)
                .As("SuppressData")
                .WithDescription(
                    "If present, do not show data when using --sd or --ss\r\n").SetDefault(false);

            p.Setup(arg => arg.KeyName)
                .As("KeyName")
                .WithDescription(
                    "\tKey name. All values under this key will be dumped");

            p.Setup(arg => arg.ValueName)
                .As("ValueName")
                .WithDescription(
                    "Value name. Only this value will be dumped");

            p.Setup(arg => arg.SaveToName)
                .As("SaveToName")
                .WithDescription("Saves ValueName value data in binary form to file\r\n");

            p.Setup(arg => arg.StartDate)
                .As("StartDate")
                .WithDescription(
                    "Start date to look for last write timestamps (UTC). If EndDate is not supplied, last writes AFTER this date will be returned");

            p.Setup(arg => arg.EndDate)
                .As("EndDate")
                .WithDescription(
                    "\tEnd date to look for last write timestamps (UTC). If StartDate is not supplied, last writes BEFORE this date will be returned");

            p.Setup(arg => arg.MinimumSize)
                .As("MinSize")
                .WithDescription("\tFind values with data size >= MinSize (specified in bytes)");

            p.Setup(arg => arg.SimpleSearchKey)
                .As("sk")
                .WithDescription("\tSearch for <string> in key names.");

            p.Setup(arg => arg.SimpleSearchValue)
                .As("sv")
                .WithDescription("\tSearch for <string> in value names");

            p.Setup(arg => arg.SimpleSearchValueData)
                .As("sd")
                .WithDescription("\tSearch for <string> in value record's value data");

            p.Setup(arg => arg.SimpleSearchValueSlack)
                .As("ss")
                .WithDescription("\tSearch for <string> in value record's value slack");

            var header =
                $"RECmd version {Assembly.GetExecutingAssembly().GetName().Version}" +
                "\r\n\r\nAuthor: Eric Zimmerman (saericzimmerman@gmail.com)" +
                "\r\nhttps://github.com/EricZimmerman/RECmd\r\n\r\nNote: Enclose all strings containing spaces (and all RegEx) with double quotes";

            var footer = @"Example: RECmd.exe --Hive ""C:\Temp\UsrClass 1.dat"" --sk URL --Recover" + "\r\n\t " +
                         @"RECmd.exe --Hive ""D:\temp\UsrClass 1.dat"" --StartDate ""11/13/2014 15:35:01"" " + "\r\n\t " +
                         @"RECmd.exe --Hive ""D:\temp\UsrClass 1.dat"" --RegEx --sv ""(App|Display)Name"" " + "\r\n\t " +
                         @"RECmd.exe --Hive ""D:\temp\UsrClass 1.dat"" --StartDate ""05/20/2014 19:00:00"" --EndDate ""05/20/2014 23:59:59"" " +
                         "\r\n\t " +
                         @"RECmd.exe --Hive ""D:\temp\UsrClass 1.dat"" --StartDate ""05/20/2014 07:00:00 AM"" --EndDate ""05/20/2014 07:59:59 PM"" ";

            p.SetupHelp("?", "help").WithHeader(header).Callback(text => _logger.Info(text + "\r\n" + footer));

            var result = p.Parse(args);

            if (result.HelpCalled)
            {
                return;
            }

            if (result.HasErrors)
            {
                p.HelpOption.ShowHelp(p.Options);

                return;
            }

            var hiveToProcess = "Live Registry";

            if (p.Object.HiveFile?.Length > 0)
            {
                hiveToProcess = p.Object.HiveFile;
            }

            _logger.Info(header);
            _logger.Info("");

            _logger.Info($"Processing hive '{hiveToProcess}'");

            _logger.Info("");

            if (File.Exists(hiveToProcess) == false)
            {
                _logger.Warn($"'{hiveToProcess}' does not exist. Exiting");
                return;
            }

            try
            {
                var reg = new RegistryHive(hiveToProcess)
                {
                    RecoverDeleted = p.Object.RecoverDeleted
                };

                _sw = new Stopwatch();
                _sw.Start();

                reg.ParseHive();

                _logger.Info("");

                if (p.Object.KeyName.Length > 0)
                {
                    var key = reg.GetKey(p.Object.KeyName);

                    if (key == null)
                    {
                        _logger.Warn($"Key '{p.Object.KeyName}' not found.");
                        DumpStopWatchInfo();
                        return;
                    }

                    if (p.Object.ValueName.Length > 0)
                    {
                        var val = key.Values.SingleOrDefault(c => c.ValueName == p.Object.ValueName);

                        if (val == null)
                        {
                            _logger.Warn($"Value '{p.Object.ValueName}' not found for key '{p.Object.KeyName}'.");

                            DumpStopWatchInfo();
                            return;
                        }

                        _sw.Stop();
                        _logger.Info(val);

                        if (p.Object.SaveToName.Length > 0)
                        {
                            var baseDir = Path.GetDirectoryName(p.Object.SaveToName);
                            if (Directory.Exists(baseDir) == false)
                            {
                                Directory.CreateDirectory(baseDir);
                            }

                            _logger.Info($"Saving contents of '{val.ValueName}' to '{p.Object.SaveToName}'");
                            File.WriteAllBytes(p.Object.SaveToName, val.ValueDataRaw);
                        }

                        DumpStopWatchInfo();

                        return;
                    }

                    _sw.Stop();

                    DumpRootKeyName(reg);

                    DumpKey(key, p.Object.Recursive);

                    DumpStopWatchInfo();
                }
                else if (p.Object.MinimumSize > 0)
                {
                    var hits = reg.FindByValueSize(p.Object.MinimumSize).ToList();
                    _sw.Stop();

                    if (p.Object.Sort)
                    {
                        hits = hits.OrderBy(t => t.Value.ValueDataRaw.Length).ToList();
                    }

                    DumpRootKeyName(reg);

                    foreach (var valueBySizeInfo in hits)
                    {
                        _logger.Info(
                            $"Key: {Helpers.StripRootKeyNameFromKeyPath(valueBySizeInfo.Key.KeyPath)}, Value: {valueBySizeInfo.Value.ValueName}, Size: {valueBySizeInfo.Value.ValueDataRaw.Length:N0}");
                    }
                    _logger.Info("");
                    var plural = "s";
                    if (hits.Count() == 1)
                    {
                        plural = "";
                    }
                    _logger.Info(
                        $"Found {hits.Count():N0} value{plural} with size greater or equal to {p.Object.MinimumSize:N0} bytes");
                    DumpStopWatchInfo();
                }
                else if (p.Object.StartDate != null || p.Object.EndDate != null)
                {
                    DateTimeOffset start;
                    DateTimeOffset end;
                    var startOk = DateTimeOffset.TryParse(p.Object.StartDate + "-0", out start);
                    var endOk = DateTimeOffset.TryParse(p.Object.EndDate + "-0", out end);

                    DateTimeOffset? startGood = null;
                    DateTimeOffset? endGood = null;
                    var hits = new List<SearchHit>();

                    if (!startOk && p.Object.StartDate != null)
                    {
                        throw new InvalidCastException("'StartDate' is not a valid datetime value");
                    }
                    if (!endOk && p.Object.EndDate != null)
                    {
                        throw new InvalidCastException("'EndDate' is not a valid datetime value");
                    }

                    if (startOk && endOk)
                    {
                        startGood = start;
                        endGood = end;
                        hits = reg.FindByLastWriteTime(startGood, endGood).ToList();
                    }
                    else if (startOk)
                    {
                        startGood = start;

                        hits = reg.FindByLastWriteTime(startGood, null).ToList();
                    }
                    else if (endOk)
                    {
                        endGood = end;
                        hits = reg.FindByLastWriteTime(null, endGood).ToList();
                    }

                    _sw.Stop();

                    if (p.Object.Sort)
                    {
                        hits = hits.OrderBy(t => t.Key.LastWriteTime ?? new DateTimeOffset()).ToList();
                    }

                    DumpRootKeyName(reg);

                    foreach (var searchHit in hits)
                    {
                        searchHit.StripRootKeyName = true;
                        _logger.Info($"Last write: {searchHit.Key.LastWriteTime}  Key: {searchHit}");
                    }

                    var suffix = string.Empty;

                    if (startGood != null || endGood != null)
                    {
                        suffix = $"between {startGood} and {endGood}";
                    }
                    if (startGood != null && endGood == null)
                    {
                        suffix = $"after {startGood}";
                    }
                    else if (endGood != null && startGood == null)
                    {
                        suffix = $"before {endGood}";
                    }

                    _logger.Info("");
                    var plural = "s";
                    if (hits.Count() == 1)
                    {
                        plural = "";
                    }
                    _logger.Info($"Found {hits.Count():N0} key{plural} with last write {suffix}");

                    DumpStopWatchInfo();
                }
                else if (p.Object.SimpleSearchKey.Length > 0 || p.Object.SimpleSearchValue.Length > 0 ||
                         p.Object.SimpleSearchValueData.Length > 0 || p.Object.SimpleSearchValueSlack.Length > 0)
                {
                    List<SearchHit> hits = null;

                    if (p.Object.SimpleSearchKey.Length > 0)
                    {
                        hits = reg.FindInKeyName(p.Object.SimpleSearchKey, p.Object.RegEx).ToList();
                        if (p.Object.Sort)
                        {
                            hits = hits.OrderBy(t => t.Key.KeyName).ToList();
                        }
                    }
                    else if (p.Object.SimpleSearchValue.Length > 0)
                    {
                        hits = reg.FindInValueName(p.Object.SimpleSearchValue, p.Object.RegEx).ToList();
                        if (p.Object.Sort)
                        {
                            hits = hits.OrderBy(t => t.Value.ValueName).ToList();
                        }
                    }
                    else if (p.Object.SimpleSearchValueData.Length > 0)
                    {
                        hits =
                            reg.FindInValueData(p.Object.SimpleSearchValueData, p.Object.RegEx, p.Object.Literal)
                                .ToList();
                        if (p.Object.Sort)
                        {
                            hits = hits.OrderBy(t => t.Value.ValueData).ToList();
                        }
                    }
                    else if (p.Object.SimpleSearchValueSlack.Length > 0)
                    {
                        hits =
                            reg.FindInValueDataSlack(p.Object.SimpleSearchValueSlack, p.Object.RegEx, p.Object.Literal)
                                .ToList();
                        if (p.Object.Sort)
                        {
                            hits = hits.OrderBy(t => t.Value.ValueData).ToList();
                        }
                    }

                    if (hits == null)
                    {
                        _logger.Warn("No search results found");
                        DumpStopWatchInfo();
                        return;
                    }

                    _sw.Stop();

                    DumpRootKeyName(reg);

                    //set up highlighting
                    var words = new List<string>();

                    if (p.Object.SimpleSearchKey.Length > 0)
                    {
                        words.Add(p.Object.SimpleSearchKey);
                    }
                    else if (p.Object.SimpleSearchValue.Length > 0)
                    {
                        words.Add(p.Object.SimpleSearchValue);
                    }
                    else if (p.Object.SimpleSearchValueData.Length > 0)
                    {
                        //TODO REFACTOR TO REMOVE DUPE CODE
                        if (p.Object.RegEx)
                        {
                            words.Add(p.Object.SimpleSearchValueData);
                        }
                        else
                        {
                            var w = p.Object.SimpleSearchValueData;
                            var hex = Encoding.ASCII.GetBytes(w);

                            var asAscii = BitConverter.ToString(hex);

                            hex = Encoding.Unicode.GetBytes(w);
                            var asUnicode = BitConverter.ToString(hex);

                            words.Add(p.Object.SimpleSearchValueData);
                            words.Add(asAscii);
                            words.Add(asUnicode);
                        }
                    }
                    else if (p.Object.SimpleSearchValueSlack.Length > 0)
                    {
                        if (p.Object.RegEx)
                        {
                            words.Add(p.Object.SimpleSearchValueSlack);
                        }
                        else
                        {
                            var w = p.Object.SimpleSearchValueSlack;
                            var hex = Encoding.ASCII.GetBytes(w);

                            var asAscii = BitConverter.ToString(hex);

                            hex = Encoding.Unicode.GetBytes(w);
                            var asUnicode = BitConverter.ToString(hex);

                            words.Add(p.Object.SimpleSearchValueSlack);
                            words.Add(asAscii);
                            words.Add(asUnicode);
                        }
                    }

                    AddHighlightingRules(words, p.Object.RegEx);

                    foreach (var searchHit in hits)
                    {
                        searchHit.StripRootKeyName = true;

                        if (p.Object.SimpleSearchValueData.Length > 0 || p.Object.SimpleSearchValueSlack.Length > 0)
                        {
                            if (p.Object.SuppressData)
                            {
                                _logger.Info(
                                    $"Key: {Helpers.StripRootKeyNameFromKeyPath(searchHit.Key.KeyPath)}, Value: {searchHit.Value.ValueName}");
                            }
                            else
                            {
                                if (p.Object.SimpleSearchValueSlack.Length > 0)
                                {
                                    _logger.Info(
                                        $"Key: {Helpers.StripRootKeyNameFromKeyPath(searchHit.Key.KeyPath)}, Value: {searchHit.Value.ValueName}, Slack: {searchHit.Value.ValueSlack}");
                                }
                                else
                                {
                                    _logger.Info(
                                        $"Key: {Helpers.StripRootKeyNameFromKeyPath(searchHit.Key.KeyPath)}, Value: {searchHit.Value.ValueName}, Data: {searchHit.Value.ValueData}");
                                }
                            }
                        }
                        else if (p.Object.SimpleSearchKey.Length > 0)
                        {
                            _logger.Info($"Key: {Helpers.StripRootKeyNameFromKeyPath(searchHit.Key.KeyPath)}");
                        }
                        else if (p.Object.SimpleSearchValue.Length > 0)
                        {
                            _logger.Info(
                                $"Key: {Helpers.StripRootKeyNameFromKeyPath(searchHit.Key.KeyPath)}, Value: {searchHit.Value.ValueName}");
                        }
                    }

                    var suffix = string.Empty;
                    var withRegex = string.Empty;

                    var plural = "s";
                    if (hits.Count() == 1)
                    {
                        plural = "";
                    }

                    if (p.Object.SimpleSearchValueData.Length > 0)
                    {
                        suffix = $"value data hit{plural}";
                    }
                    else if (p.Object.SimpleSearchValueSlack.Length > 0)
                    {
                        suffix = $"value slack hit{plural}";
                    }
                    else if (p.Object.SimpleSearchKey.Length > 0)
                    {
                        suffix = $"key{plural}";
                    }
                    else if (p.Object.SimpleSearchValue.Length > 0)
                    {
                        suffix = $"value{plural}";
                    }

                    if (p.Object.RegEx)
                    {
                        withRegex = " (via RegEx)";
                    }

                    _logger.Info("");

                    _logger.Info($"Found {hits.Count():N0} {suffix}{withRegex}");


                    DumpStopWatchInfo();
                }
                else
                {
                    _logger.Warn("Nothing to do! =(");
                }

                //TODO search deleted?? should only need to look in reg.UnassociatedRegistryValues
            }
            catch (Exception ex)
            {
                _logger.Error($"There was an error: {ex.Message}");
            }
        }

        private static void AddHighlightingRules(List<string> words, bool isRegEx = false)
        {
            var target = (ColoredConsoleTarget) LogManager.Configuration.FindTargetByName("console");
            var rule = target.WordHighlightingRules.FirstOrDefault();

            var bgColor = ConsoleOutputColor.Green;
            var fgColor = ConsoleOutputColor.Red;

            if (rule != null)
            {
                bgColor = rule.BackgroundColor;
                fgColor = rule.ForegroundColor;
            }

            target.WordHighlightingRules.Clear();

            foreach (var word in words)
            {
                var r = new ConsoleWordHighlightingRule();
                r.IgnoreCase = true;
                if (isRegEx)
                {
                    r.Regex = word;
                }
                else
                {
                    r.Text = word;
                }
                r.ForegroundColor = fgColor;
                r.BackgroundColor = bgColor;

                r.WholeWords = false;
                target.WordHighlightingRules.Add(r);
            }
        }

        private static void DumpRootKeyName(RegistryHive reg)
        {
            _logger.Info($"Root key name: {reg.Root.KeyName}");
            _logger.Info("");
        }

        private static void DumpKey(RegistryKey key, bool recursive)
        {
            if (recursive)
            {
                _logger.Info(key);
            }
            else
            {
                _logger.Info($"Key: {Helpers.StripRootKeyNameFromKeyPath(key.KeyPath)}");
                _logger.Info($"Last write time: {key.LastWriteTime}");
                _logger.Info($"Number of Values: {key.Values.Count:N0}");
                _logger.Info($"Number of Subkeys: {key.SubKeys.Count:N0}");
                _logger.Info("");

                var i = 0;

                foreach (var sk in key.SubKeys)
                {
                    _logger.Info($"------------ Subkey #{i:N0} ------------");
                    _logger.Info($"Name: {sk.KeyName} (Last write: {sk.LastWriteTime})");
                    i += 1;
                }

                i = 0;
                _logger.Info("");

                foreach (var keyValue in key.Values)
                {
                    _logger.Info($"------------ Value #{i:N0} ------------");
                    _logger.Info($"Name: {keyValue.ValueName} ({keyValue.ValueType})");

                    var slack = "";

                    if (keyValue.ValueSlack.Length > 0)
                    {
                        slack = $"(Slack: {keyValue.ValueSlack})";
                    }
                    _logger.Info($"Data: {keyValue.ValueData} {slack}");

                    i += 1;
                }
            }
        }

        private static void DumpStopWatchInfo()
        {
            _sw.Stop();
            _logger.Info("");
            _logger.Info($"Search took {_sw.Elapsed.TotalSeconds:N3} seconds");
        }
    }

    internal class ApplicationArguments
    {
        public string HiveFile { get; set; } = string.Empty;
        public bool RecoverDeleted { get; set; } = false;
        public string KeyName { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string SaveToName { get; set; } = string.Empty;
        public bool Recursive { get; set; } = false;
        public string SimpleSearchKey { get; set; } = string.Empty;
        public string SimpleSearchValue { get; set; } = string.Empty;
        public string SimpleSearchValueData { get; set; } = string.Empty;
        public string SimpleSearchValueSlack { get; set; } = string.Empty;
        public int MinimumSize { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public bool Sort { get; set; }
        public bool RegEx { get; set; }
        public bool Literal { get; set; }
        public bool SuppressData { get; set; }
    }
}