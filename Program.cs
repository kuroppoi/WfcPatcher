using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace WfcPatcher {
	class Program {

		private static byte[] EVILCHECK_MODULUS = {
            0xD9, 0x87, 0xD4, 0x65, 0xE4, 0xEE, 0xAE, 0x58, 0x2D, 0x01, 0x73, 0x15, 0xF0, 0x0E, 0xA3, 0x40,
            0x0C, 0x51, 0x0B, 0x2E, 0x51, 0xE1, 0x5D, 0x77, 0xD0, 0x3A, 0xDC, 0xB2, 0x5C, 0x83, 0x01, 0x71,
            0xF5, 0x69, 0xFB, 0xD2, 0x6A, 0x78, 0xDC, 0x69, 0x69, 0x4D, 0xDD, 0x2C, 0xEF, 0xA4, 0xA9, 0xAA,
            0xD1, 0xA0, 0xD9, 0xAA, 0x99, 0x70, 0x5B, 0xF0, 0x80, 0x38, 0xF5, 0x77, 0x64, 0xEE, 0xA5, 0xAB,
            0x7D, 0x6A, 0x38, 0x38, 0x67, 0x8A, 0xEC, 0x26, 0x2E, 0x95, 0x2A, 0x1C, 0xDB, 0xB8, 0xE2, 0xFF,
            0x68, 0xDC, 0x93, 0x2E, 0x7F, 0x8E, 0x3A, 0xEC, 0xD1, 0xFE, 0x52, 0x82, 0xEA, 0xCA, 0x41, 0x61,
            0xC2, 0x20, 0x3F, 0xF0, 0x98, 0xF7, 0x9D, 0x67, 0x35, 0xE6, 0x44, 0x14, 0xE1, 0x85, 0xFB, 0xB3,
            0xEC, 0x04, 0x3D, 0x83, 0x8D, 0x9B, 0x4B, 0x19, 0x07, 0x23, 0x31, 0xC3, 0xF7, 0x98, 0x57, 0xE5
        };

		public static string ProgramName {
			get {
				Version version = System.Reflection.Assembly.GetEntryAssembly().GetName().Version;
#if DEBUG
				string versionString = version.ToString() + " (Debug)";
#else
				string versionString = version.Major + "." + version.Minor;
#endif
				return "WfcPatcher " + versionString;
			}
		}

		static void Main( string[] args ) {
			Console.WriteLine( ProgramName );
			Console.WriteLine();

			if ( !CommandLineArguments.ParseCommandLineArguments( args ) ) {
				Console.WriteLine( "Error parsing command line options!" );
				PrintUsage();
				return;
			}

			if ( CommandLineArguments.Filenames.Length == 0 || CommandLineArguments.ModulusFilename == null ) {
				PrintUsage();
				return;
			}

			byte[] customModulus = null;

			try
			{
                customModulus = File.ReadAllBytes(CommandLineArguments.ModulusFilename);
			} catch(Exception ex) {
				Console.WriteLine("Couldn't read modulus file");
				Console.WriteLine(ex.ToString());
				Console.WriteLine();
				return;
			}

			if(customModulus.Length != EVILCHECK_MODULUS.Length) {
				Console.WriteLine("Modulus length must be " + EVILCHECK_MODULUS.Length + " bytes.");
				return;
			}

			string domainFilenamePart = "Patched";

			foreach ( string filename in CommandLineArguments.Filenames ) {
				string newFilename = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( filename ), System.IO.Path.GetFileNameWithoutExtension( filename ) ) + " (" + domainFilenamePart + ")" + System.IO.Path.GetExtension( filename );
#if !DEBUG
				try {
#endif
					if ( PatchFile( filename, newFilename, customModulus) ) {
						Console.WriteLine( "Patched to " + newFilename + "!" );
						Console.WriteLine();
					} else {
						Console.WriteLine( "Found nothing to patch in " + filename + "." );
						Console.WriteLine();
						System.IO.File.Delete( newFilename );
					}
#if !DEBUG
				} catch ( Exception ex ) {
					Console.WriteLine( "Failed patching " + filename );
					Console.WriteLine( ex.ToString() );
					Console.WriteLine();
					System.IO.File.Delete( newFilename );
				}
#endif
			}
		}

		public static void PrintUsage() {
			Console.WriteLine( "Usage: WfcPatcher --modulus-file [custom_modulus.bin] game1.nds [game2.nds] [game3.nds] [...]" );
			Console.WriteLine();
			Console.WriteLine( "Command line options:" );
			Console.WriteLine( "  -mf --modulus-file [custom_modulus.bin" );
			Console.WriteLine( "    The file containing the custom public key modulus to patch into the ROM." );
		}

		public static string GetGamecode( System.IO.FileStream nds ) {
			long pos = nds.Position;
			nds.Position = 0x0C;
			string gamecode = nds.ReadAscii( 4 );
			nds.Position = pos;
			return gamecode;
		}

		static bool PatchFile( string filename, string newFilename, byte[] customModulus) {
			Console.WriteLine( "Reading and copying " + filename + "..." );
			using ( var nds = new System.IO.FileStream( newFilename, System.IO.FileMode.Create ) ) {
				using ( var ndsSrc = new System.IO.FileStream( filename, System.IO.FileMode.Open ) ) {
					Util.CopyStream( ndsSrc, nds, (int)ndsSrc.Length );
					ndsSrc.Close();
				}

				// http://dsibrew.org/wiki/DSi_Cartridge_Header

				// arm
				Console.WriteLine( "Patching ARM Executables..." );
				nds.Position = 0x20;
				uint arm9offset = nds.ReadUInt32();
				uint arm9entry = nds.ReadUInt32();
				uint arm9load = nds.ReadUInt32();
				uint arm9size = nds.ReadUInt32();
				uint arm7offset = nds.ReadUInt32();
				uint arm7entry = nds.ReadUInt32();
				uint arm7load = nds.ReadUInt32();
				uint arm7size = nds.ReadUInt32();

				bool modArm9 = PatchArm9( nds, arm9offset, arm9size, customModulus);
				bool modArm7 = PatchArm7( nds, arm7offset, arm7size, customModulus);

				// overlays
				Console.WriteLine( "Patching Overlays..." );
				nds.Position = 0x50;
				uint arm9overlayoff = nds.ReadUInt32();
				uint arm9overlaylen = nds.ReadUInt32();
				uint arm7overlayoff = nds.ReadUInt32();
				uint arm7overlaylen = nds.ReadUInt32();

				bool modOvl9 = PatchOverlay( nds, arm9overlayoff, arm9overlaylen, customModulus);
				bool modOvl7 = PatchOverlay( nds, arm7overlayoff, arm7overlaylen, customModulus);

				nds.Close();

				return modArm9 || modArm7 || modOvl9 || modOvl7;
			}
		}

		static bool PatchArm9( System.IO.FileStream nds, uint pos, uint len, byte[] customModulus) {
			nds.Position = pos;
			byte[] data = new byte[len];
			nds.Read( data, 0, (int)len );

			// decompress size info: http://www.crackerscrap.com/docs/dsromstructure.html
			// TODO: Is there a better way to figure out if an ARM9 is compressed?

			nds.Position = nds.Position - 8;
			uint compressedSize = nds.ReadUInt24();
			byte headerLength = (byte)nds.ReadByte();
			uint additionalCompressedSize = nds.ReadUInt32();
			uint decompressedSize = additionalCompressedSize + len;

			bool compressed = false;
			byte[] decData = data;

#if DEBUG
			Console.WriteLine( "ARM9 old dec size: 0x" + decompressedSize.ToString( "X6" ) );
			Console.WriteLine( "ARM9 old cmp size: 0x" + compressedSize.ToString( "X6" ) );
			Console.WriteLine( "ARM9 old filesize: 0x" + len.ToString( "X6" ) );
			Console.WriteLine( "ARM9 old diff:     0x" + additionalCompressedSize.ToString( "X6" ) );

			System.IO.File.WriteAllBytes( "arm9-raw.bin", data );
#endif

			blz blz = new blz();
			// if one of these isn't true then it can't be blz-compressed so don't even try
			bool headerLengthValid = ( headerLength >= 8 && headerLength <= 11 );
			bool compressedSizeValid = ( data.Length >= compressedSize + 0x4000 && data.Length <= compressedSize + 0x400B );
			if ( headerLengthValid && compressedSizeValid ) {
				try {
					blz.arm9 = 1;
					byte[] maybeDecData = blz.BLZ_Decode( data );

					if ( maybeDecData.Length == decompressedSize ) {
						compressed = true;
						decData = maybeDecData;
#if DEBUG
						System.IO.File.WriteAllBytes( "arm9-dec.bin", decData );
#endif
					}
				} catch ( blzDecodingException ) {
					compressed = false;
				}
			}

			byte[] decDataUnmodified = (byte[])decData.Clone();
			if ( ReplaceInData( decData, customModulus) ) {
				if ( compressed ) {
					Console.WriteLine( "Replacing and recompressing ARM9..." );
					data = blz.BLZ_Encode( decData, 0 );

					uint newCompressedSize = (uint)data.Length;
					if ( newCompressedSize > len ) {
						// new ARM is actually bigger, redo without the additional nullterm replacement
						decData = decDataUnmodified;
						ReplaceInData( decData, customModulus);
						data = blz.BLZ_Encode( decData, 0, supressWarnings: true );
						newCompressedSize = (uint)data.Length;

						int arm9diff = (int)len - (int)newCompressedSize;
						if ( arm9diff < 0 ) {
							// still too big, remove debug strings
							if ( !RemoveStringsInKnownGames( GetGamecode( nds ), decData ) ) {
								RemoveDebugStrings( decData );
							}
#if DEBUG
							System.IO.File.WriteAllBytes( "arm9-dec-without-debug.bin", decData );
#endif
							data = blz.BLZ_Encode( decData, 0, supressWarnings: true );
							newCompressedSize = (uint)data.Length;

							arm9diff = (int)len - (int)newCompressedSize;
							if ( arm9diff < 0 ) {
								Console.WriteLine( "WARNING: Recompressed ARM9 is " + -arm9diff + " bytes bigger than original!" );
								Console.WriteLine( "         Patched game may be corrupted!" );
#if DEBUG
								System.IO.File.WriteAllBytes( "arm9-too-big-recomp.bin", data );
#endif
							}
						}
					}

					if ( newCompressedSize != len ) {
						// new ARM is (still) different, attempt to find the metadata in the ARM9 secure area and replace that
						bool foundSize = false;
						for ( int i = 0; i < 0x4000; i += 4 ) {
							uint maybeSize = BitConverter.ToUInt32( data, i );
							if ( maybeSize == len + 0x02000000u || maybeSize == len + 0x02004000u ) {
								foundSize = true;

								byte[] newCmpSizeBytes;
								if ( maybeSize == len + 0x02004000u ) {
									newCmpSizeBytes = BitConverter.GetBytes( newCompressedSize + 0x02004000u );
								} else {
									newCmpSizeBytes = BitConverter.GetBytes( newCompressedSize + 0x02000000u );
								}

								data[i + 0] = newCmpSizeBytes[0];
								data[i + 1] = newCmpSizeBytes[1];
								data[i + 2] = newCmpSizeBytes[2];
								data[i + 3] = newCmpSizeBytes[3];
								break;
							}
						}
						if ( !foundSize ) {
							Console.WriteLine( "WARNING: Recompressed ARM9 is different size, and size could not be found in secure area!" );
							Console.WriteLine( "         Patched game will probably not boot!" );
						}
					}
#if DEBUG
					uint newDecompressedSize = (uint)decData.Length;
					uint newAdditionalCompressedSize = newDecompressedSize - newCompressedSize;
					Console.WriteLine( "ARM9 new dec size: 0x" + newDecompressedSize.ToString( "X6" ) );
					Console.WriteLine( "ARM9 new cmp size: 0x" + newCompressedSize.ToString( "X6" ) );
					Console.WriteLine( "ARM9 new diff:     0x" + newAdditionalCompressedSize.ToString( "X6" ) );
#endif
				} else {
					Console.WriteLine( "Replacing ARM9..." );
					data = decData;
				}
#if DEBUG
				System.IO.File.WriteAllBytes( "arm9-new.bin", data );
#endif

				nds.Position = pos;
				nds.Write( data, 0, data.Length );

				int newSize = data.Length;
				int diff = (int)len - newSize;
				
				// copy back footer
				if ( diff > 0 ) {
					List<byte> footer = new List<byte>();
					nds.Position = pos + len;
					if ( nds.PeekUInt32() == 0xDEC00621 ) {
						for ( int j = 0; j < 12; ++j ) {
							footer.Add( (byte)nds.ReadByte() );
						}

						nds.Position = pos + newSize;
						nds.Write( footer.ToArray(), 0, footer.Count );
					}

					// padding
					for ( int j = 0; j < diff; ++j ) {
						nds.WriteByte( 0xFF );
					}
				}

				// write new size
				byte[] newSizeBytes = BitConverter.GetBytes( newSize );
				nds.Position = 0x2C;
				nds.Write( newSizeBytes, 0, 4 );

				// recalculate checksums
				nds.Position = pos;
				ushort secureChecksum = new Crc16().ComputeChecksum( nds, 0x4000, 0xFFFF );
				nds.Position = 0x6C;
				nds.Write( BitConverter.GetBytes( secureChecksum ), 0, 2 );

				nds.Position = 0;
				ushort headerChecksum = new Crc16().ComputeChecksum( nds, 0x15E, 0xFFFF );
				nds.Write( BitConverter.GetBytes( headerChecksum ), 0, 2 );

				return true;
			}

			return false;
		}

		static bool PatchArm7( System.IO.FileStream nds, uint pos, uint len, byte[] customModulus ) {
			nds.Position = pos;
			byte[] data = new byte[len];
			nds.Read( data, 0, (int)len );

			if ( ReplaceInData( data, customModulus) ) {
				Console.WriteLine( "Replacing ARM7..." );
				nds.Position = pos;
				nds.Write( data, 0, data.Length );
				
				return true;
			}

			return false;
		}

		static bool PatchOverlay( System.IO.FileStream nds, uint pos, uint len, byte[] customModulus) {
			// http://sourceforge.net/p/devkitpro/ndstool/ci/master/tree/source/ndsextract.cpp
			// http://sourceforge.net/p/devkitpro/ndstool/ci/master/tree/source/overlay.h
			// header compression info from http://gbatemp.net/threads/recompressing-an-overlay-file.329576/

			nds.Position = 0x048;
			uint fatOffset = nds.ReadUInt32();

			bool modified = false;
			for ( uint i = 0; i < len; i += 0x20 ) {
				nds.Position = pos + i;
				uint id = nds.ReadUInt32();
				uint ramAddr = nds.ReadUInt32();
				uint ramSize = nds.ReadUInt32();
				uint bssSize = nds.ReadUInt32();
				uint sinitInit = nds.ReadUInt32();
				uint sinitInitEnd = nds.ReadUInt32();
				uint fileId = nds.ReadUInt32();
				uint compressedSize = nds.ReadUInt24();
				byte compressedBitmask = (byte)nds.ReadByte();

				nds.Position = fatOffset + 8 * id;
				uint overlayPositionStart = nds.ReadUInt32();
				uint overlayPositionEnd = nds.ReadUInt32();
				uint overlaySize = overlayPositionEnd - overlayPositionStart;

				if ( overlaySize == 0 ) { continue; }

				nds.Position = overlayPositionStart;
				byte[] data = new byte[overlaySize];
				nds.Read( data, 0, (int)overlaySize );

				blz blz = new blz();
				byte[] decData;

				bool compressed = ( compressedBitmask & 0x01 ) == 0x01;
				if ( compressed ) {
					try {
						decData = blz.BLZ_Decode( data );
					} catch ( blzDecodingException ) {
						Console.WriteLine( "WARNING: Decompression of Overlay " + ( i / 0x20 ) + " failed!" );
						decData = data;
						compressed = false;
					}
				} else {
					decData = data;
				}

#if DEBUG
				System.IO.File.WriteAllBytes( "overlay" + ( i / 0x20 ) + "-dec.bin", decData );
#endif

				if ( ReplaceInData( decData, customModulus ) ) {
					modified = true;
					int newOverlaySize;
					int diff;

					// if something was replaced, put it back into the ROM
					if ( compressed ) {
						Console.WriteLine( "Replacing and recompressing overlay " + id + "..." );

						uint newCompressedSize = 0;
						data = blz.BLZ_Encode( decData, 0 );
						newCompressedSize = (uint)data.Length;

						newOverlaySize = data.Length;
						diff = (int)overlaySize - newOverlaySize;

						if ( diff < 0 ) {
							Console.WriteLine( "Removing known debug strings and recompressing overlay " + id + "..." );
							RemoveDebugStrings( decData );
							data = blz.BLZ_Encode( decData, 0, supressWarnings: true );
							newCompressedSize = (uint)data.Length;

							newOverlaySize = data.Length;
							diff = (int)overlaySize - newOverlaySize;
							if ( diff < 0 ) {
								Console.WriteLine( "WARNING: Recompressed overlay is " + -diff + " bytes bigger than original!" );
								Console.WriteLine( "         Patched game may be corrupted!" );
							}
						}

						// replace compressed size, if it was used before
						if ( compressedSize == overlaySize ) {
							byte[] newCompressedSizeBytes = BitConverter.GetBytes( newCompressedSize );
							nds.Position = pos + i + 0x1C;
							nds.Write( newCompressedSizeBytes, 0, 3 );
						}

					} else {
						Console.WriteLine( "Replacing overlay " + id + "..." );

						data = decData;
					}

					newOverlaySize = data.Length;
					diff = (int)overlaySize - newOverlaySize;

					nds.Position = overlayPositionStart;
					nds.Write( data, 0, data.Length );

					overlayPositionEnd = (uint)nds.Position;

					// padding
					for ( int j = 0; j < diff; ++j ) {
						nds.WriteByte( 0xFF );
					}

					// new file end offset
					byte[] newPosEndData = BitConverter.GetBytes( overlayPositionEnd );
					nds.Position = fatOffset + 8 * id + 4;
					nds.Write( newPosEndData, 0, 4 );
				}
			}
			
			return modified;
		}

		static void RemoveDebugStrings( byte[] data ) {
			string[] debugStrings = new string[] {
				"recv buffer size",
				"send buffer size",
				"unknown connect mode",
				"Split packet parse error",
				"NULL byte expected!",
				"Processing adderror packet",
				"Out of memory.",
				" buf->buffer",
			};

			foreach ( string s in debugStrings ) {
				byte[] searchBytes = Encoding.ASCII.GetBytes( s );
				var results = data.Locate( searchBytes );

				foreach ( int result in results ) {
					for ( int i = 0; i < searchBytes.Length; ++i ) {
						data[result + i] = 0x20;
					}
				}
			}
		}

		class KnownGamedata {
			public string Gamecode;
			public uint Position;
			public uint Length;

			public KnownGamedata( string gamecode, uint position, uint length ) {
				this.Gamecode = gamecode;
				this.Position = position;
				this.Length = length;
			}
		}

		static bool RemoveStringsInKnownGames( string gamecode, byte[] data ) {
			KnownGamedata[] knownData = new KnownGamedata[] {
				new KnownGamedata( "VI2J", 0x1047C8, 0x4A4 ), // FE12 English Patch
			};

			bool knownGame = false;
			foreach ( KnownGamedata d in knownData ) {
				if ( gamecode == d.Gamecode ) {
					knownGame = true;
					for ( uint i = d.Position; i < d.Position + d.Length; ++i ) {
						if ( data[i] != 0 ) {
							data[i] = 0x20;
						}
					}
				}
			}

			return knownGame;
		}

		static bool ReplaceInData( byte[] data, byte[] replaceBytes) {
			return ReplaceInData( data, EVILCHECK_MODULUS, replaceBytes);
		}

		static bool ReplaceInData( byte[] data, byte[] searchBytes, byte[] replaceBytes) {
			bool replacedData = false;

			var results = data.Locate( searchBytes );
			if ( results.Length == 0 ) {
				return false;
			}

			foreach ( int result in results ) {
				Array.Copy(replaceBytes, 0, data, result, replaceBytes.Length);
				replacedData = true;
			}

			return replacedData;
		}
	}
}
