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

            // Извлекаем и обрабатываем секцию init
            string initPattern = @"init\(([^)]+)\):=(.*?);";
            MatchCollection initMatches = Regex.Matches(smvContent, initPattern, RegexOptions.Multiline);
            Dictionary<string, string> initVars = new Dictionary<string, string>();
            string lastComment = "";

            foreach (Match match in initMatches)
            {
                string fullMatch = match.Value;
                string variable = match.Groups[1].Value.Trim();
                string value = match.Groups[2].Value.Trim();

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

                // Сохраняем переменную и тип с начальным значением
                if (value == "0..1")
                {
                    initVars[variable] = "BOOL";
                }
                else if (value == "timer")
                {
                    initVars[variable] = "TON := (PT := T#0s)";
                }
                else if (value == "0")
                {
                    initVars[variable] = "BOOL := FALSE";
                }
                else if (value == "1")
                {
                    initVars[variable] = "BOOL := TRUE";
                }
                else
                {
                    // Если тип неизвестен, выводим сообщение об ошибке и пропускаем переменную
                    MessageBox.Show($"Неизвестный тип для переменной {variable}: {value}. Ожидается '0..1', 'timer', '0' или '1'. Переменная будет пропущена.",
                        "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }

                // Добавляем комментарий, если он есть
                if (!string.IsNullOrEmpty(lastComment))
                {
                    output.AppendLine($"    (*{lastComment}*)");
                }

                output.AppendLine($"    {variable} : {initVars[variable]};");
                lastIndex = match.Index + fullMatch.Length;
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

            // Находим последнее вхождение .IN:=...;
            string inPattern = @"\w+\.I:=.+?;";
            MatchCollection inMatches = Regex.Matches(smvContent, inPattern);
            if (inMatches.Count == 0)
            {
                MessageBox.Show("В файле не найдено ни одного .I:=...;", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            int endIndex = inMatches[inMatches.Count - 1].Index + inMatches[inMatches.Count - 1].Length;

            // Извлекаем содержимое между последним next(...):={0,1}; и последним .IN:=...;
            string relevantContent = smvContent.Substring(startIndex, endIndex - startIndex);

            // Множество для хранения переменных (без дубликатов)
            HashSet<string> variables = new HashSet<string>();

            // Регулярное выражение для поиска выражений (case и одиночные присваивания)
            string combinedPattern = @"(case\s*\{[\s\S]*?\}\s*;|(?:next\([^)]+\)|[^:=\s]+\.IN):=[^;]+;)";
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
                    if (!expression.Contains(".IN"))
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
        condition = condition.Replace("&", " AND ")
                            .Replace("|", " OR ")
                            .Replace("~", "NOT ")
                            .Replace("next(", "_")
                            .Replace(")", "");

        condition = Regex.Replace(condition, @"\s+", " ").Trim();

        int openParens = condition.Count(c => c == '(');
        int closeParens = condition.Count(c => c == ')');
        if (openParens > closeParens)
        {
            condition += new string(')', openParens - closeParens);
        }
        else if (closeParens > openParens)
        {
            condition = new string('(', closeParens - openParens) + condition;
        }

        return condition;
    }

    private static string TransformAssignment(string assignment)
    {
        assignment = assignment.TrimEnd(';').Trim();
        string[] parts = assignment.Split(new[] { ":=" }, StringSplitOptions.None);
        if (parts.Length != 2) return assignment;

        string left = parts[0].Trim().Replace("next(", "").Replace(")", "");
        string right = parts[1].Trim().Replace("next(", "").Replace(")", "")
                              .Replace("&", " AND ")
                              .Replace("|", " OR ")
                              .Replace("~", "NOT ")
                              .Replace(".I", ".IN"); // Заменяем .I на .IN
        right = Regex.Replace(right, @"\s+", " ").Trim();

        int openParens = right.Count(c => c == '(');
        int closeParens = right.Count(c => c == ')');
        if (openParens > closeParens)
        {
            right += new string(')', openParens - closeParens);
        }
        else if (closeParens > openParens)
        {
            right = new string('(', closeParens - openParens) + right;
        }

        return $"{left}:={right};";
    }
}