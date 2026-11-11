using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WaiuBenchmark
{
    internal sealed class ReferenceTargets
    {
        internal const string FileName = "pc-benchmark-reference-v2.json";
        internal const string Workload = "waiu-windows-workloads-v2";
        private const int MaxBytes = 8192;
        private static readonly string[] Metrics = { "graphicsFps", "cpuSingle", "cpuParallel", "ramCopy",
            "randomReads", "storageWrite", "ramEverydayGiB", "ramWorkstationGiB" };
        private static readonly int[] BandStarts = { 0, 40, 60, 80, 95, 100 };
        internal static readonly ReferenceTargets Bundled = ReadBundled();
        internal readonly int Revision;
        internal readonly string Date;
        internal readonly string Source;
        private readonly Dictionary<string, int[]> scales;
        private readonly Dictionary<string, string> cpuNotes, gpuNotes;
        internal int GraphicsGoal { get { return scales["graphicsFps"][1]; } }

        private ReferenceTargets(int revision, string date, Dictionary<string, int[]> scales, string source,
            Dictionary<string, string> cpuNotes, Dictionary<string, string> gpuNotes)
        {
            Revision = revision;
            Date = date;
            this.scales = scales;
            Source = source;
            this.cpuNotes = cpuNotes;
            this.gpuNotes = gpuNotes;
        }

        internal int GraphicsScore(double slowFrameMs)
        {
            return Score("graphicsFps", slowFrameMs > 0 && !double.IsInfinity(slowFrameMs) ? Math.Round(1000.0 / slowFrameMs,9) : 0);
        }

        internal int Score(string metric, double value)
        {
            if (value <= 0 || double.IsNaN(value) || double.IsInfinity(value)) return -1;
            int[] thresholds = scales[metric];
            int band = 0;
            while (band < thresholds.Length && value >= thresholds[band]) band++;
            double lower = band == 0 ? 0 : thresholds[band - 1];
            double upper = band == thresholds.Length ? lower * 1.25 : thresholds[band];
            double fraction = Math.Min(1, (value - lower) / (upper - lower));
            return BandStarts[band] + (int)((BandStarts[band + 1] - BandStarts[band]) * fraction);
        }

        internal static int Grade(int score)
        {
            if (score < 0) return -1;
            int band = 0;
            while (band < 4 && score >= BandStarts[band + 1]) band++;
            return band;
        }

        internal string Scale(string metric)
        {
            return string.Join(" / ", Array.ConvertAll(scales[metric], value => value.ToString("N0",CultureInfo.InvariantCulture)));
        }

        internal string CpuNote(string model)
        {
            foreach (string token in model.Split(' '))
            {
                string note;
                if (cpuNotes.TryGetValue(token,out note)) return note;
            }
            return null;
        }

        internal string GpuNote(string model)
        {
            string note;
            return gpuNotes.TryGetValue(model.Trim(),out note) ? note : null;
        }

        private static ReferenceTargets ReadBundled()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ReferenceV2.json"))
            using (var reader = new StreamReader(stream))
            {
                var bundled = Parse(reader.ReadToEnd(), "Bundled");
                if (bundled == null) throw new InvalidDataException("The bundled benchmark reference is invalid.");
                return bundled;
            }
        }

        internal static ReferenceTargets Parse(string json, string source)
        {
            if (string.IsNullOrEmpty(json) || json.Length > MaxBytes) return null;
            try
            {
                var parser = new JavaScriptSerializer { MaxJsonLength = MaxBytes, RecursionLimit = 8 };
                var fields = parser.DeserializeObject(json) as Dictionary<string, object>;
                if (fields == null || fields.Count != Metrics.Length + 5 || !fields.ContainsKey("workload") ||
                    !fields.ContainsKey("revision") || !fields.ContainsKey("date") ||
                    !fields.ContainsKey("cpuNotes") || !fields.ContainsKey("gpuNotes")) return null;
                var date = fields["date"] as string;
                DateTime parsed;
                if (fields["workload"] as string != Workload || !(fields["revision"] is int) ||
                    (int)fields["revision"] < 1 ||
                    !DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) return null;
                var parsedScales = new Dictionary<string, int[]>();
                foreach (string metric in Metrics)
                {
                    object field;
                    if (!fields.TryGetValue(metric,out field)) return null;
                    var values = field as object[];
                    if (values == null || values.Length != 4) return null;
                    var thresholds = new int[4];
                    for (int i = 0; i < thresholds.Length; i++)
                    {
                        if (!(values[i] is int)) return null;
                        thresholds[i] = (int)values[i];
                        if (thresholds[i] < 1 || thresholds[i] > (metric == "graphicsFps" ? 360 : 1000000) ||
                            (i > 0 && thresholds[i] <= thresholds[i - 1])) return null;
                    }
                    parsedScales.Add(metric, thresholds);
                }
                var cpuNotes = ParseNotes(fields["cpuNotes"]);
                var gpuNotes = ParseNotes(fields["gpuNotes"]);
                if (cpuNotes == null || gpuNotes == null) return null;
                return new ReferenceTargets((int)fields["revision"], date, parsedScales, source, cpuNotes, gpuNotes);
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private static Dictionary<string, string> ParseNotes(object value)
        {
            var entries = value as Dictionary<string, object>;
            if (entries == null || entries.Count > 40) return null;
            var notes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                string note = entry.Value as string;
                if (entry.Key.Length == 0 || entry.Key.Length > 100 || string.IsNullOrWhiteSpace(note) ||
                    note.Length > 240 || notes.ContainsKey(entry.Key)) return null;
                notes.Add(entry.Key,note);
            }
            return notes;
        }

        internal static async Task<ReferenceTargets> LoadAsync(Uri endpoint, string cacheFile, CancellationToken cancel)
        {
            if (endpoint == null) return Bundled;
            ReferenceTargets best = Bundled;
            try
            {
                var cache = new FileInfo(cacheFile);
                if (cache.Exists && cache.Length <= MaxBytes)
                {
                    var saved = Parse(File.ReadAllText(cacheFile), "Cached");
                    if (saved != null && saved.Revision >= best.Revision &&
                        (saved.Revision > best.Revision || SameTargets(saved, best)))
                    {
                        best = saved;
                        var age = DateTime.UtcNow - cache.LastWriteTimeUtc;
                        if (age >= TimeSpan.Zero && age < TimeSpan.FromHours(24)) return best;
                    }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            try
            {
                using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = !endpoint.IsLoopback })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(4), MaxResponseContentBufferSize = MaxBytes })
                using (var response = await client.GetAsync(endpoint, cancel).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType == null ||
                        !string.Equals(response.Content.Headers.ContentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase)) return best;
                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var updated = Parse(json, "Downloaded");
                    if (updated == null || updated.Revision < best.Revision ||
                        (updated.Revision == best.Revision && !SameTargets(updated, best))) return best;
                    SaveCache(cacheFile, json);
                    return updated;
                }
            }
            catch (HttpRequestException) { return best; }
            catch (OperationCanceledException) { return best; }
        }

        private static bool SameTargets(ReferenceTargets first, ReferenceTargets second)
        {
            if (first.Date != second.Date) return false;
            foreach (string metric in Metrics)
                for (int i = 0; i < first.scales[metric].Length; i++)
                    if (first.scales[metric][i] != second.scales[metric][i]) return false;
            return SameNotes(first.cpuNotes,second.cpuNotes) && SameNotes(first.gpuNotes,second.gpuNotes);
        }

        private static bool SameNotes(Dictionary<string, string> first, Dictionary<string, string> second)
        {
            if (first.Count != second.Count) return false;
            foreach (var entry in first)
            {
                string value;
                if (!second.TryGetValue(entry.Key,out value) || value != entry.Value) return false;
            }
            return true;
        }

        private static void SaveCache(string path, string json)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
