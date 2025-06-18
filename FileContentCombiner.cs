using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

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
            HashSet<string> declaredVars = new HashSet<string>();

            ProcessFile(globalFilePath, "VAR_GLOBAL", "END_VAR", declarations, initializations, assignedVars, caseStatements, declaredVars, isStFile: false);
            ProcessFile(stFilePath, "VAR", "END_VAR", declarations, initializations, assignedVars, caseStatements, declaredVars, isStFile: true);

            StringBuilder result = new StringBuilder();

            bool hasTimer = declarations.Any(line => line.Contains(":timer;"));
            if (hasTimer)
            {
                result.AppendLine("module timer(){");
                result.AppendLine("  I: 0..1;                     /* вход                                  */");
                result.AppendLine("  Q: 0..1;                     /* выход                                 */");
                result.AppendLine("  init(I):=0; init(Q):=0;      /* инициализация                         */");
                result.AppendLine("  next(Q):= I & (Q | {0, 1});  /* новое значение выхода таймера         */");
                result.AppendLine("  FAIRNESS I -> Q;             /* условие честного срабатывания таймера */");
                result.AppendLine("}");
                result.AppendLine();
            }

            result.AppendLine("module main(){");
            foreach (var decl in declarations)
                result.AppendLine(decl);

            result.AppendLine("  /* Раздел инициализации */");
            foreach (var init in initializations)
                result.AppendLine(init);

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

            result.AppendLine("  /* Система переходов */");
            foreach (var nextAssign in nextAssignments)
                result.AppendLine(nextAssign);
            foreach (var stmt in caseStatements)
                result.AppendLine(stmt);
            result.AppendLine("}");

            string finalResult = result.ToString();
            finalResult = Regex.Replace(finalResult, @"next\(\s*next\(([^)]+)\)\)", "next($1)", RegexOptions.IgnoreCase);

            return finalResult;
        }
        catch (IOException ex)
        {
            throw new Exception($"IO error processing files '{globalFilePath}' or '{stFilePath}': {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error processing files '{globalFilePath}' or '{stFilePath}': {ex.Message}", ex);
        }
    }

    private static void ProcessFile(string filePath, string startMarker, string endMarker, List<string> declarations,
        List<string> initializations, HashSet<string> assignedVars, List<string> caseStatements, HashSet<string> declaredVars, bool isStFile)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"File not found: '{filePath}'");

        string[] lines = File.ReadAllLines(filePath, Encoding.GetEncoding(1251));
        int currentIndex = 0;
        while (currentIndex < lines.Length)
        {
            ProcessVarSection(lines, startMarker, endMarker, declarations, initializations, declaredVars, isStFile, ref currentIndex);
            if (isStFile)
                ProcessCodeSection(lines, endMarker, assignedVars, caseStatements, declaredVars);
            break; // ProcessVarSection и ProcessCodeSection управляют индексом сами
        }
    }

    private static void ProcessCodeSection(string[] lines, string endMarker, HashSet<string> assignedVars, List<string> caseStatements, HashSet<string> declaredVars)
    {
        bool inCodeSection = false;
        List<(string condition, string varName, string value, string comment)> currentIfBlock = null;
        string currentVar = null;
        string pendingComment = null;
        bool expectingAssignment = false;
        int ifNestingLevel = 0;
        StringBuilder multiLineExpr = null;
        string multiLineVarName = null;
        string multiLineComment = null;
        StringBuilder multiLineCondition = null;
        string conditionStarter = null;
        int parenthesisLevel = 0;
        string pendingCondition = null;
        bool collectingMultiLineCondition = false;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line))
            {
                string commentOnly = ExtractComment(ref line, lines, ref i);
                if (!string.IsNullOrEmpty(commentOnly))
                {
                    caseStatements.Add($"  {commentOnly}");
                }
                continue;
            }

            string comment = ExtractComment(ref line, lines, ref i);
            string lineWithoutComment = line;

            if (string.IsNullOrEmpty(lineWithoutComment) && !string.IsNullOrEmpty(comment))
            {
                caseStatements.Add($"  {comment}");
                continue;
            }

            if (!inCodeSection && lineWithoutComment.StartsWith(endMarker, StringComparison.OrdinalIgnoreCase))
            {
                inCodeSection = true;
                continue;
            }

            if (!inCodeSection)
                continue;

            // Обработка многострочных условий
            if (collectingMultiLineCondition)
            {
                lineWithoutComment = lineWithoutComment.Trim();
                parenthesisLevel += lineWithoutComment.Count(c => c == '(');
                parenthesisLevel -= lineWithoutComment.Count(c => c == ')');
                multiLineCondition.Append(" ").Append(lineWithoutComment);

                if (lineWithoutComment.IndexOf("THEN", StringComparison.OrdinalIgnoreCase) >= 0 && parenthesisLevel == 0)
                {
                    string fullCondition = multiLineCondition.ToString();
                    var thenMatch = Regex.Match(fullCondition, @"^(.*?)\s+THEN\s*(.*)$", RegexOptions.IgnoreCase);
                    if (thenMatch.Success)
                    {
                        string condition = thenMatch.Groups[1].Value.Trim();
                        string afterThen = thenMatch.Groups[2].Value.Trim();
                        condition = Regex.Replace(condition, @"\s+", " ");

                        if (conditionStarter == "IF" && ifNestingLevel == 0)
                        {
                            ifNestingLevel++;
                            if (string.IsNullOrEmpty(afterThen))
                            {
                                expectingAssignment = true;
                                pendingCondition = condition;
                                pendingComment = multiLineComment;
                            }
                            else
                            {
                                var assignMatch = Regex.Match(afterThen, @"^(\w+)\s*:=\s*(0|1)\s*;", RegexOptions.IgnoreCase);
                                if (assignMatch.Success)
                                {
                                    string varName = assignMatch.Groups[1].Value;
                                    string value = assignMatch.Groups[2].Value;
                                    if (!varName.StartsWith("_"))
                                    {
                                        ValidateVariable(varName, declaredVars, i + 1);
                                        ValidateExpressionVariables(condition, declaredVars, i + 1);
                                        assignedVars.Add(varName);
                                        currentIfBlock = new List<(string, string, string, string)> { (condition, varName, value, multiLineComment) };
                                        currentVar = varName;
                                    }
                                }
                            }
                        }
                        else if (conditionStarter == "ELSIF" && currentIfBlock != null && ifNestingLevel == 1)
                        {
                            if (string.IsNullOrEmpty(afterThen))
                            {
                                expectingAssignment = true;
                                pendingCondition = condition;
                                pendingComment = multiLineComment;
                            }
                            else
                            {
                                var assignMatch = Regex.Match(afterThen, @"^(\w+)\s*:=\s*(0|1)\s*;", RegexOptions.IgnoreCase);
                                if (assignMatch.Success)
                                {
                                    string varName = assignMatch.Groups[1].Value;
                                    string value = assignMatch.Groups[2].Value;
                                    if (!varName.StartsWith("_") && varName == currentVar)
                                    {
                                        ValidateVariable(varName, declaredVars, i + 1);
                                        ValidateExpressionVariables(condition, declaredVars, i + 1);
                                        assignedVars.Add(varName);
                                        currentIfBlock.Add((condition, varName, value, multiLineComment));
                                    }
                                }
                            }
                        }

                        collectingMultiLineCondition = false;
                        multiLineCondition = null;
                        conditionStarter = null;
                        parenthesisLevel = 0;
                        multiLineComment = null;
                    }
                }
                continue;
            }

            // Обработка многострочного присваивания
            if (multiLineExpr != null)
            {
                multiLineExpr.Append(" ").Append(lineWithoutComment);
                parenthesisLevel += lineWithoutComment.Count(c => c == '(') - lineWithoutComment.Count(c => c == ')');

                if (lineWithoutComment.EndsWith(";") && parenthesisLevel == 0)
                {
                    string fullExpr = multiLineExpr.ToString().TrimEnd(';').Trim();
                    if (!multiLineVarName.StartsWith("_"))
                    {
                        ValidateVariable(multiLineVarName, declaredVars, i + 1);
                        ValidateExpressionVariables(fullExpr, declaredVars, i + 1);
                        assignedVars.Add(multiLineVarName);
                        string transformedExpr = TransformAssignment(fullExpr);
                        caseStatements.Add($"  next({multiLineVarName}):={transformedExpr};{(!string.IsNullOrEmpty(multiLineComment) ? " " + multiLineComment : "")}");
                    }
                    multiLineExpr = null;
                    multiLineVarName = null;
                    multiLineComment = null;
                    parenthesisLevel = 0;
                }
                continue;
            }

            // Проверяем полное присваивание в одной строке (вне IF/ELSIF)
            var assignMatchDirect = Regex.Match(lineWithoutComment, @"^(\w+)\s*:=\s*(.+?)\s*;", RegexOptions.IgnoreCase);
            if (assignMatchDirect.Success && currentIfBlock == null && !expectingAssignment)
            {
                string varName = assignMatchDirect.Groups[1].Value;
                string expr = assignMatchDirect.Groups[2].Value;
                if (!varName.StartsWith("_"))
                {
                    ValidateVariable(varName, declaredVars, i + 1);
                    ValidateExpressionVariables(expr, declaredVars, i + 1);
                    assignedVars.Add(varName);
                    string transformedExpr = TransformAssignment(expr);
                    caseStatements.Add($"  next({varName}):={transformedExpr};{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                }
                continue;
            }

            // Проверяем начало многострочного присваивания
            var partialAssignMatch = Regex.Match(lineWithoutComment, @"^(\w+)\s*:=\s*(.+)$", RegexOptions.IgnoreCase);
            if (partialAssignMatch.Success && !lineWithoutComment.EndsWith(";") && currentIfBlock == null && !expectingAssignment)
            {
                multiLineVarName = partialAssignMatch.Groups[1].Value;
                multiLineExpr = new StringBuilder(partialAssignMatch.Groups[2].Value);
                multiLineComment = comment;
                parenthesisLevel = lineWithoutComment.Count(c => c == '(') - lineWithoutComment.Count(c => c == ')');
                continue;
            }

            // Проверяем IF с присваиванием в одной строке
            var ifAssignMatch = Regex.Match(lineWithoutComment, @"^IF\s+(.+?)\s+THEN\s+(\w+):=(0|1);", RegexOptions.IgnoreCase);
            if (ifAssignMatch.Success)
            {
                ifNestingLevel++;
                if (ifNestingLevel > 1)
                    continue;

                string condition = ifAssignMatch.Groups[1].Value.Trim();
                string varName = ifAssignMatch.Groups[2].Value;
                string value = ifAssignMatch.Groups[3].Value;
                if (!varName.StartsWith("_"))
                {
                    ValidateVariable(varName, declaredVars, i + 1);
                    ValidateExpressionVariables(condition, declaredVars, i + 1);
                    assignedVars.Add(varName);
                    currentIfBlock = new List<(string, string, string, string)> { (condition, varName, value, comment) };
                    currentVar = varName;
                }
                continue;
            }

            // Проверяем ELSIF с присваиванием в одной строке
            var elsifAssignMatch = Regex.Match(lineWithoutComment, @"^ELSIF\s+(.+?)\s+THEN\s+(\w+):=(0|1);", RegexOptions.IgnoreCase);
            if (elsifAssignMatch.Success && currentIfBlock != null && ifNestingLevel == 1)
            {
                string condition = elsifAssignMatch.Groups[1].Value.Trim();
                string varName = elsifAssignMatch.Groups[2].Value;
                string value = elsifAssignMatch.Groups[3].Value;
                if (!varName.StartsWith("_") && varName == currentVar)
                {
                    ValidateVariable(varName, declaredVars, i + 1);
                    ValidateExpressionVariables(condition, declaredVars, i + 1);
                    assignedVars.Add(varName);
                    currentIfBlock.Add((condition, varName, value, comment));
                }
                continue;
            }

            // Проверяем начало многострочного IF/ELSIF
            var ifMatch = Regex.Match(lineWithoutComment, @"^(IF|ELSIF)\s+(.+)$", RegexOptions.IgnoreCase);
            if (ifMatch.Success)
            {
                string keyword = ifMatch.Groups[1].Value.ToUpper();
                string conditionPart = ifMatch.Groups[2].Value.Trim();
                bool hasThen = conditionPart.IndexOf("THEN", StringComparison.OrdinalIgnoreCase) >= 0;

                int localParenLevel = conditionPart.Count(c => c == '(') - conditionPart.Count(c => c == ')');

                if (!hasThen || localParenLevel != 0)
                {
                    collectingMultiLineCondition = true;
                    conditionStarter = keyword;
                    parenthesisLevel = localParenLevel;
                    multiLineComment = comment;
                    multiLineCondition = new StringBuilder(conditionPart);
                    continue;
                }

                // Обработка IF/ELSIF с THEN в одной строке
                var thenMatch = Regex.Match(conditionPart, @"^(.*?)\s+THEN\s*$", RegexOptions.IgnoreCase);
                if (thenMatch.Success)
                {
                    if (keyword == "IF")
                    {
                        ifNestingLevel++;
                        if (ifNestingLevel > 1)
                            continue;

                        expectingAssignment = true;
                        pendingCondition = thenMatch.Groups[1].Value.Trim();
                        pendingComment = comment;
                    }
                    else if (keyword == "ELSIF" && currentIfBlock != null && ifNestingLevel == 1)
                    {
                        expectingAssignment = true;
                        pendingCondition = thenMatch.Groups[1].Value.Trim();
                        pendingComment = comment;
                    }
                    continue;
                }
            }

            // Обработка присваивания после THEN на следующей строке
            if (expectingAssignment && ifNestingLevel == 1)
            {
                var assignMatchThen = Regex.Match(lineWithoutComment, @"^(\w+)\s*:=\s*(0|1)\s*;", RegexOptions.IgnoreCase);
                if (assignMatchThen.Success)
                {
                    string varName = assignMatchThen.Groups[1].Value;
                    string value = assignMatchThen.Groups[2].Value;
                    if (!varName.StartsWith("_"))
                    {
                        ValidateVariable(varName, declaredVars, i + 1);
                        ValidateExpressionVariables(pendingCondition, declaredVars, i + 1);
                        assignedVars.Add(varName);
                        if (currentIfBlock == null)
                        {
                            currentIfBlock = new List<(string, string, string, string)>();
                            currentVar = varName;
                        }
                        else if (varName != currentVar)
                        {
                            throw new FormatException($"Несогласованное присваивание переменной на строке {i + 1}: ожидалась '{currentVar}', найдена '{varName}'.");
                        }
                        currentIfBlock.Add((pendingCondition, varName, value, pendingComment));
                        expectingAssignment = false;
                        pendingCondition = null;
                        pendingComment = null;
                    }
                    continue;
                }
                // Не сбрасываем expectingAssignment, чтобы дождаться присваивания
            }

            // Обработка END_IF
            if ((lineWithoutComment.Equals("END_IF", StringComparison.OrdinalIgnoreCase) ||
                 lineWithoutComment.Equals("END_IF;", StringComparison.OrdinalIgnoreCase)) && ifNestingLevel > 0)
            {
                ifNestingLevel--;
                if (ifNestingLevel > 0)
                    continue;

                if (currentIfBlock != null)
                {
                    List<string> caseLines = new List<string> { $"    case{{" };
                    foreach (var (condition, varName, value, blockComment) in currentIfBlock)
                    {
                        string transformedCondition = TransformCondition(condition);
                        string caseLine = $"      {transformedCondition,-50} : next({varName}):={value};";
                        if (!string.IsNullOrEmpty(blockComment))
                        {
                            caseLine += $" {blockComment}";
                        }
                        caseLines.Add(caseLine);
                    }
                    caseLines.Add($"      default                                : next({currentVar}):={currentVar};");
                    caseLines.Add("    };");
                    caseStatements.AddRange(caseLines);

                    currentIfBlock = null;
                    currentVar = null;
                    expectingAssignment = false;
                    pendingCondition = null;
                    pendingComment = null;
                }
                continue;
            }

            // Обработка таймеров
            var timerAssignMatch = Regex.Match(lineWithoutComment, @"^(\w+\.IN)\s*:=\s*(.+?)\s*;", RegexOptions.IgnoreCase);
            if (timerAssignMatch.Success)
            {
                string timerVar = timerAssignMatch.Groups[1].Value.Replace(".IN", ".I");
                string expr = timerAssignMatch.Groups[2].Value;
                ValidateVariable(timerVar.Split('.')[0], declaredVars, i + 1);
                ValidateExpressionVariables(expr, declaredVars, i + 1);
                string transformedExpr = TransformAssignment(expr);
                caseStatements.Add($"  {timerVar}:={transformedExpr};{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                continue;
            }
        }

        if (ifNestingLevel > 0 || currentIfBlock != null)
            throw new FormatException("Обнаружен несбалансированный блок IF/END_IF в файле.");
        if (multiLineExpr != null)
            throw new FormatException($"Незавершенное многострочное присваивание для переменной '{multiLineVarName}' в файле.");
        if (multiLineCondition != null)
            throw new FormatException($"Незавершенное многострочное условие для {conditionStarter} в файле.");
    }
    private static void ProcessVarSection(string[] lines, string startMarker, string endMarker, List<string> declarations,
     List<string> initializations, HashSet<string> declaredVars, bool isStFile, ref int currentIndex)
    {
        bool inVarSection = false;
        while (currentIndex < lines.Length)
        {
            string line = lines[currentIndex].Trim();
            if (string.IsNullOrEmpty(line))
            {
                currentIndex++;
                continue;
            }

            string comment = ExtractComment(ref line, lines, ref currentIndex);
            string lineWithoutComment = line;

            if (lineWithoutComment.StartsWith(startMarker, StringComparison.OrdinalIgnoreCase))
            {
                inVarSection = true;
                currentIndex++;
                continue;
            }
            if (lineWithoutComment.StartsWith(endMarker, StringComparison.OrdinalIgnoreCase))
            {
                inVarSection = false;
                currentIndex++;
                continue;
            }

            if (!inVarSection)
            {
                currentIndex++;
                continue;
            }

            if (lineWithoutComment.StartsWith("_"))
            {
                currentIndex++;
                continue;
            }

            lineWithoutComment = Regex.Replace(lineWithoutComment, @"\s+", " ");

            if (Regex.IsMatch(lineWithoutComment, @":\s*BOOL", RegexOptions.IgnoreCase))
            {
                var match = Regex.Match(lineWithoutComment, @"^(.*?):\s*BOOL\s*(?::=\s*(\d|TRUE|FALSE))?\s*;", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    currentIndex++;
                    continue;
                }

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
                    declaredVars.Add(cleanVarName);
                }
            }
            else if (Regex.IsMatch(lineWithoutComment, @":\s*TON\s*:=\s*\(PT\s*:=\s*T#\d+s\)\s*;", RegexOptions.IgnoreCase))
            {
                var match = Regex.Match(lineWithoutComment, @"^(\w+):\s*TON\s*:=\s*\(PT\s*:=\s*T#\d+s\)\s*;", RegexOptions.IgnoreCase);
                if (!match.Success)
                {
                    currentIndex++;
                    continue;
                }

                string varName = match.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(varName) || varName.StartsWith("_"))
                {
                    currentIndex++;
                    continue;
                }

                declarations.Add($"  {varName}:timer;{(!string.IsNullOrEmpty(comment) ? " " + comment : "")}");
                declaredVars.Add(varName);
            }

            currentIndex++;
        }
    }
    private static string ExtractComment(ref string line, string[] lines, ref int currentIndex)
    {
        string comment = "";
        int commentStart = line.IndexOf("(*");
        if (commentStart >= 0)
        {
            int commentEnd = line.IndexOf("*)", commentStart);
            if (commentEnd >= 0)
            {
                // Однострочный комментарий
                string rawComment = line.Substring(commentStart, commentEnd - commentStart + 2);
                comment = "/*" + rawComment.Substring(2, rawComment.Length - 4).Trim() + "*/";
                line = line.Remove(commentStart, rawComment.Length).Trim();
            }
            else
            {
                // Многострочный комментарий
                StringBuilder commentBuilder = new StringBuilder();
                string initialPart = line.Substring(commentStart);
                commentBuilder.Append(initialPart);
                line = line.Substring(0, commentStart).Trim();

                bool commentClosed = false;
                while (currentIndex + 1 < lines.Length && !commentClosed)
                {
                    currentIndex++;
                    string nextLine = lines[currentIndex].Trim();
                    commentBuilder.Append(" ").Append(nextLine);

                    if (nextLine.Contains("*)"))
                    {
                        commentClosed = true;
                        int endPos = commentBuilder.ToString().IndexOf("*)");
                        string fullComment = commentBuilder.ToString().Substring(0, endPos + 2);
                        comment = "/*" + fullComment.Substring(2, fullComment.Length - 4).Trim() + "*/";
                    }
                }
            }
        }
        else
        {
            // Проверяем однострочные комментарии //
            commentStart = line.IndexOf("//");
            if (commentStart >= 0)
            {
                comment = line.Substring(commentStart).Trim();
                line = line.Substring(0, commentStart).Trim();
            }
        }

        return comment;
    }

    private static void ValidateVariable(string varName, HashSet<string> declaredVars, int lineNumber)
    {
        if (!declaredVars.Contains(varName) && !varName.Contains("."))
            throw new FormatException($"Undeclared variable '{varName}' used at line {lineNumber}.");
    }

    private static void ValidateExpressionVariables(string expr, HashSet<string> declaredVars, int lineNumber)
    {
        var variables = Regex.Matches(expr, @"\b_?([a-zA-Z]\w*)(\.Q)?\b");
        foreach (Match match in variables)
        {
            string varName = match.Groups[1].Value;
            if (!new[] { "NOT", "AND", "OR", "TRUE", "FALSE" }.Contains(varName, StringComparer.OrdinalIgnoreCase))
            {
                if (!declaredVars.Contains(varName) && !match.Groups[2].Success)
                    throw new FormatException($"Undeclared variable '{varName}' used in expression at line {lineNumber}.");
            }
        }
    }

    private static string TransformCondition(string condition)
    {
        Console.WriteLine($"Original condition: {condition}");
        condition = Regex.Replace(condition, @"\bNOT\b", "~", RegexOptions.IgnoreCase);
        condition = Regex.Replace(condition, @"\bAND\b", " & ", RegexOptions.IgnoreCase);
        condition = Regex.Replace(condition, @"\bOR\b", " | ", RegexOptions.IgnoreCase);
        condition = Regex.Replace(condition, @"\s*([ ~ ])\s*", "$1");
        condition = Regex.Replace(condition, @"\s*([&|])\s*", " $1 ");

        var variables = Regex.Matches(condition, @"\b_?([a-zA-Z]\w*)(\.Q)?\b");
        foreach (Match match in variables)
        {
            string originalVar = match.Value;
            string varName = match.Groups[1].Value;

            Console.WriteLine($"Processing variable: original='{originalVar}', varName='{varName}'");

            if (new[] { "NOT", "AND", "OR", "TRUE", "FALSE", "next" }.Contains(varName, StringComparer.OrdinalIgnoreCase))
                continue;

            string replacement;
            if (originalVar.StartsWith("_"))
            {
                replacement = varName;
            }
            else
            {
                replacement = $"next({originalVar})";
            }

            Console.WriteLine($"Transformed to: replacement='{replacement}'");
            condition = condition.Replace(originalVar, replacement);
        }

        // Удаление _next, если присутствует
        condition = Regex.Replace(condition, @"_next\(([^)]+)\)", "$1");
        condition = Regex.Replace(condition, @"next\((\w+)\)(\d+)", "next($1$2)");


        if (condition.Contains("_next"))
            throw new FormatException($"Invalid pattern '_next' detected in transformed condition: {condition}");

        Console.WriteLine($"Transformed condition: {condition}");
        return Regex.Replace(condition, @"\s+", " ").Trim();
    }

    private static string TransformAssignment(string expr)
    {
        // Проверка баланса скобок
        if (expr.Count(c => c == '(') != expr.Count(c => c == ')'))
            throw new FormatException($"Mismatched parentheses in expression: {expr}");

        // Сначала обрабатываем переменные с суффиксами (например, cMove5)
        expr = Regex.Replace(expr, @"\b([a-zA-Z]\w*)(\d+)\b", match =>
        {
            string varName = match.Groups[1].Value;
            string suffix = match.Groups[2].Value;
            return $"{varName}{suffix}"; // Оставляем как есть, но можем добавить next() если нужно
        });

        // Затем обрабатываем обычные переменные
        var variables = Regex.Matches(expr, @"\b_?([a-zA-Z]\w*)(\.Q)?\b");
        foreach (Match match in variables)
        {
            string originalVar = match.Value;
            string varName = match.Groups[1].Value;

            if (new[] { "NOT", "AND", "OR", "TRUE", "FALSE", "next" }.Contains(varName, StringComparer.OrdinalIgnoreCase))
                continue;

            string replacement;
            if (originalVar.StartsWith("_"))
            {
                replacement = varName;
            }
            else
            {
                // Проверяем, не является ли это частью имени переменной с цифрой (например, cMove5)
                if (Regex.IsMatch(originalVar, @"^\w+\d+$"))
                {
                    replacement = $"next({originalVar})";
                }
                else
                {
                    replacement = $"next({originalVar})";
                }
            }

            expr = expr.Replace(originalVar, replacement);
        }

        // Замена логических операторов
        expr = Regex.Replace(expr, @"\bNOT\b", "~", RegexOptions.IgnoreCase);
        expr = Regex.Replace(expr, @"\bAND\b", " & ", RegexOptions.IgnoreCase);
        expr = Regex.Replace(expr, @"\bOR\b", " | ", RegexOptions.IgnoreCase);
        expr = Regex.Replace(expr, @"\s*([ ~ ])\s*", "$1");
        expr = Regex.Replace(expr, @"\s*([&|])\s*", " $1 ");

        // Удаление _next, если присутствует
        expr = Regex.Replace(expr, @"_next\(([^)]+)\)", "$1");

        if (expr.Contains("_next"))
            throw new FormatException($"Invalid pattern '_next' detected in transformed expression: {expr}");

        // Нормализация скобок
        expr = Regex.Replace(expr, @"\s*([\(\)])\s*", "$1");

        // Исправляем случаи типа next(cMove)5 -> next(cMove5)
        expr = Regex.Replace(expr, @"next\((\w+)\)(\d+)", "next($1$2)");

        return Regex.Replace(expr, @"\s+", " ").Trim();
    }
}