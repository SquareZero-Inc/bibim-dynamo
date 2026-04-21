// Copyright (c) 2026 SquareZero Inc. — Licensed under Apache 2.0. See LICENSE in the repo root.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace BIBIM_MVP
{
    /// <summary>
    /// Local BM25-based RAG for Dynamo — mirrors LocalRevitRagService in BIBIM_REVIT.
    ///
    /// Design:
    ///   • Indexes RevitAPI.xml (+ RevitAPIUI.xml, RevitAPIIFC.xml) from the Revit installation.
    ///   • Lazy-load: index is built on the first call, then cached for the process lifetime.
    ///   • Returns apiDocContext string to be injected into Claude's system prompt via
    ///     ClaudeApiClient.CallClaudeApiAsync(apiDocContext: ...).
    ///   • No external API required — works with Claude API key only.
    ///
    /// Debug logging:
    ///   All RAG events logged via Logger.Log("LocalRAG", ...) → bibim_debug.txt.
    ///   Check for [INDEX_BUILD_DONE], [HIT], [MISS] lines to verify RAG is working.
    /// </summary>
    internal static class LocalDynamoRagService
    {
        private static BM25Engine _engine;
        private static string _indexedXmlDir;
        private static readonly object _lock = new object();

        private static readonly string[] XmlFileNames =
        {
            "RevitAPI.xml",
            "RevitAPIUI.xml",
            "RevitAPIIFC.xml"
        };

        private const int TopK = 5;
        private const int MaxChunkDisplayChars = 3000;

        /// <summary>
        /// Fetch relevant Revit API documentation for the query.
        /// Returns a context string ready to pass to CallClaudeApiAsync as apiDocContext.
        /// Returns empty string if index unavailable or no match found.
        /// </summary>
        public static Task<string> FetchContextAsync(
            string query,
            string revitVersion,
            CancellationToken ct = default)
        {
            return Task.Run(() => FetchContextInternal(query, revitVersion), ct);
        }

        private static string FetchContextInternal(string query, string revitVersion)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var engine = GetOrBuildEngine(revitVersion);
                if (engine == null)
                {
                    Logger.Log("LocalRAG", "[MISS] engine=null (RevitAPI.xml not found)");
                    return "";
                }

                var hits = engine.Search(query, TopK);
                sw.Stop();

                if (hits.Count == 0)
                {
                    Logger.Log("LocalRAG",
                        $"[MISS] query=\"{Clip(query)}\" ms={sw.ElapsedMilliseconds}");
                    return "";
                }

                var sb = new StringBuilder();
                for (int i = 0; i < hits.Count; i++)
                {
                    var chunk = hits[i];
                    sb.AppendLine($"--- [{i + 1}] {chunk.Namespace}.{chunk.ClassName} ---");
                    sb.AppendLine(chunk.DisplayText);
                    sb.AppendLine();
                }

                Logger.Log("LocalRAG",
                    $"[HIT] query=\"{Clip(query)}\" hits={hits.Count} " +
                    $"top=\"{hits[0].ClassName}\" ms={sw.ElapsedMilliseconds} " +
                    $"preview=\"{Clip(hits[0].DisplayText, 120)}\"");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.LogError("LocalRAG.FetchContextInternal", ex);
                return "";
            }
        }

        private static BM25Engine GetOrBuildEngine(string revitVersion)
        {
            lock (_lock)
            {
                string xmlDir = ResolveRevitXmlDirectory();
                if (string.IsNullOrEmpty(xmlDir))
                {
                    Logger.Log("LocalRAG", "[INDEX] RevitAPI.xml directory not resolved");
                    return null;
                }

                if (_engine != null && string.Equals(_indexedXmlDir, xmlDir, StringComparison.OrdinalIgnoreCase))
                    return _engine;

                Logger.Log("LocalRAG", $"[INDEX_BUILD_START] dir=\"{xmlDir}\" revit={revitVersion}");
                var buildSw = Stopwatch.StartNew();

                var chunks = BuildChunks(xmlDir);
                buildSw.Stop();

                if (chunks.Count == 0)
                {
                    Logger.Log("LocalRAG", $"[INDEX_BUILD_FAILED] no chunks produced ms={buildSw.ElapsedMilliseconds}");
                    return null;
                }

                _engine = new BM25Engine(chunks);
                _indexedXmlDir = xmlDir;

                Logger.Log("LocalRAG",
                    $"[INDEX_BUILD_DONE] chunks={chunks.Count} " +
                    $"ms={buildSw.ElapsedMilliseconds} dir=\"{xmlDir}\"");

                return _engine;
            }
        }

        private static List<RagChunk> BuildChunks(string xmlDir)
        {
            var allMembers = new List<XElement>();
            foreach (string fileName in XmlFileNames)
            {
                string path = Path.Combine(xmlDir, fileName);
                if (!File.Exists(path)) continue;
                try
                {
                    var doc = XDocument.Load(path);
                    allMembers.AddRange(doc.Descendants("member"));
                    Logger.Log("LocalRAG", $"[XML_LOADED] file={fileName} total_so_far={allMembers.Count}");
                }
                catch (Exception ex)
                {
                    Logger.Log("LocalRAG", $"[XML_LOAD_ERROR] file={fileName} err={ex.Message}");
                }
            }

            if (allMembers.Count == 0) return new List<RagChunk>();

            var byClass = new Dictionary<string, ClassAccumulator>(StringComparer.OrdinalIgnoreCase);

            foreach (var member in allMembers)
            {
                string nameAttr = member.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(nameAttr)) continue;

                if (!TryParseRevitMember(nameAttr, out string ns, out string className, out string memberName))
                    continue;

                string key = ns + "." + className;
                if (!byClass.TryGetValue(key, out var acc))
                {
                    acc = new ClassAccumulator { Namespace = ns, ClassName = className };
                    byClass[key] = acc;
                }

                string summary = NormalizeSummary(member.Element("summary")?.Value);
                string remarks = NormalizeSummary(member.Element("remarks")?.Value);

                if (string.IsNullOrEmpty(memberName))
                {
                    acc.ClassSummary = summary;
                    if (!string.IsNullOrEmpty(remarks)) acc.ClassRemarks = remarks;
                }
                else
                {
                    var paramDescs = new List<string>();
                    foreach (var p in member.Elements("param"))
                    {
                        string pname = p.Attribute("name")?.Value;
                        string pdesc = NormalizeSummary(p.Value);
                        if (!string.IsNullOrEmpty(pname) && !string.IsNullOrEmpty(pdesc))
                            paramDescs.Add($"  {pname}: {pdesc}");
                    }
                    acc.Members.Add(new MemberEntry
                    {
                        MemberName = memberName,
                        Summary = summary,
                        Remarks = remarks,
                        Returns = NormalizeSummary(member.Element("returns")?.Value),
                        ParamDescriptions = paramDescs,
                        MemberTypePrefix = nameAttr[0]
                    });
                }
            }

            var chunks = new List<RagChunk>(byClass.Count);
            foreach (var kv in byClass.Values)
            {
                if (kv.ClassName == "BuiltInParameter" && kv.Members.Count > 200)
                {
                    chunks.AddRange(SplitBuiltInParameterChunks(kv));
                    continue;
                }
                var chunk = AccumulatorToChunk(kv);
                if (chunk != null) chunks.Add(chunk);
            }
            return chunks;
        }

        private static RagChunk AccumulatorToChunk(ClassAccumulator acc)
        {
            if (acc.Members.Count == 0 && string.IsNullOrEmpty(acc.ClassSummary)) return null;

            var sb = new StringBuilder();
            sb.AppendLine($"[Class: {acc.ClassName}]");
            sb.AppendLine($"Namespace: {acc.Namespace}");
            if (!string.IsNullOrEmpty(acc.ClassSummary)) sb.AppendLine($"Summary: {acc.ClassSummary}");
            if (!string.IsNullOrEmpty(acc.ClassRemarks)) sb.AppendLine($"Remarks: {acc.ClassRemarks}");

            if (acc.Members.Count > 0)
            {
                sb.AppendLine();
                foreach (var m in acc.Members.Take(60))
                {
                    string prefix = m.MemberTypePrefix == 'P' ? "[Property] " :
                                    m.MemberTypePrefix == 'F' ? "[Field] " :
                                    m.MemberTypePrefix == 'E' ? "[Event] " : "";
                    sb.Append($"{prefix}{m.MemberName}");
                    if (!string.IsNullOrEmpty(m.Returns)) sb.Append($" -> {m.Returns}");
                    sb.AppendLine();
                    if (!string.IsNullOrEmpty(m.Summary)) sb.AppendLine($"  {m.Summary}");
                    foreach (var pd in m.ParamDescriptions) sb.AppendLine(pd);
                    if (!string.IsNullOrEmpty(m.Remarks)) sb.AppendLine($"  Note: {m.Remarks}");
                }
            }

            string displayText = sb.ToString();
            if (displayText.Length > MaxChunkDisplayChars)
                displayText = displayText.Substring(0, MaxChunkDisplayChars) + "\n[...truncated]";

            return new RagChunk
            {
                ClassName = acc.ClassName,
                Namespace = acc.Namespace,
                DisplayText = displayText,
                IndexText = displayText + " " + acc.ClassName + " " + acc.Namespace
            };
        }

        private static IEnumerable<RagChunk> SplitBuiltInParameterChunks(ClassAccumulator acc)
        {
            var domainGroups = new Dictionary<string, List<MemberEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in acc.Members)
            {
                string domain = ExtractDomain(m.MemberName);
                if (!domainGroups.TryGetValue(domain, out var list)) { list = new List<MemberEntry>(); domainGroups[domain] = list; }
                list.Add(m);
            }
            foreach (var kv in domainGroups)
            {
                var sub = new ClassAccumulator
                {
                    Namespace = acc.Namespace,
                    ClassName = $"BuiltInParameter.{kv.Key}",
                    ClassSummary = $"BuiltInParameter enum values for domain: {kv.Key}",
                    Members = kv.Value
                };
                var chunk = AccumulatorToChunk(sub);
                if (chunk != null) yield return chunk;
            }
        }

        private static string ExtractDomain(string name)
        {
            if (string.IsNullOrEmpty(name)) return "OTHER";
            int idx = name.IndexOf('_');
            return idx > 0 ? name.Substring(0, idx) : name;
        }

        private static bool TryParseRevitMember(string nameAttr, out string ns, out string className, out string memberName)
        {
            ns = ""; className = ""; memberName = "";
            if (nameAttr.Length < 3) return false;

            string fullName = nameAttr.Length > 2 && nameAttr[1] == ':' ? nameAttr.Substring(2) : nameAttr;
            int parenIdx = fullName.IndexOf('(');
            if (parenIdx >= 0) fullName = fullName.Substring(0, parenIdx);

            const string prefix = "Autodesk.Revit.";
            int prefixIdx = fullName.IndexOf(prefix, StringComparison.Ordinal);
            if (prefixIdx < 0) return false;

            string[] parts = fullName.Substring(prefixIdx).Split('.');
            if (parts.Length < 4) return false;

            var knownSubNs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "DB", "UI", "Creation", "Structure", "Mechanical", "Plumbing",
                "Electrical", "Architecture", "Analysis", "IFC", "Macros",
                "Visual", "ApplicationServices", "Parameters", "Exceptions"
            };

            int classIdx = -1;
            for (int i = 2; i < parts.Length; i++)
            {
                if (!knownSubNs.Contains(parts[i])) { classIdx = i; break; }
            }
            if (classIdx < 0) return false;

            ns = string.Join(".", parts, 0, classIdx);
            className = parts[classIdx];
            memberName = classIdx + 1 < parts.Length ? parts[classIdx + 1] : "";
            if (memberName.StartsWith("get_", StringComparison.Ordinal)) memberName = memberName.Substring(4);
            else if (memberName.StartsWith("set_", StringComparison.Ordinal)) memberName = memberName.Substring(4);

            return !string.IsNullOrWhiteSpace(className);
        }

        private static string ResolveRevitXmlDirectory()
        {
            try
            {
                var revitApiAssembly = AppDomain.CurrentDomain
                    .GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "RevitAPI", StringComparison.OrdinalIgnoreCase));

                if (revitApiAssembly != null)
                {
                    string dir = Path.GetDirectoryName(revitApiAssembly.Location);
                    if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "RevitAPI.xml")))
                        return dir;
                }

                string[] years = { "2026", "2025", "2024", "2027", "2023", "2022" };
                foreach (string year in years)
                {
                    string candidate = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                        "Autodesk", $"Revit {year}");
                    if (File.Exists(Path.Combine(candidate, "RevitAPI.xml")))
                        return candidate;
                }
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log("LocalRAG", $"[RESOLVE_ERROR] {ex.Message}");
                return null;
            }
        }

        private static string NormalizeSummary(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string t = raw.Replace("\r", " ").Replace("\n", " ").Trim();
            while (t.Contains("  ")) t = t.Replace("  ", " ");
            return t;
        }

        private static string Clip(string text, int maxLen = 80)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";
        }

        private class ClassAccumulator
        {
            public string Namespace;
            public string ClassName;
            public string ClassSummary;
            public string ClassRemarks;
            public List<MemberEntry> Members = new List<MemberEntry>();
        }

        private class MemberEntry
        {
            public string MemberName;
            public string Summary;
            public string Remarks;
            public string Returns;
            public List<string> ParamDescriptions = new List<string>();
            public char MemberTypePrefix;
        }
    }
}
