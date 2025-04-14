using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WfcPatcher {
	static class CommandLineArguments {
		public static string[] Filenames { get; private set; }
		public static string ModulusFilename { get; private set; }

		public static bool ParseCommandLineArguments( string[] args ) {
			bool parseSuccess = true;

			try {
				List<string> filenames = new List<string>();

				for ( int i = 0; i < args.Length; ++i ) {
					switch ( args[i] ) {
						case "-mf":
						case "--modulus-file":
							ModulusFilename = args[++i];
                            break;
						default:
							filenames.Add( args[i] );
							break;
					}
				}

				Filenames = filenames.ToArray();
			} catch ( IndexOutOfRangeException ) {
				Console.WriteLine( "Last given option needs more parameters!" );
				parseSuccess = false;
			}

			return parseSuccess;
		}
	}
}
