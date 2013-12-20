using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace LogCheck
{
    class Program
    {
        const string IniFileName = "logcheck.ini";

        static void Log(string msg)
        {
            Console.WriteLine(msg);
        }

        class ConversionRule
        {
            public int Index { get; set; }
            public int Start { get; set; }
            public int Length { get; set; }
            public Dictionary<string, string> Conversions { get; set; }
        }

        static string RepairEDILine(string ediLine, string logLine, ConversionRule[] rules, ref int nrChanged)
        {
            string[] ediFields = ediLine.Split(';');
            var changed = false;
            foreach (var rule in rules)
            {
                var readField = logLine.Length > rule.Start + rule.Length ? logLine.Substring(rule.Start, rule.Length).TrimEnd() : string.Empty;
                string convertedValue;
                if (rule.Conversions != null)
                    readField = rule.Conversions.TryGetValue(readField, out convertedValue) ? convertedValue : ediFields[rule.Index];
                if (ediFields.Length > rule.Index && string.Compare(ediFields[rule.Index], readField) != 0)
                {
                    changed = true;
                    ediFields[rule.Index] = readField;
                }
            }
            if (changed) nrChanged++;
            return string.Join(";", ediFields);
        }

        static string ToEdiDate(string logDate)
        {
            var parts = logDate.Trim().Split('-');
            return parts[2] + parts[0] + parts[1];
        }

        static string ToEdiMode(string logMode)
        {
            logMode = logMode.Trim();
            return (
                logMode == "SSB" ? 1 :
                logMode == "CW" ? 2 :
                logMode == "AM" ? 5 :
                logMode == "FM" ? 6 :
                logMode == "RTTY" ? 7 :
                logMode == "SSTV" ? 8 :
                logMode == "ATV" ? 9 : 0
                ).ToString();
        }

        static string RegexConvert(string ediLine, string logLine)
        {
            //111016;0901;SK7MW;2;;500;55;56;;JO65MJ;550;;N;N;
            //432   SSB 10-16-11  0901  SK7MW        500         55    56     JO65mj  550   
            //111016;1923;DL0VV;2;;500;54;54;;JO64AD;443;;N;;
            //432   SSB 10-16-11  1923  DL0VV        500         54    54     JO64ad  443   

            var regExLog = new Regex(@"^(?<first>.{6})(?<mode>.{4})(?<date>.{10})(?<time>.{6})(?<call>.{13})(?<sQSO>.{12})(?<rRST>.{6})(?<rQSO>.{7})(?<rWWL>.{8})(?<QSOPoints>.{6})$");
            var match = regExLog.Match(logLine);
            var edi = ediLine.Split(';');
            if (!match.Success) return ediLine;
            return string.Join(";", new [] 
            { 
                ToEdiDate(match.Groups["date"].Value),
                match.Groups["time"].Value,
                match.Groups["call"].Value,
                ToEdiMode(match.Groups["mode"].Value),
                edi[4],
                match.Groups["sQSO"].Value,
                match.Groups["rRST"].Value,
                match.Groups["rQSO"].Value,
                edi[8],
                match.Groups["rWWL"].Value,
                match.Groups["QSOPoints"].Value,
                edi[11],
                edi[12],
                edi[13],
                edi[14]
            }.Select(field => field.Trim().ToUpper()));
        }

        static bool RepareEDI(string logName, string ediName, int skipLines, ConversionRule[] rules, int useRegex)
        {
            //(0)111016;(1)0901;(2)SK7MW;(3)2;(4);(5)500;(6)55;(7)56;(8);(9)JO65MJ;(10)550;(11);(12)N;(13)N;(14)
            // http://www.vushf.dk/pages/contest/reg1test.htm
            // 0 Date; 111016
            // 1 Time; 0901
            // 2 Call; SK7MW
            // 3 Mode code; 2
            // 4 Sent-RST;
            // 5 Sent QSO number; 500
            // 6 Received-RST; 55
            // 7 Received QSO number; 56
            // 8 Received exchange;
            // 9 Received-WWL; JO65MJ
            //10 QSO-Points; 550
            //11 New-Exchange-(N);
            //12 New-WWL-(N); N
            //13 New-DXCC-(N); N
            //14 Duplicate-QSO-(D)

            List<string> logLines = new List<string>(File.ReadAllLines(logName).Where(ln => ln.Length > skipLines + 1).Skip(skipLines));
            List<string> ediLines = new List<string>(File.ReadAllLines(ediName));
            // skip first 2 logLines
            // ediLog has line: [QSORecords;xxx]
            // log: fixed length, de velden op pos 51, 6 en 57,7
            // log: 432   SSB 10-16-11  2002  PA0FEI       500         51008 55001  JO33bc  34    
            // edi ;delimited veld 6 en 7
            // edi: 111016;2002;PA0FEI;2;;500;510;5500;;JO33BC;34;;N;;
            // 510 => 51008
            // 5500 => 55001
            if (ediLines.Count > logLines.Count && ediLines[ediLines.Count - logLines.Count - 1] == string.Format("[QSORecords;{0}]", logLines.Count))
            {
                var index = 0;
                var nrChanged = 0;
                for (int i = ediLines.Count - logLines.Count; i < ediLines.Count; i++)
                {
                    var log = logLines[index++];
                    var edi = ediLines[i];
                    ediLines[i] = useRegex == 0 ? RepairEDILine(edi, log, rules, ref nrChanged) : RegexConvert(edi, log);
                }
                string newEdiFile = ediName.Replace(".edi", "_V.edi");
                File.WriteAllLines(newEdiFile, ediLines.ToArray());
                System.Diagnostics.Process.Start("notepad.exe", newEdiFile);
                Log(logName + " en " + ediName + " zijn succesvol samengevoegd tot " + newEdiFile + ".");
                Log("Er zijn " + nrChanged + " regels aangepast.");
                return true;
            }
            Log("Kan " + logName + " en " + ediName + " niet samenvoegen. Aantallen QSORecords komen niet overeen.");
            Log("Aantal QSORecords in " + logName + "=" + logLines.Count);
            Log("Aantal QSORecords in " + ediName + " = onbekend");
            return false;
        }

        static void Help()
        {
            Log("Helaas, het converteren is niet gelukt.");
            Log("Gebruik: LogCheck.exe <ediBestandsNaam> <logBestandsnaam>");
            Log("Mag ook alleen met de ediBestandsnaam, en dan probeert het programma het logbestand te vinden");
            Log("Er wordt een nieuw edi bestand aangemaakt met een _V achter de bestandsnaam");
        }

        static bool LegalFileName(string chk, string ext, out string fileName)
        {
            fileName = chk;
            chk = chk.ToLower();
            ext = ext.ToLower();
            if (chk.Length > 4 && chk.Substring(chk.Length - 4, 1) == ".")
                chk = chk.Substring(0, chk.Length - 4);
            fileName = chk + ext;
            return File.Exists(fileName);
        }

        static int GetSettingValue(Dictionary<string, string> settings, string name, int defaultValue)
        {
            var val = defaultValue.ToString();
            if (!settings.TryGetValue(name.ToLower(), out val)) return defaultValue;
            int newValue;
            if (!int.TryParse(val, out newValue)) return defaultValue;
            return newValue;
        }

        static void Main(string[] args)
        {
            if (!File.Exists(IniFileName))
                File.WriteAllLines(IniFileName, new[] {
                    "skiplines=2",
                    "maxEdiFields=15",
                    "useRegex=0",
                    "rule6start=51",
                    "rule6length=6",
                    "rule7start=57",
                    "rule7length=7",
                    "rule3start=6",
                    "rule3length=4",
                    "rule3conv=SSB,1#CW,2",
                });

            var settings = File.ReadAllLines(IniFileName)
                .Where(ln => ln.Split('=').Length == 2)
                .ToDictionary(key => key.Split('=')[0].ToLower().Trim(), val => val.Split('=')[1].Trim());
            
            var skipLines = GetSettingValue(settings, "skiplines", 2);
            var maxEdiFields = GetSettingValue(settings, "maxEdiFields", 14);
            var useRegex = GetSettingValue(settings, "useRegex", 0);

            Log("SkipLines=" + skipLines.ToString());
            Log("MaxEdiFields=" + maxEdiFields.ToString());
            Log("UseRegex=" + useRegex.ToString());

            List<ConversionRule> rules = new List<ConversionRule>();
            for (var i = 0; i < maxEdiFields; i++)
            {
                var start = GetSettingValue(settings, "rule" + i + "start", -1);
                var length = GetSettingValue(settings, "rule" + i + "length", -1);
                if (start > -1 && length > -1)
                {
                    Log(i.ToString() + "," + start.ToString() + "," + length.ToString());
                    var rule = new ConversionRule { Index = i, Start = start, Length = length }; 
                    string conv;
                    if (settings.TryGetValue("rule" + i + "conv", out conv))
                        rule.Conversions = conv.Split('#').ToDictionary(key => key.Split(',')[0], value => value.Split(',')[1]);
                    rules.Add(rule);
                }
            }
            string ediName, logName;
            if (args.Length > 0 && LegalFileName(args[0], ".edi", out ediName))
            {
                if (args.Length == 2 && LegalFileName(args[1], ".log", out logName) && RepareEDI(logName, ediName, skipLines, rules.ToArray(), useRegex))
                    return;
                else if (args.Length == 1)
                {
                    if (LegalFileName(args[0], ".log", out logName) && RepareEDI(logName, ediName, skipLines, rules.ToArray(), useRegex))
                        return;
                    string path = Path.GetDirectoryName(args[0]);
                    string[] logFiles = Directory.GetFiles(path, "*.log");
                    for (int i = 0; i < logFiles.Length; i++)
                        if (RepareEDI(logFiles[i], ediName, skipLines, rules.ToArray(), useRegex))
                            return;
                }
            }
            Help();
        }
    }
}
