using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Reloaded.Mod.Interfaces;
using System.IO;
using System.Text.Json;
using System.Reflection;
using Reloaded.Memory.Sources;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Memory.Sigscan.Definitions;

namespace p4gpc.heeheeho
{
    public class heePatcher : IDisposable
    {
        private Dictionary<string, byte[]> spEncoding;
        private readonly ILogger mLogger;
        private readonly IMemory mMem;
        private readonly IScannerFactory mFactory;

        private readonly Process mProc;
        private readonly IntPtr mBaseAddr;

        public heePatcher(ILogger logger, IScannerFactory factory)
        {
            mLogger = logger;
            mProc = Process.GetCurrentProcess();
            mFactory = factory;
            mBaseAddr = mProc.MainModule.BaseAddress;
            mMem = new Memory();
        }

        private byte[] newEncode(string name)
        {
            byte[] newEncoding = new byte[0];
            foreach (char glyph in name)
            {
                byte[] byteGlyph = Encoding.UTF8.GetBytes(Char.ToString(glyph));
                string key = BitConverter.ToString(byteGlyph).Replace("-", "");
                if (spEncoding.ContainsKey(key))
                {
                    byteGlyph = spEncoding[key];
                }
                byte[] newArray = new byte[newEncoding.Length + byteGlyph.Length];
                newEncoding.CopyTo(newArray, 0);
                byteGlyph.CopyTo(newArray, newEncoding.Length);
                newEncoding = newArray;
            }
            return newEncoding;
        }

        private void createTable()
        {
            spEncoding = new Dictionary<string, byte[]>();
            // 1 before the row where special characters start
            string stringHex = "80CF";
            int intFromHex;
        
            // Read embedded tsv file
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "p4gpc.heeheeho.Properties.P4.tsv";

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var charStrings = line.Split('\t');
                        foreach (string charString in charStrings)
                        {
                            intFromHex = int.Parse(stringHex, System.Globalization.NumberStyles.HexNumber);
                            // Go from FF -> 80 otherwise increment
                            if (stringHex.Substring(stringHex.Length - 2) == "FF")
                                intFromHex += 129;
                            else
                                intFromHex += 1;
                            stringHex = intFromHex.ToString("X");
                            // Convert hex string to bytes
                            int NumberChars = stringHex.Length;
                            byte[] bytes = new byte[NumberChars / 2];
                            for (int j = 0; j < NumberChars; j += 2)
                                bytes[j / 2] = Convert.ToByte(stringHex.Substring(j, 2), 16);
                            string charBytes = BitConverter.ToString(Encoding.UTF8.GetBytes(charString)).Replace("-", "");
                            if (charString != "" && !spEncoding.ContainsKey(charBytes))
                                spEncoding.Add(charBytes, bytes);
                        }
                    }
                }
            }
        }

        private unsafe void overwrite(SongObject song)
        {
            if (song.newName == null)
                return;
            string newName = shortenName(song.newName);    
            byte[] hexOriginalName = Encoding.ASCII.GetBytes(song.originalName);
            byte[] hexNewName = newEncode(newName);

            // Add null byte to make I'll Face Myself unique
            if (song.originalName.Equals("I'll Face Myself"))
            {
                byte[] tempArray = new byte[hexOriginalName.Length + 1];
                Array.Copy(hexOriginalName, tempArray, hexOriginalName.Length);
                hexOriginalName = tempArray;
            }

            if (hexOriginalName.Length > hexNewName.Length)
            {
                byte[] tempArray = new byte[hexOriginalName.Length];
                Array.Copy(hexNewName, tempArray, hexNewName.Length);
                hexNewName = tempArray;
            }

            var songListOffset = mBaseAddr + songsOffset;
            var scanner = mFactory.CreateScanner((byte*)songListOffset, mProc.MainModule.ModuleMemorySize - songsOffset);
            string songBytePattern = BitConverter.ToString(hexOriginalName).Replace("-", " ");
            var result = scanner.FindPattern(songBytePattern);
            if (result.Found)
            {
                mMem.SafeWriteRaw(mBaseAddr + songsOffset + result.Offset, hexNewName);
                mLogger.WriteLine("[HeeHeeHo Music Renamer] Renamed \"" + song.originalName + "\" to \"" + newName + "\"");
            }
            else
                mLogger.WriteLine("[HeeHeeHo Music Renamer] Couldn't find song name \"" + song.originalName + "\"");
        }

        private string shortenName(string name)
        {
            if (name.Length >= 47)
            {
                var lastSpaceIndex = name[0..44].LastIndexOf(' ');
                var cutoffName = name.Substring(0, lastSpaceIndex);
                return (cutoffName + "...");
            }
            else
                return name;
        }


        private SongObject[] songs;
        public class SongObject
        {
            public string originalName { get; set; }
            public string newName { get; set; }
        }

        private int songsOffset;
        public void Patch()
        {
            string file = $"{Directory.GetCurrentDirectory()}/mods/SND/HeeHeeHo.uwus";
            if (!File.Exists(file))
            {
                mLogger.WriteLine($"[HeeHeeHo Music Renamer] {file} doesn't exist.");
                return;
            }

            try
            {
                songs = JsonSerializer.Deserialize<SongObject[]>(File.ReadAllText(file));
            }
            catch (JsonException ex)
            {
                mLogger.WriteLine("[HeeHeeHo Music Renamer] Invalid HeeHeeHo.uwus");
                return;
            }

            string startBytePattern = BitConverter.ToString(Encoding.ASCII.GetBytes("Blank")).Replace("-", " ") + " 00";
            mLogger.WriteLine($"[HeeHeeHo Music Renamer] Searching for location of songs using: {startBytePattern}");
            var scanner = mFactory.CreateScanner(mProc);
            var result = scanner.FindPattern(startBytePattern);
            if (result.Found)
            {
                songsOffset = result.Offset;
                mLogger.WriteLine("[HeeHeeHo Music Renamer] Found location of songs");
            }
            else
            {
                mLogger.WriteLine("[HeeHeeHo Music Renamer] Couldn't find location of songs...");
                return;
            }

            createTable();
            foreach (SongObject song in songs)
                overwrite(song);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            mProc?.Dispose();
        }
    }
}