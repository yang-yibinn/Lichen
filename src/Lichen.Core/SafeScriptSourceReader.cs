using System;
using System.Collections.Generic;
using System.Reflection;

namespace Lichen.Core
{
    public sealed class ScriptSourceReadResult
    {
        public ScriptSourceReadResult()
        {
            Language = "";
            Source = "";
            ExtractionNote = "";
        }

        public bool Recognized { get; set; }
        public string Language { get; set; }
        public string Source { get; set; }
        public string ExtractionNote { get; set; }
    }

    /// <summary>
    /// Reads script text through a small allow-list of public, read-only APIs.
    /// It has no Rhino dependency so every supported API shape can be tested
    /// without loading or starting Rhino or Grasshopper.
    /// </summary>
    public static class SafeScriptSourceReader
    {
        public static ScriptSourceReadResult Read(object target)
        {
            ScriptSourceReadResult result = new ScriptSourceReadResult();
            if (target == null) return result;

            Type type = target.GetType();
            string typeName = type.FullName ?? type.Name ?? "";
            string lower = typeName.ToLowerInvariant();
            if (!LooksLikeScriptOrExpression(lower)) return result;

            result.Recognized = true;
            result.Language = DetectLanguage(lower);

            string source;
            string error;
            if (TryReadOutStringMethod(target, "TryGetSource", out source, out error) && !String.IsNullOrWhiteSpace(source))
            {
                result.Source = source;
                return result;
            }

            object structured;
            if (TryReadProperty(target, "ScriptSource", out structured, out error) && structured != null)
            {
                string composite = ReadStructuredSource(structured);
                if (!String.IsNullOrWhiteSpace(composite))
                {
                    result.Source = composite;
                    return result;
                }
            }

            string[] propertyNames = { "Expression", "Code", "SourceCode", "Script", "Text" };
            foreach (string propertyName in propertyNames)
            {
                object value;
                if (TryReadProperty(target, propertyName, out value, out error))
                {
                    source = value as string;
                    if (!String.IsNullOrWhiteSpace(source))
                    {
                        result.Source = source;
                        return result;
                    }
                }
            }

            result.ExtractionNote = "The component appears to contain a script or expression, but the installed SDK exposed no safely readable source through Lichen's supported public APIs.";
            return result;
        }

        private static bool LooksLikeScriptOrExpression(string lowerTypeName)
        {
            return lowerTypeName.IndexOf("script", StringComparison.Ordinal) >= 0
                || lowerTypeName.IndexOf("python", StringComparison.Ordinal) >= 0
                || lowerTypeName.IndexOf("csharp", StringComparison.Ordinal) >= 0
                || lowerTypeName.IndexOf("ghpython", StringComparison.Ordinal) >= 0
                || lowerTypeName.IndexOf("component_expression", StringComparison.Ordinal) >= 0;
        }

        private static string DetectLanguage(string lowerTypeName)
        {
            if (lowerTypeName.IndexOf("component_expression", StringComparison.Ordinal) >= 0) return "Grasshopper expression";
            if (lowerTypeName.IndexOf("python3", StringComparison.Ordinal) >= 0) return "Python 3";
            if (lowerTypeName.IndexOf("ironpython2", StringComparison.Ordinal) >= 0
                || lowerTypeName.IndexOf("ghpython", StringComparison.Ordinal) >= 0
                || lowerTypeName.IndexOf("zuipython", StringComparison.Ordinal) >= 0) return "Python 2 (IronPython)";
            if (lowerTypeName.IndexOf("python", StringComparison.Ordinal) >= 0) return "Python";
            if (lowerTypeName.IndexOf("csharp", StringComparison.Ordinal) >= 0
                || lowerTypeName.IndexOf("csnet", StringComparison.Ordinal) >= 0
                || lowerTypeName.IndexOf("c#", StringComparison.Ordinal) >= 0) return "C#";
            if (lowerTypeName.IndexOf("vbnet", StringComparison.Ordinal) >= 0) return "VB.NET";
            return "Unknown script language";
        }

        private static string ReadStructuredSource(object structured)
        {
            List<string> sections = new List<string>();
            string[] sectionNames = { "UsingCode", "ScriptCode", "AdditionalCode" };
            foreach (string sectionName in sectionNames)
            {
                object value;
                string error;
                if (!TryReadProperty(structured, sectionName, out value, out error)) continue;
                string text = value as string;
                if (!String.IsNullOrWhiteSpace(text)) sections.Add(text);
            }
            return String.Join(Environment.NewLine + Environment.NewLine, sections.ToArray());
        }

        private static bool TryReadOutStringMethod(object target, string methodName, out string value, out string error)
        {
            value = "";
            error = "";
            try
            {
                foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!String.Equals(method.Name, methodName, StringComparison.Ordinal) || method.ReturnType != typeof(bool)) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1 || !parameters[0].IsOut || parameters[0].ParameterType != typeof(string).MakeByRefType()) continue;

                    object[] arguments = { null };
                    bool succeeded = (bool)method.Invoke(target, arguments);
                    value = arguments[0] as string ?? "";
                    return succeeded;
                }
            }
            catch (Exception ex)
            {
                error = OneLine(Unwrap(ex).Message);
            }
            return false;
        }

        private static bool TryReadProperty(object target, string propertyName, out object value, out string error)
        {
            value = null;
            error = "";
            if (target == null) return false;
            try
            {
                PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0) return false;
                MethodInfo getter = property.GetGetMethod(false);
                if (getter == null || !getter.IsPublic) return false;
                value = property.GetValue(target, null);
                return true;
            }
            catch (Exception ex)
            {
                error = OneLine(Unwrap(ex).Message);
                return false;
            }
        }

        private static Exception Unwrap(Exception exception)
        {
            TargetInvocationException invocation = exception as TargetInvocationException;
            return invocation != null && invocation.InnerException != null ? invocation.InnerException : exception;
        }

        private static string OneLine(string value)
        {
            return (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
