using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameProtection.Editor
{
    /// <summary>
    /// Evidence tool. Scans a folder (a suspected stolen copy, ideally extracted with AssetRipper or AssetStudio)
    /// for your ownership token and your build IDs, and writes a dated report you can attach to a takedown.
    /// </summary>
    public static class SuspiciousBuildScanner
    {
        const int ChunkSize = 4 * 1024 * 1024;
        const string LedgerFile = "GameProtectionBuildLedger.csv";

        public sealed class Match
        {
            public string File;
            public string Marker;
            public long Offset;
        }

        [MenuItem("Tools/Game Protection/Scan Suspicious Build...", priority = 10)]
        static void ScanFromMenu()
        {
            var guard = ReleaseGuardSettings.Find();
            if (guard == null || string.IsNullOrWhiteSpace(guard.ownershipToken))
            {
                EditorUtility.DisplayDialog("Game Protection", "Run Tools > Game Protection > Setup first: there is no ownership token yet.", "OK");
                return;
            }

            var folder = EditorUtility.OpenFolderPanel("Folder with the suspected copy (extracted)", "", "");
            if (string.IsNullOrEmpty(folder)) return;

            var markers = Markers(guard);
            var matches = Scan(folder, markers, (file, progress) =>
                EditorUtility.DisplayCancelableProgressBar("Scanning suspected copy", file, progress));
            EditorUtility.ClearProgressBar();

            var reportPath = WriteReport(folder, guard, markers, matches);

            if (matches.Count > 0)
            {
                EditorUtility.DisplayDialog("Game Protection",
                    $"Found {matches.Count} match(es) of your ownership markers.\n\nReport saved to:\n{reportPath}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Game Protection",
                    "No markers found.\n\nIf the copy is packed (APK, AAB, LZ4-compressed data or bundles), extract it first with " +
                    "AssetRipper or AssetStudio and scan the extracted folder.\n\nReport saved to:\n" + reportPath, "OK");
            }
            EditorUtility.RevealInFinder(reportPath);
        }

        /// <summary>The ownership token, plus every build ID from your ledger.</summary>
        public static Dictionary<string, string> Markers(ReleaseGuardSettings guard)
        {
            var markers = new Dictionary<string, string> { [guard.ownershipToken] = "ownership token" };
            if (!File.Exists(LedgerFile)) return markers;

            foreach (var line in File.ReadAllLines(LedgerFile).Skip(1))
            {
                var cols = line.Split(',');
                if (cols.Length > 2 && cols[1].Length >= 8 && !markers.ContainsKey(cols[1]))
                    markers[cols[1]] = $"build {cols[1]} ({cols[2]}, {cols[0]})";
            }
            return markers;
        }

        /// <summary>Searches every file for each marker as UTF-8 and UTF-16 bytes. Returns false from onProgress to cancel.</summary>
        public static List<Match> Scan(string folder, IReadOnlyDictionary<string, string> markers, Func<string, float, bool> onProgress = null)
        {
            var patterns = markers.Keys
                .SelectMany(m => new[]
                {
                    new KeyValuePair<string, byte[]>(m, Encoding.UTF8.GetBytes(m)),
                    new KeyValuePair<string, byte[]>(m, Encoding.Unicode.GetBytes(m)),
                })
                .ToArray();
            var overlap = patterns.Max(p => p.Value.Length) - 1;

            var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            var matches = new List<Match>();
            var buffer = new byte[ChunkSize + overlap];

            for (var i = 0; i < files.Length; i++)
            {
                if (onProgress != null && onProgress(Path.GetFileName(files[i]), (float)i / files.Length)) break;

                try { ScanFile(files[i], patterns, buffer, overlap, matches); }
                catch (IOException) { /* locked or unreadable: skip */ }
                catch (UnauthorizedAccessException) { }
            }
            return matches;
        }

        static void ScanFile(string path, KeyValuePair<string, byte[]>[] patterns, byte[] buffer, int overlap, List<Match> matches)
        {
            var found = new HashSet<string>();
            using (var stream = File.OpenRead(path))
            {
                long chunkStart = 0;
                var carried = 0;

                while (true)
                {
                    var read = stream.Read(buffer, carried, buffer.Length - carried);
                    var length = carried + read;
                    if (length == 0) break;

                    var window = new ReadOnlySpan<byte>(buffer, 0, length);
                    foreach (var pattern in patterns)
                    {
                        if (found.Contains(pattern.Key)) continue;
                        var index = window.IndexOf(new ReadOnlySpan<byte>(pattern.Value));
                        if (index < 0) continue;

                        found.Add(pattern.Key);
                        matches.Add(new Match { File = path, Marker = pattern.Key, Offset = chunkStart + index });
                    }

                    if (read == 0) break;

                    // Keep the tail so markers spanning two chunks are still found.
                    carried = Math.Min(overlap, length);
                    Buffer.BlockCopy(buffer, length - carried, buffer, 0, carried);
                    chunkStart += length - carried;
                }
            }
        }

        static string WriteReport(string folder, ReleaseGuardSettings guard, IReadOnlyDictionary<string, string> markers, List<Match> matches)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Game Protection Kit - suspicious build scan");
            sb.AppendLine($"Scanned (UTC):   {DateTime.UtcNow:O}");
            sb.AppendLine($"Folder:          {folder}");
            sb.AppendLine($"Owner statement: {guard.ownershipStatement}");
            sb.AppendLine($"Markers checked: {markers.Count}");
            sb.AppendLine();
            sb.AppendLine(matches.Count == 0 ? "No markers found." : $"{matches.Count} match(es):");
            foreach (var m in matches)
                sb.AppendLine($"- {markers[m.Marker]} in {m.File} at byte offset {m.Offset}");

            Directory.CreateDirectory("GameProtectionEvidence");
            var path = Path.GetFullPath(Path.Combine("GameProtectionEvidence", $"scan-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt"));
            File.WriteAllText(path, sb.ToString());
            return path;
        }
    }
}
