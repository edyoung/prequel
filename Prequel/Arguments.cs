﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Prequel
{
    public class Input
    {
        public string Path { get; set; }

        private Stream stream;
        public Stream Stream {
            get
            {
                if(stream == null)
                {
                    try {
                        stream = File.OpenRead(Path);
                    }
                    catch(ArgumentException ex)
                    {
                        throw new ProgramTerminatingException(
                            String.Format("Error reading file {0}: {1}", Path, ex.Message), ExitReason.IOError);
                    }
                }
                return stream;
            }

            private set { stream = value; }
        }

        public static Input FromString(string inlineInput)
        {
            return new Input() { Path = "<inline>", Stream = new MemoryStream(Encoding.UTF8.GetBytes(inlineInput)) }; // correct encoding? 
        }

        public static Input FromFile(string path)
        {
            return new Input() { Path = path }; // defer opening the file until we want to read it
        }
    }

    public class Arguments
    {
        private string[] args;
        private IList<Input> inputs = new List<Input>();

        public Arguments(params string[] args)
        {
            SetDefaults();
            if (args.Length == 0)
            {
                throw new ProgramTerminatingException("You must specify at least one file to check");
            }
            this.args = args;

            for (int i = 0; i < args.Length; ++i)
            {
                ProcessArgument(i, args);
            }
        }

        private void SetDefaults()
        {
            SqlParserType = SqlParserFactory.DefaultType;
            DisplayLogo = true;
            WarningLevel = WarningLevel.Serious;
        }

        public IList<Input> Inputs {
            get { return inputs; }
        }

        public Type SqlParserType { get; private set; }
        public static string UsageDescription {
            get
            {
                return String.Format(@"Usage: Prequel.exe [flags] file1.sql [file2.sql ...]

Flags:
/?                  Print this message and exit
/v:version          Specify the SQL dialect to use. Default 2014, options: {0}
/i:'select ...'     Give a string of inline sql to parse and check
/nologo             Don't print program name and version info
/warn:level         0-3. 0 = syntax errors only, 1 critical warnings, 2 = serious warnings, 3 = all warnings
", 
                        SqlParserFactory.AllVersions);      
            }
        }

        public bool DisplayLogo { get; private set; }
        public WarningLevel WarningLevel { get; set; }

        private void ProcessArgument(int i, string[] args)
        {
            string currentArg = args[i].ToLowerInvariant();
            if (currentArg.StartsWith("/") || currentArg.StartsWith("-"))
            {
                ProcessFlag(currentArg.Substring(1));
            }
            else
            {
                ProcessFile(currentArg);
            }
        }

        private void ProcessFile(string file)
        {
            inputs.Add( Input.FromFile(file));
        }

        private void ProcessFlag(string flag)
        { 
            if (flag == "?")
            {
                throw new ProgramTerminatingException("Usage Information Requested", ExitReason.Success);
            }

            if (flag.StartsWith("v:"))
            {
                string versionString = flag.Substring(2);
                try {
                    SqlParserType = SqlParserFactory.Type(versionString);
                }
                catch(ArgumentException ex)
                {
                    throw new ProgramTerminatingException(ex.Message);
                }
            }
            else if (flag.StartsWith("i:"))
            {
                string inlineSql = flag.Substring(2);
                inputs.Add(Input.FromString(inlineSql));
            }
            else if (flag.Equals("nologo"))
            {
                DisplayLogo = false;
            }
            else if (flag.StartsWith("warn:"))
            {
                string levelString = flag.Substring(5);
                try {
                    int level = (Convert.ToInt32(levelString));
                    if(level < 0 || level > (int)WarningLevel.Max)
                    {
                        throw new ProgramTerminatingException(String.Format("Invalid Warning Level '{0}'", levelString));
                    }
                    WarningLevel = (WarningLevel)level;
                }
                catch(FormatException)
                {
                    throw new ProgramTerminatingException(String.Format("Invalid Warning Level '{0}'", levelString));
                }
            }
        }

        
    }
}
