using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

public static class FileContentCombiner
{
    public static string CombineVarSections(string globalFilePath, string stFilePath)
    {
        try
        {
            List<string> declarations = new List<string>();
            List<string> initializations = new List<string>();
            HashSet<string> assignedVars = new HashSet<string>();
            List<string> caseStatements = new List<string>();

            ProcessFile(globalFilePath, "VAR_GLOBAL", "END_VAR", declarations, initializations, assignedVars, caseStatements, isStFile: false);
            ProcessFile(stFilePath, "VAR", "END_VAR", declarations, initializations, assignedVars, caseStatements, isStFile: true);

            List<string> result = new List<string>();

            bool hasTimer = declarations.Exists(line => line.Contains(":timer;"));
            if (hasTimer)
            {
                result.Add("module timer(){");
                result.Add("  I: 0..1;                     /* вход                                  */");
                result.Add("  Q: 0..1;                     /* выход                                 */");
                result.Add("  init(I):=0; init(Q):=0;      /* инициализация                         */");
                result.Add("  next(Q):= I & (Q | {0, 1});  /* новое значение выхода таймера         */");
                result.Add("  FAIRNESS I -> Q;             /* условие честного срабатывания таймера */");
                result.Add("}");
                result.Add("");
            }

            result.Add("module main(){");
            result.AddRange(declarations);

            result.Add("  /* Раздел инициализации */");
            result.AddRange(initializations);

            List<string> nextAssignments = new List<string>();
            foreach (string decl in declarations)
            {
                if (!decl.Contains(":0..1;"))
                    continue;

                var match = Regex.Match(decl, @"^\s*(\w+):0\..1;(\s*/\*.*\*/)?\s*$");
                if (!match.Success)
                    continue;

                string varName = match.Groups[1].Value;
                string comment = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
                if (!assignedVars.Contains(varName))
                {
                    nextAssignments.Add($"  next({varName}):={{0,1}};{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                }
            }

            result.Add("  /* Система переходов */");
            result.AddRange(nextAssignments);
            result.AddRange(caseStatements);
            result.Add("}");

            string finalResult = string.Join(Environment.NewLine, result);
            finalResult = Regex.Replace(finalResult, @"next\(next\(([^)]+)\)\)", "next($1)");

            return finalResult;
        }
        catch (Exception ex)
        {
            throw new Exception($"Ошибка при обработке файлов: {ex.Message}");
        }
    }

    private static void ProcessFile(string filePath, string startMarker, string endMarker, List<string> declarations,
        List<string> initializations, HashSet<string> assignedVars, List<string> caseStatements, bool isStFile)
    {
        if (!File.Exists(filePath))
            throw new Exception($"Файл {filePath} не существует.");

        string[] lines = File.ReadAllLines(filePath, Encoding.GetEncoding(1251));
        bool inVarSection = false;
        bool inCodeSection = false;
        List<string> currentIfBlock = null;
        string currentVar = null;
        string currentComment = "";
        bool expectingAssignment = false;
        string pendingCondition = null;

        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            string comment = "";
            string lineWithoutComment = trimmedLine;

            int commentStart = trimmedLine.IndexOf("(*");
            if (commentStart >= 0)
            {
                int commentEnd = trimmedLine.IndexOf("*)", commentStart);
                if (commentEnd > commentStart)
                {
                    string rawComment = trimmedLine.Substring(commentStart, commentEnd - commentStart + 2);
                    comment = "/*" + rawComment.Substring(2, rawComment.Length - 4) + "*/";
                    lineWithoutComment = trimmedLine.Remove(commentStart, commentEnd - commentStart + 2).Trim();
                }
            }

            commentStart = lineWithoutComment.IndexOf("//");
            if (commentStart >= 0)
            {
                comment = lineWithoutComment.Substring(commentStart).Trim();
                lineWithoutComment = lineWithoutComment.Substring(0, commentStart).Trim();
            }

            if (lineWithoutComment.StartsWith(startMarker, StringComparison.OrdinalIgnoreCase))
            {
                inVarSection = true;
                continue;
            }
            if (lineWithoutComment.StartsWith(endMarker, StringComparison.OrdinalIgnoreCase))
            {
                inVarSection = false;
                inCodeSection = isStFile;
                continue;
            }

            if (inVarSection)
            {
                if (lineWithoutComment.StartsWith("_"))
                    continue;

                lineWithoutComment = Regex.Replace(lineWithoutComment, @"\s+", " ");

                if (Regex.IsMatch(lineWithoutComment, @":\s*BOOL", RegexOptions.IgnoreCase))
                {
                    var match = Regex.Match(lineWithoutComment, @"^(.*?):\s*BOOL\s*(?::=\s*(\d|TRUE|FALSE))?\s*;", RegexOptions.IgnoreCase);
                    if (!match.Success)
                        continue;

                    string varNamesPart = match.Groups[1].Value.Trim();
                    string[] varNames = varNamesPart.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                    string initValue = "0";
                    if (match.Groups[2].Success)
                    {
                        string initPart = match.Groups[2].Value.Trim();
                        if (initPart == "1" || initPart.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                            initValue = "1";
                        else if (initPart == "0" || initPart.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                            initValue = "0";
                    }

                    foreach (string varName in varNames)
                    {
                        string cleanVarName = varName.Trim();
                        if (string.IsNullOrEmpty(cleanVarName) || cleanVarName.StartsWith("_"))
                            continue;

                        declarations.Add($"  {cleanVarName}:0..1;{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                        initializations.Add($"  init({cleanVarName}):={initValue};{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                    }
                }
                else if (Regex.IsMatch(lineWithoutComment, @":\s*TON\s*:=\s*\(PT\s*:=\s*T#\d+s\)\s*;", RegexOptions.IgnoreCase))
                {
                    var match = Regex.Match(lineWithoutComment, @"^(\w+):\s*TON\s*:=\s*\(PT\s*:=\s*T#\d+s\)\s*;", RegexOptions.IgnoreCase);
                    if (!match.Success)
                        continue;

                    string varName = match.Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(varName) || varName.StartsWith("_"))
                        continue;

                    declarations.Add($"  {varName}:timer;{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                }
            }
            else if (inCodeSection)
            {
                if (string.IsNullOrEmpty(lineWithoutComment) && !string.IsNullOrEmpty(comment))
                {
                    caseStatements.Add($"  {comment}");
                    continue;
                }

                // Declare assignMatch once to avoid CS0136
                Match assignMatch = null;

                // Обработка IF с THEN (присваивание может быть на следующей строке)
                var ifMatch = Regex.Match(lineWithoutComment, @"^IF\s+(.+?)\s+THEN\s*$", RegexOptions.IgnoreCase);
                if (ifMatch.Success)
                {
                    string condition = ifMatch.Groups[1].Value.Trim();
                    expectingAssignment = true;
                    pendingCondition = condition;
                    currentComment = comment;
                    continue;
                }

                // Обработка ELSIF с THEN (присваивание может быть на следующей строке)
                var elsifMatch = Regex.Match(lineWithoutComment, @"^ELSIF\s+(.+?)\s+THEN\s*$", RegexOptions.IgnoreCase);
                if (elsifMatch.Success && currentIfBlock != null)
                {
                    string condition = elsifMatch.Groups[1].Value.Trim();
                    expectingAssignment = true;
                    pendingCondition = condition;
                    currentComment = comment;
                    continue;
                }

                // Обработка присваивания после THEN
                if (expectingAssignment)
                {
                    assignMatch = Regex.Match(lineWithoutComment, @"^(\w+)\s*:=\s*(0|1)\s*;", RegexOptions.IgnoreCase);
                    if (assignMatch.Success)
                    {
                        string varName = assignMatch.Groups[1].Value;
                        string value = assignMatch.Groups[2].Value;
                        if (!varName.StartsWith("_"))
                        {
                            assignedVars.Add(varName);
                            if (currentIfBlock == null)
                            {
                                currentIfBlock = new List<string>();
                                currentVar = varName;
                            }
                            currentIfBlock.Add($"{pendingCondition}:{varName}:{value}");
                            expectingAssignment = false;
                            pendingCondition = null;
                        }
                    }
                    continue;
                }

                // Обработка IF с присваиванием на той же строке
                ifMatch = Regex.Match(lineWithoutComment, @"^IF\s+(.+?)\s+THEN\s+(\w+):=(0|1);", RegexOptions.IgnoreCase);
                if (ifMatch.Success)
                {
                    string condition = ifMatch.Groups[1].Value.Trim();
                    string varName = ifMatch.Groups[2].Value;
                    string value = ifMatch.Groups[3].Value;
                    if (!varName.StartsWith("_"))
                    {
                        assignedVars.Add(varName);
                        currentIfBlock = new List<string> { $"{condition}:{varName}:{value}" };
                        currentVar = varName;
                        currentComment = comment;
                    }
                    continue;
                }

                // Обработка ELSIF с присваиванием на той же строке
                elsifMatch = Regex.Match(lineWithoutComment, @"^ELSIF\s+(.+?)\s+THEN\s+(\w+):=(0|1);", RegexOptions.IgnoreCase);
                if (elsifMatch.Success && currentIfBlock != null)
                {
                    string condition = elsifMatch.Groups[1].Value.Trim();
                    string varName = elsifMatch.Groups[2].Value;
                    string value = elsifMatch.Groups[3].Value;
                    if (!varName.StartsWith("_") && varName == currentVar)
                    {
                        assignedVars.Add(varName);
                        currentIfBlock.Add($"{condition}:{varName}:{value}");
                    }
                    continue;
                }

                if (lineWithoutComment.Equals("END_IF", StringComparison.OrdinalIgnoreCase) && currentIfBlock != null)
                {
                    List<string> caseLines = new List<string> { $"    case{{" };
                    foreach (string block in currentIfBlock)
                    {
                        var parts = block.Split(':');
                        string condition = parts[0];
                        string varName = parts[1];
                        string value = parts[2];

                        condition = TransformCondition(condition);
                        caseLines.Add($"      {condition,-50} : next({varName}):={value};");
                    }
                    caseLines.Add($"      default                                : next({currentVar}):={currentVar};");
                    caseLines.Add("    };");
                    if (!string.IsNullOrEmpty(currentComment))
                        caseStatements.Add($"  {currentComment}");
                    caseStatements.AddRange(caseLines);

                    currentIfBlock = null;
                    currentVar = null;
                    currentComment = "";
                    expectingAssignment = false;
                    pendingCondition = null;
                    continue;
                }
                if (lineWithoutComment.Equals("END_IF;", StringComparison.OrdinalIgnoreCase) && currentIfBlock != null)
                {
                    List<string> caseLines = new List<string> { $"    case{{" };
                    foreach (string block in currentIfBlock)
                    {
                        var parts = block.Split(':');
                        string condition = parts[0];
                        string varName = parts[1];
                        string value = parts[2];

                        condition = TransformCondition(condition);
                        caseLines.Add($"      {condition,-50} : next({varName}):={value};");
                    }
                    caseLines.Add($"      default                                : next({currentVar}):={currentVar};");
                    caseLines.Add("    };");
                    if (!string.IsNullOrEmpty(currentComment))
                        caseStatements.Add($"  {currentComment}");
                    caseStatements.AddRange(caseLines);

                    currentIfBlock = null;
                    currentVar = null;
                    currentComment = "";
                    expectingAssignment = false;
                    pendingCondition = null;
                    continue;
                }

                // Обработка присваиваний таймерам (IN-входов), заменяем .IN на .I
                var timerAssignMatch = Regex.Match(lineWithoutComment, @"^(\w+\.IN)\s*:=\s*(.+?)\s*;", RegexOptions.IgnoreCase);
                if (timerAssignMatch.Success)
                {
                    string timerVar = timerAssignMatch.Groups[1].Value.Replace(".IN", ".I"); // Заменяем .IN на .I
                    string expr = timerAssignMatch.Groups[2].Value;

                    string transformedExpr = TransformAssignment(expr);
                    caseStatements.Add($"  {timerVar}:={transformedExpr};{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                    continue;
                }

                // Обработка обычных одиночных присваиваний
                assignMatch = Regex.Match(lineWithoutComment, @"^(\w+)\s*:=\s*(.+?)\s*;", RegexOptions.IgnoreCase);
                if (assignMatch.Success && currentIfBlock == null)
                {
                    string varName = assignMatch.Groups[1].Value;
                    string expr = assignMatch.Groups[2].Value;

                    if (!varName.StartsWith("_"))
                    {
                        assignedVars.Add(varName);
                        string transformedExpr = TransformAssignment(expr);
                        caseStatements.Add($"  next({varName}):={transformedExpr};{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                    }
                    continue;
                }
            }
        }
    }

    private static string TransformCondition(string condition)
    {
        condition = Regex.Replace(condition, @"\bNOT\b", "~", RegexOptions.IgnoreCase);
        condition = Regex.Replace(condition, @"\bAND\b", "&", RegexOptions.IgnoreCase);
        condition = Regex.Replace(condition, @"\bOR\b", "|", RegexOptions.IgnoreCase);
        condition = Regex.Replace(condition, @"\s*([~&|])\s*", " $1 ");

        var variables = Regex.Matches(condition, @"\b_?(\w+)(\.Q)?\b");
        foreach (Match match in variables)
        {
            string originalVar = match.Value;
            string varName = match.Groups[1].Value;

            if (new[] { "NOT", "AND", "OR", "TRUE", "FALSE" }.Contains(varName, StringComparer.OrdinalIgnoreCase))
                continue;

            string cleanVar = originalVar.StartsWith("_") ? varName : originalVar;

            if (match.Groups[2].Success || !originalVar.StartsWith("_"))
            {
                cleanVar = $"next({cleanVar})";
            }

            condition = condition.Replace(originalVar, cleanVar);
        }

        return Regex.Replace(condition, @"\s+", " ").Trim();
    }

    private static string TransformAssignment(string expr)
    {
        var variables = Regex.Matches(expr, @"\b_?(\w+)(\.Q)?\b");
        foreach (Match match in variables)
        {
            string originalVar = match.Value;
            string varName = match.Groups[1].Value;

            if (new[] { "NOT", "AND", "OR", "TRUE", "FALSE" }.Contains(varName, StringComparer.OrdinalIgnoreCase))
                continue;

            string cleanVar = originalVar.StartsWith("_") ? varName : originalVar;
            string replacement = $"next({cleanVar})";
            expr = expr.Replace(originalVar, replacement);
        }

        expr = Regex.Replace(expr, @"\bNOT\b", "~", RegexOptions.IgnoreCase);
        expr = Regex.Replace(expr, @"\bAND\b", "&", RegexOptions.IgnoreCase);
        expr = Regex.Replace(expr, @"\bOR\b", "|", RegexOptions.IgnoreCase);
        expr = Regex.Replace(expr, @"\s*([~&|])\s*", " $1 ");

        return Regex.Replace(expr, @"\s+", " ").Trim();
    }
}