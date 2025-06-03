using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SMV
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void search_Click(object sender, EventArgs e)
        {
            ShowFileDialog(globalText, "EXP files (*.exp)|*.exp");
        }

        private void ok_Click(object sender, EventArgs e)
        {
            ShowFileContent(globalText.Text);
        }

        private void search2_Click(object sender, EventArgs e)
        {
            ShowFileDialog(stText, "EXP files (*.exp)|*.exp");
        }

        private void ok2_Click(object sender, EventArgs e)
        {
            ShowFileContent(stText.Text);
        }

        private void venom_Click(object sender, EventArgs e)
        {
            try
            {
                string combinedText = FileContentCombiner.CombineVarSections(globalText.Text, stText.Text);

                using (var saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.Filter = "Text files (*.txt)|*.txt|EXP files (*.exp)|*.exp";
                    saveFileDialog.DefaultExt = "txt";
                    saveFileDialog.AddExtension = true;
                    saveFileDialog.RestoreDirectory = true;

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        File.WriteAllText(saveFileDialog.FileName, combinedText, Encoding.GetEncoding(1251));
                    }
                    else
                    {
                        MessageBox.Show(combinedText, "Результат обработки", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }

                MessageBox.Show(combinedText, "Результат обработки", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowFileDialog(TextBox textBox, string filter)
        {
            using (var openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = filter;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    textBox.Text = openFileDialog.FileName;
                }
            }
        }

        private void ShowFileContent(string filePath)
        {
            try
            {
                if (File.Exists(filePath) &&
                    (Path.GetExtension(filePath).ToLower() == ".exp" ||
                     Path.GetExtension(filePath).ToLower() == ".smv" ||
                     Path.GetExtension(filePath).ToLower() == ".txt"))
                {
                    string[] content = File.ReadAllLines(filePath, Encoding.GetEncoding(1251));
                    string contentText = string.Join(Environment.NewLine, content);
                    MessageBox.Show(contentText, "Содержимое файла", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Пожалуйста, выберите действительный файл (.exp, .smv или .txt)", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при чтении файла: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void search3_Click(object sender, EventArgs e)
        {
            ShowFileDialog(textSMVST, "SMV or Text files (*.smv;*.txt)|*.smv;*.txt");
        }

        private void ok3_Click(object sender, EventArgs e)
        {
            try
            {
                string filePath = textSMVST.Text;
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath) ||
                    (Path.GetExtension(filePath).ToLower() != ".smv" && Path.GetExtension(filePath).ToLower() != ".txt"))
                {
                    MessageBox.Show("Пожалуйста, выберите действительный файл (.smv или .txt)", "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                // Call the ConvertCaseToIfStatements method from SmvToStConverter
                SmvToStConverter.ConvertCaseToIfStatements(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при преобразовании файла: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}