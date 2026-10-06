using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lichen.Core
{
    public sealed class GraphMapperGripState
    {
        public int Index { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string Constraint { get; set; }
    }

    public sealed class GraphMapperSampleState
    {
        public double NormalizedInput { get; set; }
        public double NormalizedOutput { get; set; }
        public double MappedInput { get; set; }
        public double MappedOutput { get; set; }
    }

    public sealed class GraphMapperStateCapture
    {
        public GraphMapperStateCapture()
        {
            CaptureStatus = "unavailable";
            CaptureNote = "";
            GraphType = "";
            GraphTypeId = "";
            Grips = new List<GraphMapperGripState>();
            Samples = new List<GraphMapperSampleState>();
        }

        public string CaptureStatus { get; set; }
        public string CaptureNote { get; set; }
        public string GraphType { get; set; }
        public string GraphTypeId { get; set; }
        public bool GraphValid { get; set; }
        public bool LockGrips { get; set; }
        public double InputDomainStart { get; set; }
        public double InputDomainEnd { get; set; }
        public double OutputDomainStart { get; set; }
        public double OutputDomainEnd { get; set; }
        public int TotalGripCount { get; set; }
        public int TotalSampleCount { get; set; }
        public List<GraphMapperGripState> Grips { get; set; }
        public List<GraphMapperSampleState> Samples { get; set; }
    }

    public static class GraphMapperStateProjection
    {
        public const int MaximumGrips = 64;
        public const int MaximumSamples = 33;
        public const int DefaultSampleCount = 17;

        public static List<ContextMetadataEntry> Metadata(GraphMapperStateCapture capture)
        {
            GraphMapperStateCapture value = capture ?? Unavailable("Graph Mapper authored state was not captured.");
            string requestedStatus = (value.CaptureStatus ?? "").Trim().ToLowerInvariant();
            if ((requestedStatus == "captured" || requestedStatus == "partial") && !FiniteDomains(value))
                value = Unavailable("Graph Mapper authored domains were unavailable or non-finite.");
            List<ContextMetadataEntry> metadata = new List<ContextMetadataEntry>();
            string status = String.IsNullOrWhiteSpace(value.CaptureStatus) ? "unavailable" : value.CaptureStatus.Trim().ToLowerInvariant();
            Add(metadata, "graphMapper.captureStatus", status);
            if (!String.IsNullOrWhiteSpace(value.CaptureNote)) Add(metadata, "graphMapper.captureNote", OneLine(value.CaptureNote));
            if (status != "captured" && status != "partial") return metadata;

            Add(metadata, "graphMapper.graphType", OneLine(value.GraphType));
            Add(metadata, "graphMapper.graphTypeId", (value.GraphTypeId ?? "").ToLowerInvariant());
            Add(metadata, "graphMapper.graphValid", value.GraphValid.ToString());
            Add(metadata, "graphMapper.inputDomain", Pair(value.InputDomainStart, value.InputDomainEnd));
            Add(metadata, "graphMapper.outputDomain", Pair(value.OutputDomainStart, value.OutputDomainEnd));
            Add(metadata, "graphMapper.lockGrips", value.LockGrips.ToString());

            List<GraphMapperGripState> grips = (value.Grips ?? new List<GraphMapperGripState>())
                .Where(Finite).OrderBy(grip => grip.Index).ThenBy(grip => grip.X).ThenBy(grip => grip.Y).Take(MaximumGrips).ToList();
            int totalGrips = Math.Max(value.TotalGripCount, value.Grips == null ? 0 : value.Grips.Count);
            Add(metadata, "graphMapper.gripCount", totalGrips.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < grips.Count; i++)
                Add(metadata, "graphMapper.grip." + i.ToString("D3", CultureInfo.InvariantCulture),
                    "index=" + grips[i].Index.ToString(CultureInfo.InvariantCulture) + "; x=" + Number(grips[i].X)
                    + "; y=" + Number(grips[i].Y) + "; constraint=" + OneLine(grips[i].Constraint));
            if (totalGrips > grips.Count) Add(metadata, "graphMapper.omittedGripCount", (totalGrips - grips.Count).ToString(CultureInfo.InvariantCulture));

            List<GraphMapperSampleState> samples = (value.Samples ?? new List<GraphMapperSampleState>())
                .Where(Finite).OrderBy(sample => sample.NormalizedInput).Take(MaximumSamples).ToList();
            int totalSamples = Math.Max(value.TotalSampleCount, value.Samples == null ? 0 : value.Samples.Count);
            Add(metadata, "graphMapper.sampleCount", totalSamples.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < samples.Count; i++)
                Add(metadata, "graphMapper.sample." + i.ToString("D3", CultureInfo.InvariantCulture),
                    "normalizedInput=" + Number(samples[i].NormalizedInput) + "; normalizedOutput=" + Number(samples[i].NormalizedOutput)
                    + "; mappedInput=" + Number(samples[i].MappedInput) + "; mappedOutput=" + Number(samples[i].MappedOutput));
            if (totalSamples > samples.Count) Add(metadata, "graphMapper.omittedSampleCount", (totalSamples - samples.Count).ToString(CultureInfo.InvariantCulture));
            return metadata;
        }

        public static string ReadableSummary(GraphMapperStateCapture capture)
        {
            if (capture == null || (!String.Equals(capture.CaptureStatus, "captured", StringComparison.OrdinalIgnoreCase)
                && !String.Equals(capture.CaptureStatus, "partial", StringComparison.OrdinalIgnoreCase)) || !FiniteDomains(capture))
                return "authored graph state unavailable" + (capture == null || String.IsNullOrWhiteSpace(capture.CaptureNote) ? "" : ": " + OneLine(capture.CaptureNote));

            List<GraphMapperGripState> grips = (capture.Grips ?? new List<GraphMapperGripState>()).Where(Finite)
                .OrderBy(grip => grip.Index).ThenBy(grip => grip.X).ThenBy(grip => grip.Y).Take(MaximumGrips).ToList();
            string gripText = grips.Count == 0 ? "none" : String.Join(", ", grips.Select(grip => grip.Index.ToString(CultureInfo.InvariantCulture)
                + ":(" + Number(grip.X) + ", " + Number(grip.Y) + ", " + OneLine(grip.Constraint) + ")").ToArray());
            int totalGrips = Math.Max(capture.TotalGripCount, capture.Grips == null ? 0 : capture.Grips.Count);
            if (totalGrips > grips.Count) gripText += ", " + (totalGrips - grips.Count).ToString(CultureInfo.InvariantCulture) + " omitted";
            return "authored graph state: type=" + OneLine(capture.GraphType) + "; input domain=" + Pair(capture.InputDomainStart, capture.InputDomainEnd)
                + "; output domain=" + Pair(capture.OutputDomainStart, capture.OutputDomainEnd) + "; grips=" + gripText
                + "; normalized samples=" + Math.Max(capture.TotalSampleCount, capture.Samples == null ? 0 : capture.Samples.Count).ToString(CultureInfo.InvariantCulture);
        }

        public static GraphMapperStateCapture Unavailable(string note)
        {
            return new GraphMapperStateCapture { CaptureStatus = "unavailable", CaptureNote = OneLine(note) };
        }

        private static void Add(ICollection<ContextMetadataEntry> values, string key, string value)
        {
            values.Add(new ContextMetadataEntry { Key = key, Value = value ?? "" });
        }

        private static string Pair(double first, double second) { return "[" + Number(first) + ", " + Number(second) + "]"; }
        private static string Number(double value) { return value.ToString("G17", CultureInfo.InvariantCulture); }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static bool Finite(GraphMapperGripState value) { return value != null && Finite(value.X) && Finite(value.Y); }
        private static bool Finite(GraphMapperSampleState value)
        {
            return value != null && Finite(value.NormalizedInput) && Finite(value.NormalizedOutput) && Finite(value.MappedInput) && Finite(value.MappedOutput);
        }
        private static bool FiniteDomains(GraphMapperStateCapture value)
        {
            return value != null && Finite(value.InputDomainStart) && Finite(value.InputDomainEnd)
                && Finite(value.OutputDomainStart) && Finite(value.OutputDomainEnd);
        }
        private static string OneLine(string value)
        {
            return String.Join(" ", (value ?? "").Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()).Where(part => part.Length > 0).ToArray());
        }
    }
}
