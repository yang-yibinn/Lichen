using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lichen.Core
{
    public sealed class ScriptBehaviorSummary
    {
        public ScriptBehaviorSummary()
        {
            AuthorDescription = "";
            PossibleRole = "";
            Observations = new List<string>();
            Evidence = new List<string>();
            DetectedCalls = new List<string>();
        }

        public string AuthorDescription { get; set; }
        public string PossibleRole { get; set; }
        public List<string> Observations { get; private set; }
        public List<string> Evidence { get; private set; }
        public List<string> DetectedCalls { get; private set; }
    }

    /// <summary>
    /// Describes directly observable source behavior using conservative rules.
    /// It never executes or compiles source. A possible role is emitted only when
    /// several distinctive API signals agree; it is not treated as design intent.
    /// </summary>
    public static class ScriptBehaviorAnalyzer
    {
        public static ScriptBehaviorSummary Analyze(ContextNode node)
        {
            ScriptBehaviorSummary result = new ScriptBehaviorSummary();
            if (node == null || node.Script == null || String.IsNullOrWhiteSpace(node.Script.Source)) return result;

            string source = node.Script.Source;
            string lower = source.ToLowerInvariant();
            string language = (node.Script.Language ?? "").ToLowerInvariant();
            result.AuthorDescription = ExtractAuthorDescription(source, language);

            if (language.IndexOf("expression", StringComparison.Ordinal) >= 0)
            {
                DescribeExpression(source, result);
                return result;
            }

            if (HasAll(lower, "curve.getfilletpoints", "duplicatesegments", "joincurves", "radii"))
            {
                result.Observations.Add("Reads a curve together with curve parameters and one or more radius values.");
                result.Observations.Add("Finds adjacent curve segments and constructs fillet arcs with Curve.GetFilletPoints.");
                if (HasAll(lower, "overlapping fillet", "trimparams"))
                    result.Observations.Add("Removes overlapping fillets, trims the original curve, and joins the retained segments and arcs into the output curve.");
                else result.Observations.Add("Trims the original curve and joins the retained segments and fillet arcs into the output curve.");
                result.PossibleRole = "rebuild curves with per-location fillet radii";
                AddEvidence(result, "Curve.GetFilletPoints", "Curve.DuplicateSegments", "Curve.JoinCurves");
            }
            else if (HasAll(lower, "lengthparameter", ".trim(", "getlength"))
            {
                result.Observations.Add("Reads a curve and one or more intervals expressed as distances along that curve.");
                result.Observations.Add("Converts distance endpoints to native curve parameters with LengthParameter and trims the corresponding subcurves.");
                if (HasAll(lower, "isclosed", "% l")) result.Observations.Add("Clamps distances on open curves and wraps distances around closed curves.");
                result.Observations.Add("Reports trimmed curves together with measured lengths and parameter intervals.");
                result.PossibleRole = "extract curve portions by physical distance along a curve";
                AddEvidence(result, "Curve.GetLength", "Curve.LengthParameter", "Curve.Trim");
            }
            else if (HasAll(lower, "mesh.createbooleansplit", "splitdisjointpieces", "ispointinside"))
            {
                result.Observations.Add("Converts supplied cutter geometry to a mesh and attempts to split the input mesh with it.");
                if (HasAll(lower, "boolcontaminated", ".split(cutter)"))
                    result.Observations.Add("Rejects Boolean results containing cutter-aligned faces and falls back to a legacy mesh split.");
                result.Observations.Add("Classifies disjoint pieces against the cutter and separates outside and inside results.");
                if (HasAll(lower, "stripcutterfaces", "culldegeneratefaces"))
                    result.Observations.Add("Removes cutter-aligned and degenerate faces, rebuilds normals, and optionally emits diagnostics.");
                result.PossibleRole = "split meshes with cutter geometry and separate inside/outside results";
                AddEvidence(result, "Mesh.CreateBooleanSplit", "Mesh.SplitDisjointPieces", "Mesh.IsPointInside");
            }
            else if (ContainsAny(lower, "brep.createbooleandifference", "brep.createbooleanunion", "brep.createbooleanintersection"))
            {
                result.Observations.Add("Performs a RhinoCommon Boolean operation on Brep geometry and returns the resulting Breps.");
                result.PossibleRole = "combine or subtract solid Brep geometry";
                AddPresentEvidence(lower, result, "Brep.CreateBooleanDifference", "Brep.CreateBooleanUnion", "Brep.CreateBooleanIntersection");
            }
            else if (HasAll(lower, ".transform(", "transform."))
            {
                result.Observations.Add("Constructs a Rhino transformation and applies it to geometry.");
                result.PossibleRole = "transform input geometry";
                AddPresentEvidence(lower, result, "Transform.Translation", "Transform.Rotation", "Transform.Scale", "GeometryBase.Transform");
            }
            else if (ContainsAny(lower, "intersection.curvecurve", "intersection.brepbrep", "intersection.meshmesh"))
            {
                result.Observations.Add("Computes geometric intersections and reports the resulting events or geometry.");
                result.PossibleRole = "calculate geometric intersections";
                AddPresentEvidence(lower, result, "Intersection.CurveCurve", "Intersection.BrepBrep", "Intersection.MeshMesh");
            }
            else if (HasAll(lower, "datatree<", "gh_path"))
            {
                result.Observations.Add("Constructs or reorganizes Grasshopper data-tree branches using GH_Path values.");
                result.PossibleRole = "assemble or reorganize Grasshopper data trees";
                AddEvidence(result, "DataTree<T>", "GH_Path");
            }

            result.DetectedCalls.AddRange(DetectCalls(source).Where(c => !result.Evidence.Contains(c, StringComparer.OrdinalIgnoreCase)).Take(8));
            return result;
        }

        private static void DescribeExpression(string source, ScriptBehaviorSummary result)
        {
            string expression = OneLine(source);
            string operand = @"(?:[A-Za-z_]\w*|[-+]?(?:\d+(?:\.\d*)?|\.\d+))";
            Match conditional = Regex.Match(expression, "^if\\s*\\(\\s*(?<left>" + operand + ")\\s*(?<op>>=|<=|==|!=|>|<)\\s*(?<right>" + operand + ")\\s*,\\s*(?<yes>" + operand + ")\\s*,\\s*(?<no>" + operand + ")\\s*\\)$", RegexOptions.IgnoreCase);
            if (conditional.Success)
            {
                result.Observations.Add("Returns " + conditional.Groups["yes"].Value + " when " + conditional.Groups["left"].Value + " " + conditional.Groups["op"].Value + " " + conditional.Groups["right"].Value + "; otherwise returns " + conditional.Groups["no"].Value + ".");
                result.PossibleRole = IsBinaryFlag(conditional.Groups["yes"].Value, conditional.Groups["no"].Value) ? "produce a binary threshold flag" : "select between two values using a condition";
            }
            else
            {
                Match normalized = Regex.Match(expression, @"^\(\s*(?<value>[A-Za-z_]\w*)\s*-\s*(?<minimum>[A-Za-z_]\w*)\s*\)\s*/\s*\(\s*(?<maximum>[A-Za-z_]\w*)\s*-\s*\k<minimum>\s*\)$", RegexOptions.IgnoreCase);
                if (normalized.Success)
                {
                    result.Observations.Add("Subtracts " + normalized.Groups["minimum"].Value + " from " + normalized.Groups["value"].Value + " and divides by " + normalized.Groups["maximum"].Value + " minus " + normalized.Groups["minimum"].Value + ", producing a normalized ratio.");
                    result.PossibleRole = "normalize a value between two reference bounds";
                }
                else
                {
                    Match arithmetic = Regex.Match(expression, "^\\s*(?<left>" + operand + ")\\s*(?<op>[+\\-*/])\\s*(?<right>" + operand + ")\\s*$", RegexOptions.IgnoreCase);
                    if (arithmetic.Success)
                    {
                        string operation = arithmetic.Groups["op"].Value == "+" ? "Adds" : arithmetic.Groups["op"].Value == "-" ? "Subtracts" : arithmetic.Groups["op"].Value == "*" ? "Multiplies" : "Divides";
                        string connector = arithmetic.Groups["op"].Value == "-" ? " from " : arithmetic.Groups["op"].Value == "/" ? " by " : " and ";
                        string left = arithmetic.Groups["left"].Value, right = arithmetic.Groups["right"].Value;
                        result.Observations.Add(operation + (arithmetic.Groups["op"].Value == "-" ? " " + right + connector + left : " " + left + connector + right) + ".");
                        result.PossibleRole = "perform a basic arithmetic calculation";
                    }
                    else result.Observations.Add("Evaluates the captured Grasshopper expression.");
                }
            }
            result.Evidence.Add(expression);
        }

        private static bool IsBinaryFlag(string whenTrue, string whenFalse)
        {
            return (whenTrue == "1" && whenFalse == "0") || (whenTrue == "0" && whenFalse == "1");
        }

        private static string ExtractAuthorDescription(string source, string language)
        {
            if (language.IndexOf("python", StringComparison.Ordinal) >= 0)
            {
                Match doc = Regex.Match(source, "^\\s*(?:\"\"\"|''')(?<body>[\\s\\S]*?)(?:\"\"\"|''')");
                if (doc.Success)
                {
                    List<string> lines = doc.Groups["body"].Value.Replace("\r", "").Split('\n').Select(l => l.Trim())
                        .Where(l => l.Length > 0 && !Regex.IsMatch(l, @"^[-=]+$") && !String.Equals(l, "INPUTS", StringComparison.OrdinalIgnoreCase) && !String.Equals(l, "OUTPUTS", StringComparison.OrdinalIgnoreCase)).ToList();
                    if (lines.Count > 0)
                    {
                        string text = lines[0];
                        if (lines.Count > 1 && lines[1].Length > 12) text += " — " + lines[1];
                        return Bounded(text, 240);
                    }
                }
            }
            Match xml = Regex.Match(source, @"(?s)<summary>\s*(?<body>.*?)\s*</summary>", RegexOptions.IgnoreCase);
            return xml.Success ? Bounded(Regex.Replace(xml.Groups["body"].Value, @"\s+", " ").Trim(), 240) : "";
        }

        private static List<string> DetectCalls(string source)
        {
            List<string> calls = new List<string>();
            foreach (Match match in Regex.Matches(source ?? "", @"\b(?:[A-Z][A-Za-z0-9_]*\.)+[A-Z][A-Za-z0-9_]*\s*\("))
            {
                string call = match.Value.Trim().TrimEnd('(').Trim();
                if (!calls.Contains(call, StringComparer.OrdinalIgnoreCase)) calls.Add(call);
            }
            return calls;
        }

        private static void AddEvidence(ScriptBehaviorSummary result, params string[] evidence)
        {
            foreach (string value in evidence) if (!result.Evidence.Contains(value, StringComparer.OrdinalIgnoreCase)) result.Evidence.Add(value);
        }

        private static void AddPresentEvidence(string lower, ScriptBehaviorSummary result, params string[] evidence)
        {
            foreach (string value in evidence) if (lower.IndexOf(value.ToLowerInvariant(), StringComparison.Ordinal) >= 0) AddEvidence(result, value);
        }

        private static bool HasAll(string source, params string[] signals)
        {
            foreach (string signal in signals) if (source.IndexOf(signal.ToLowerInvariant(), StringComparison.Ordinal) < 0) return false;
            return true;
        }

        private static bool ContainsAny(string source, params string[] signals)
        {
            foreach (string signal in signals) if (source.IndexOf(signal.ToLowerInvariant(), StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static string OneLine(string value) { return Regex.Replace((value ?? "").Replace("\r", " ").Replace("\n", " "), @"\s+", " ").Trim(); }
        private static string Bounded(string value, int maximum) { return String.IsNullOrEmpty(value) || value.Length <= maximum ? value ?? "" : value.Substring(0, maximum - 1).TrimEnd() + "…"; }
    }
}
