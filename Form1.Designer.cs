namespace SMV
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.globalText = new System.Windows.Forms.TextBox();
            this.stText = new System.Windows.Forms.TextBox();
            this.search = new System.Windows.Forms.Button();
            this.search2 = new System.Windows.Forms.Button();
            this.venom = new System.Windows.Forms.Button();
            this.search3 = new System.Windows.Forms.Button();
            this.textSMVST = new System.Windows.Forms.TextBox();
            this.button1 = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // globalText
            // 
            this.globalText.Location = new System.Drawing.Point(12, 61);
            this.globalText.Name = "globalText";
            this.globalText.Size = new System.Drawing.Size(498, 20);
            this.globalText.TabIndex = 0;
            // 
            // stText
            // 
            this.stText.Location = new System.Drawing.Point(12, 159);
            this.stText.Name = "stText";
            this.stText.Size = new System.Drawing.Size(498, 20);
            this.stText.TabIndex = 1;
            // 
            // search
            // 
            this.search.Location = new System.Drawing.Point(12, 102);
            this.search.Name = "search";
            this.search.Size = new System.Drawing.Size(75, 23);
            this.search.TabIndex = 2;
            this.search.Text = "Поиск";
            this.search.UseVisualStyleBackColor = true;
            this.search.Click += new System.EventHandler(this.search_Click);
            // 
            // search2
            // 
            this.search2.Location = new System.Drawing.Point(12, 218);
            this.search2.Name = "search2";
            this.search2.Size = new System.Drawing.Size(75, 23);
            this.search2.TabIndex = 4;
            this.search2.Text = "Поиск";
            this.search2.UseVisualStyleBackColor = true;
            this.search2.Click += new System.EventHandler(this.search2_Click);
            // 
            // venom
            // 
            this.venom.Location = new System.Drawing.Point(199, 218);
            this.venom.Name = "venom";
            this.venom.Size = new System.Drawing.Size(75, 23);
            this.venom.TabIndex = 6;
            this.venom.Text = "Соединить";
            this.venom.UseVisualStyleBackColor = true;
            this.venom.Click += new System.EventHandler(this.venom_Click);
            // 
            // search3
            // 
            this.search3.Location = new System.Drawing.Point(12, 361);
            this.search3.Name = "search3";
            this.search3.Size = new System.Drawing.Size(75, 23);
            this.search3.TabIndex = 8;
            this.search3.Text = "Поиск";
            this.search3.UseVisualStyleBackColor = true;
            this.search3.Click += new System.EventHandler(this.search3_Click);
            // 
            // textSMVST
            // 
            this.textSMVST.Location = new System.Drawing.Point(12, 302);
            this.textSMVST.Name = "textSMVST";
            this.textSMVST.Size = new System.Drawing.Size(498, 20);
            this.textSMVST.TabIndex = 7;
            // 
            // button1
            // 
            this.button1.Location = new System.Drawing.Point(199, 361);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(75, 23);
            this.button1.TabIndex = 9;
            this.button1.Text = "Принять";
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.ok3_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(215, 272);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(59, 13);
            this.label1.TabIndex = 10;
            this.label1.Text = "SMV -> ST";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(215, 22);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(56, 13);
            this.label2.TabIndex = 11;
            this.label2.Text = "ST-> SMV";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(9, 45);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(69, 13);
            this.label3.TabIndex = 12;
            this.label3.Text = "GLOBAL_ST";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(15, 143);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(21, 13);
            this.label4.TabIndex = 13;
            this.label4.Text = "ST";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(570, 459);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.button1);
            this.Controls.Add(this.search3);
            this.Controls.Add(this.textSMVST);
            this.Controls.Add(this.venom);
            this.Controls.Add(this.search2);
            this.Controls.Add(this.search);
            this.Controls.Add(this.stText);
            this.Controls.Add(this.globalText);
            this.Name = "Form1";
            this.Text = "SMV v0.4";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox globalText;
        private System.Windows.Forms.TextBox stText;
        private System.Windows.Forms.Button search;
        private System.Windows.Forms.Button search2;
        private System.Windows.Forms.Button venom;
        private System.Windows.Forms.Button Ok3;
        private System.Windows.Forms.Button search3;
        private System.Windows.Forms.TextBox textSMVST;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
    }
}

