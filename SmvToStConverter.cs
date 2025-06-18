using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;

public static class SmvToStConverter
{
    public static string ConvertCaseToIfStatements(string filePath)
    {
        try
        {
            // Читаем содержимое SMV-файла с кодировкой Windows-1251
            string smvContent = File.ReadAllText(filePath, Encoding.GetEncoding(1251));

            // StringBuilder для хранения результата
            StringBuilder output = new StringBuilder();

            // Переменная для отслеживания последнего индекса
            int lastIndex = 0;

            // Добавляем заголовок
            output.AppendLine("(* @NESTEDCOMMENTS := 'Yes' *)");
            output.AppendLine("(* @PATH := '' *)");
            output.AppendLine("(* @OBJECTFLAGS := '0, 8' *)");
            output.AppendLine("(* @SYMFILEFLAGS := '2048' *)");
            output.AppendLine("PROGRAM PLC_PRG");
            output.AppendLine("VAR");

            // Словарь для хранения переменных и их типов
            Dictionary<string, string> initVars = new Dictionary<string, string>();
            string lastComment = "";

            // Поиск всех объявлений переменных (включая timer) в SMV-файле
            string varPattern = @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(timer|0..1|0|1)\s*;";
            MatchCollection varMatches = Regex.Matches(smvContent, varPattern, RegexOptions.Multiline);

            foreach (Match match in varMatches)
            {
                // Ищем комментарий перед текущей строкой
                int commentStart = smvContent.LastIndexOf("/*", match.Index, match.Index - lastIndex) + 2;
                if (commentStart > 1 && commentStart < match.Index)
                {
                    int commentEnd = smvContent.IndexOf("*/", commentStart);
                    if (commentEnd > commentStart && commentEnd < match.Index)
                    {
                        lastComment = smvContent.Substring(commentStart, commentEnd - commentStart).Trim();
           
                    }
                }

                string variable = match.Groups[1].Value.Trim();
                string type = match.Groups[2].Value.Trim();

                // Определяем тип переменной
                if (type == "timer")
                {
                    initVars[variable] = "TON := (PT := T#0s)";
                }
                else if (type == "0..1")
                {
                    initVars[variable] = "BOOL";
                }
                else if (type == "0")
                {
                    initVars[variable] = "BOOL := FALSE";
                }
                else if (type == "1")
                {
                    initVars[variable] = "BOOL := TRUE";
                }
                else
                {
                    MessageBox.Show($"Неизвестный тип для переменной {variable}: {type}. Переменная будет пропущена.",
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }

                // Добавляем комментарий, если он есть
                if (!string.IsNullOrEmpty(lastComment))
                {
                    output.AppendLine($"    (*{lastComment}*)");
                }

                output.AppendLine($"    {variable} : {initVars[variable]};");
                lastIndex = match.Index + match.Length;
            }

            // Находим последнее вхождение next(...):={0,1};
            string nextPattern = @"next\([^)]+\):=\{0,1\};";
            MatchCollection nextMatches = Regex.Matches(smvContent, nextPattern);
            if (nextMatches.Count == 0)
            {
                MessageBox.Show("В файле не найдено ни одного next(...):={0,1};", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            int startIndex = nextMatches[nextMatches.Count - 1].Index + nextMatches[nextMatches.Count - 1].Length;

            // Находим последнее вхождение .I:=...;
            string inPattern = @"\w+\.I:=.+?;";
            MatchCollection inMatches = Regex.Matches(smvContent, inPattern);
            if (inMatches.Count == 0)
            {
                MessageBox.Show("В файле не найдено ни одного .I:=...;", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            int endIndex = inMatches[inMatches.Count - 1].Index + inMatches[inMatches.Count - 1].Length;

            // Извлекаем содержимое между последним next(...):={0,1}; и последним .I:=...;
            string relevantContent = smvContent.Substring(startIndex, endIndex - startIndex);

            // Множество для хранения переменных (без дубликатов)
            HashSet<string> variables = new HashSet<string>();

            // Регулярное выражение для поиска выражений (case и одиночные присваивания)
            string combinedPattern = @"(case\s*\{[\s\S]*?\}\s*;|(?:next\([^)]+\)|[^:=\s]+\.I):=[^;]+;)";
            MatchCollection matches = Regex.Matches(relevantContent, combinedPattern, RegexOptions.Multiline);

            // Первый проход: сбор всех переменных
            foreach (Match match in matches)
            {
                string expression = match.Value;

                if (expression.Trim().StartsWith("case"))
                {
                    string caseContent = Regex.Match(expression, @"case\s*\{([\s\S]*?)\}\s*;").Groups[1].Value;
                    CollectVariablesFromCase(caseContent, variables);
                }
                else
                {
                    // Это одиночное присваивание
                    if (!expression.Contains(".I"))
                    {
                        string[] parts = expression.Split(new[] { ":=" }, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            string left = parts[0].Replace("next(", "").Replace(")", "").Trim();
                            variables.Add(left);
                        }
                    }
                }
            }

            // Добавляем переменные с префиксом _ в секцию VAR
            output.AppendLine("    (* Переменные с префиксом _ *)");
            foreach (string variable in variables)
            {
                output.AppendLine($"    _{variable} : BOOL;");
            }

            // Завершаем секцию VAR
            output.AppendLine("END_VAR");
            output.AppendLine("(* @END_DECLARATION := '0' *)");

            // Второй проход: генерация кода
            lastIndex = 0;
            foreach (Match match in matches)
            {
                // Извлекаем текст перед текущим выражением (комментарии)
                int expressionStart = match.Index;
                if (expressionStart > lastIndex)
                {
                    string commentSection = relevantContent.Substring(lastIndex, expressionStart - lastIndex).Trim();
                    if (!string.IsNullOrWhiteSpace(commentSection))
                    {
                        commentSection=commentSection.Replace("/*", "(*").Replace("*/", "*)");
                        output.AppendLine(commentSection);
                    }
                }

                string expression = match.Value;

                if (expression.Trim().StartsWith("case"))
                {
                    string caseContent = Regex.Match(expression, @"case\s*\{([\s\S]*?)\}\s*;").Groups[1].Value;
                    string converted = ConvertCaseBlockToIf(caseContent);
                    output.AppendLine(converted);
                }
                else
                {
                    string convertedAssignment = TransformAssignment(expression);
                    output.AppendLine(convertedAssignment);
                }

                lastIndex = match.Index + match.Length;
            }

            // Добавляем оставшиеся комментарии после последнего выражения
            if (lastIndex < relevantContent.Length)
            {
                string finalComments = relevantContent.Substring(lastIndex).Trim();
                if (!string.IsNullOrWhiteSpace(finalComments))
                {
                    output.AppendLine(finalComments);
                }
            }

            // Добавляем строки вида _(имя) := имя; для каждой переменной
            output.AppendLine();
            output.AppendLine("(* Добавленные переменные *)");
            foreach (string variable in variables)
            {
                output.AppendLine($"_{variable} := {variable};");
            }

            // Добавляем конец программы
            output.AppendLine("END_PROGRAM");

            // Показываем результат преобразования в MessageBox
            MessageBox.Show(output.ToString(), "Преобразованные IF-блоки и присваивания",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            // Показываем SaveFileDialog для сохранения результата
            using (var saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "Text files (*.txt)|*.txt|EXP files (*.exp)|*.exp";
                saveFileDialog.DefaultExt = "exp";
                saveFileDialog.AddExtension = true;
                saveFileDialog.RestoreDirectory = true;

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    File.WriteAllText(saveFileDialog.FileName, output.ToString(), Encoding.GetEncoding(1251));
                    MessageBox.Show("Результат успешно сохранен в файл!", "Успех",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }

            return output.ToString();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при обработке файла: {ex.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
    }

    private static void CollectVariablesFromCase(string caseContent, HashSet<string> variables)
    {
        string[] lines = caseContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(line => line.Trim())
                                   .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("default"))
                                   .ToArray();

        foreach (string line in lines)
        {
            string cleanedLine = line.TrimEnd(';').Trim();
            int colonIndex = cleanedLine.IndexOf(':');
            if (colonIndex == -1) continue;

            string assignment = cleanedLine.Substring(colonIndex + 1).Trim();
            string[] assignmentParts = assignment.Split(new[] { ":=" }, StringSplitOptions.None);
            if (assignmentParts.Length == 2)
            {
                string left = assignmentParts[0].Replace("next(", "").Replace(")", "").Trim();
                variables.Add(left);
            }
        }
    }

    private static string ConvertCaseBlockToIf(string caseContent)
    {
        string[] lines = caseContent.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(line => line.Trim())
                                   .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("default"))
                                   .Take(2) // Берем только первые две строки
                                   .ToArray();

        StringBuilder ifBuilder = new StringBuilder();
        bool isFirstCondition = true;

        foreach (string line in lines)
        {
            string cleanedLine = line.TrimEnd(';').Trim();
            int colonIndex = cleanedLine.IndexOf(':');
            if (colonIndex == -1) continue;

            string condition = cleanedLine.Substring(0, colonIndex).Trim();
            string assignment = cleanedLine.Substring(colonIndex + 1).Trim();

            condition = TransformCondition(condition);
            string transformedAssignment = TransformAssignment(assignment);

            if (isFirstCondition)
            {
                ifBuilder.AppendLine($"IF {condition} THEN {transformedAssignment}");
                isFirstCondition = false;
            }
            else
            {
                ifBuilder.AppendLine($"ELSIF {condition} THEN {transformedAssignment}");
            }
        }

        ifBuilder.AppendLine("END_IF;");
        return ifBuilder.ToString();
    }

    private static string TransformCondition(string condition)
    {
        // 1. Временная замена next() переменных
        var nextVars = new List<string>();
        condition = Regex.Replace(condition, @"next\s*\((.*?)\)", m =>
        {
            nextVars.Add(m.Groups[1].Value);
            return $"[NEXT_VAR_{nextVars.Count - 1}]";
        });

        // 2. Замена операторов
        condition = condition
            .Replace("&", " AND ")
            .Replace("|", " OR ")
            .Replace("~", " NOT ")
            .Replace(".I", ".IN");

        // 3. Добавление _ к обычным переменным (не next, не поля объектов)
        condition = Regex.Replace(
            condition,
            @"\b(?!NOT\b|AND\b|OR\b|\[NEXT_VAR_\d+\])([A-Za-z][A-Za-z0-9]*)(?![\.\(])",
            "_$1"
        );

        // 4. Восстановление next() переменных (без _)
        for (int i = 0; i < nextVars.Count; i++)
        {
            condition = condition.Replace($"NEXT_VAR_{i}", nextVars[i]);
        }
        condition = condition
       .Replace("[_", "")
       .Replace("]", "");
        // 5. Балансировка скобок

        return condition;
    }

    private static string TransformAssignment(string assignment)
    {
        assignment = assignment.TrimEnd(';').Trim();
        string[] parts = assignment.Split(new[] { ":=" }, StringSplitOptions.None);
        if (parts.Length != 2) return assignment;

        // Обработка левой части (next(var) → var)
        string left = parts[0].Trim();
        if (left.StartsWith("next(") && left.EndsWith(")"))
        {
            left = left.Substring(5, left.Length - 6);
        }

        // Обработка правой части
        string right = TransformCondition(parts[1].Trim());

        return $"{left} := {right};";
    }
}