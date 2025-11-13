using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace IconPopupApp
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                var arguments = ParseArguments(args);
                if (arguments == null) return;

                var config = LoadConfiguration(arguments.ConfigFileName);
                Application.Run(new IconPopup(arguments.FolderPath, config, arguments.FileExtension));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore critico: {ex.Message}", "Errore",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static CommandLineArguments ParseArguments(string[] args)
        {
            if (args.Length == 0)
            {
                ShowHelp();
                return null;
            }

            var arguments = new CommandLineArguments();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "-folder" when i + 1 < args.Length:
                        arguments.FolderPath = args[++i].Trim('"');
                        break;
                    case "-config" when i + 1 < args.Length:
                        arguments.ConfigFileName = args[++i].Trim('"');
                        break;
                    case "-ext" when i + 1 < args.Length:
                        arguments.FileExtension = args[++i].Trim('"');
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(arguments.FolderPath))
            {
                MessageBox.Show("Specifica una cartella valida.", "Errore",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            if (!Directory.Exists(arguments.FolderPath))
            {
                MessageBox.Show($"Cartella '{arguments.FolderPath}' non trovata.", "Errore",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            return arguments;
        }

        private static void ShowHelp()
        {
            const string helpText = @"IconPopup - Visualizzatore file con icone

Utilizzo:
  IconPopup.exe -folder <percorso> [-config <file>] [-ext <estensione>]

Parametri:
  -folder   Cartella contenente i file (obbligatorio)
  -config   File di configurazione JSON (opzionale)
  -ext      Filtra per estensione file (opzionale)

Esempi:
  IconPopup.exe -folder ""C:\MyFolder""
  IconPopup.exe -folder ""C:\MyFolder"" -config settings.json -ext .exe";

            MessageBox.Show(helpText, "Aiuto", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static IconPopupConfig LoadConfiguration(string configFileName)
        {
            var defaultConfig = new IconPopupConfig();

            if (string.IsNullOrWhiteSpace(configFileName))
                return defaultConfig;

            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName);
                if (!File.Exists(configPath))
                    return defaultConfig;

                string configJson = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<IconPopupConfig>(configJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                });

                return config ?? defaultConfig;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel caricamento configurazione: {ex.Message}\nVerranno usate le impostazioni predefinite.",
                    "Avviso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return defaultConfig;
            }
        }
    }

    public class CommandLineArguments
    {
        public string FolderPath { get; set; } = string.Empty;
        public string ConfigFileName { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
    }

    public class IconPopupConfig
    {
        public int CornerRadius { get; set; } = 15;
        public int AnimationSpeed { get; set; } = 15;
        public int MaxHeight { get; set; } = 600;
        public int IconSize { get; set; } = 32;
        public int ButtonWidth { get; set; } = 180;
        public int ButtonHeight { get; set; } = 42;
        public bool EnableAnimations { get; set; } = true;
        public bool ShowFileExtensions { get; set; } = false;
        public bool EnableGlassEffect { get; set; } = true;
        public int GlassBlurAmount { get; set; } = 10;
        public int BorderThickness { get; set; } = 1;
        public string BorderColorHex { get; set; } = "#404040";

        private string _textColorHex = "#000000";
        public string TextColorHex
        {
            get => _textColorHex;
            set
            {
                _textColorHex = value;
                TextColor = ParseColor(value, Color.Black);
            }
        }

        private string _backgroundColorHex = "#F0F0F0";
        public string BackgroundColorHex
        {
            get => _backgroundColorHex;
            set
            {
                _backgroundColorHex = value;
                BackgroundColor = ParseColor(value, SystemColors.Control);
            }
        }

        private string _panelColorHex = "#FFFFFF";
        public string PanelColorHex
        {
            get => _panelColorHex;
            set
            {
                _panelColorHex = value;
                PanelColor = ParseColor(value, SystemColors.Window);
            }
        }

        public Color TextColor { get; private set; } = Color.Black;
        public Color BackgroundColor { get; private set; } = SystemColors.Control;
        public Color PanelColor { get; private set; } = SystemColors.Window;

        private static Color ParseColor(string hexValue, Color defaultColor)
        {
            if (string.IsNullOrWhiteSpace(hexValue))
                return defaultColor;

            try
            {
                return ColorTranslator.FromHtml(hexValue);
            }
            catch
            {
                return defaultColor;
            }
        }
    }

    public class IconPopup : Form
    {
        private readonly IconPopupConfig _config;
        private readonly string _folderPath;
        private readonly string _fileExtension;
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private CancellationTokenSource _animationCancellation;

        private const int SHADOW_SIZE = 5;
        private const int MAX_FILENAME_LENGTH = 25;

        public IconPopup(string folderPath, IconPopupConfig config, string fileExtension)
        {
            _folderPath = folderPath;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _fileExtension = NormalizeFileExtension(fileExtension);

            InitializeForm();
            CreateUI();
            PositionForm();
        }

        private void InitializeForm()
        {
            SuspendLayout();

            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = _config.BackgroundColor;
            Padding = new Padding(0); // Rimuovi padding per evitare bordi bianchi
            TopMost = true;

            // Imposta il colore di sfondo immediatamente per evitare flash bianchi
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            UpdateStyles();

            if (_config.EnableAnimations)
                Opacity = 0;

            // Eventi
            Load += OnFormLoad;
            Deactivate += OnFormDeactivate;
            KeyDown += OnKeyDown;

            ResumeLayout(false);
        }

        private void CreateUI()
        {
            var files = GetFilteredFiles();
            if (!files.Any())
            {
                CreateEmptyStateUI();
                return;
            }

            var panel = CreateMainPanel();
            CreateFileButtons(panel, files);

            Controls.Add(panel);
            AdjustFormSize(panel);
        }

        private string[] GetFilteredFiles()
        {
            try
            {
                string searchPattern = string.IsNullOrEmpty(_fileExtension) ? "*" : $"*{_fileExtension}";
                return Directory.GetFiles(_folderPath, searchPattern)
                    .Where(file => !Path.GetFileName(file).StartsWith("."))
                    .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nel leggere i file: {ex.Message}", "Errore",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return Array.Empty<string>();
            }
        }

        private FlowLayoutPanel CreateMainPanel()
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(8),
                BackColor = _config.PanelColor,
                BorderStyle = BorderStyle.None
            };

            return panel;
        }

        private void CreateFileButtons(FlowLayoutPanel panel, string[] files)
        {
            foreach (string file in files)
            {
                try
                {
                    var button = CreateFileButton(file);
                    panel.Controls.Add(button);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Errore nella creazione del pulsante per {file}: {ex.Message}");
                }
            }
        }

        private Button CreateFileButton(string filePath)
        {
            string fileName = _config.ShowFileExtensions
                ? Path.GetFileName(filePath)
                : Path.GetFileNameWithoutExtension(filePath);

            fileName = TruncateFileName(fileName);

            var icon = ExtractFileIcon(filePath);
            _disposables.Add(icon);

            var button = new Button
            {
                Size = new Size(_config.ButtonWidth, _config.ButtonHeight),
                Text = fileName,
                Image = icon,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 5, 0), // Ridotto padding sinistro per eliminare spazio extra
                FlatStyle = FlatStyle.Flat,
                BackColor = _config.PanelColor,
                ForeColor = _config.TextColor,
                Tag = filePath,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(_config.PanelColor, 0.1f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(_config.PanelColor, 0.1f);

            button.Click += OnFileButtonClick;
            button.MouseEnter += (s, e) => button.BackColor = button.FlatAppearance.MouseOverBackColor;
            button.MouseLeave += (s, e) => button.BackColor = _config.PanelColor;

            return button;
        }

        private Bitmap ExtractFileIcon(string filePath)
        {
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(filePath);
                if (icon != null)
                {
                    return new Bitmap(icon.ToBitmap(), new Size(_config.IconSize, _config.IconSize));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore estrazione icona per {filePath}: {ex.Message}");
            }

            // Icona di fallback
            using var defaultIcon = SystemIcons.Application;
            return new Bitmap(defaultIcon.ToBitmap(), new Size(_config.IconSize, _config.IconSize));
        }

        private string TruncateFileName(string fileName)
        {
            if (fileName.Length <= MAX_FILENAME_LENGTH)
                return fileName;

            return fileName.Substring(0, MAX_FILENAME_LENGTH - 3) + "...";
        }

        private void CreateEmptyStateUI()
        {
            var label = new Label
            {
                Text = "Nessun file trovato",
                ForeColor = _config.TextColor,
                BackColor = _config.PanelColor,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(200, 50),
                Padding = new Padding(10)
            };

            Controls.Add(label);
            Size = new Size(label.Width + Padding.Horizontal, label.Height + Padding.Vertical);
        }

        private void AdjustFormSize(Control panel)
        {
            Size = new Size(
                panel.PreferredSize.Width,
                Math.Min(panel.PreferredSize.Height, _config.MaxHeight)
            );

            if (panel.PreferredSize.Height > _config.MaxHeight)
            {
                if (panel is FlowLayoutPanel flowPanel)
                {
                    flowPanel.AutoScroll = true;
                }
            }

            // Applica gli angoli arrotondati dopo che il form è completamente inizializzato
            if (_config.CornerRadius > 0)
            {
                // Aspetta che il form sia completamente caricato prima di applicare gli angoli arrotondati
                Load += (s, e) => 
                {
                    if (Width > 0 && Height > 0)
                    {
                        SetRoundedRegion();
                    }
                };
            }
        }

        private void PositionForm()
        {
            Point mousePos = Cursor.Position;
            Screen screen = Screen.FromPoint(mousePos);
            Rectangle workingArea = screen.WorkingArea;

            int x = Math.Max(workingArea.Left,
                Math.Min(mousePos.X - Width / 2, workingArea.Right - Width));

            int y = Math.Max(workingArea.Top, workingArea.Bottom - Height - 10);

            Location = new Point(x, y);
        }

        private void SetRoundedRegion()
        {
            if (_config.CornerRadius <= 0 || Width <= 0 || Height <= 0)
                return;

            try
            {
                using var path = new GraphicsPath();
                int radius = Math.Min(_config.CornerRadius, Math.Min(Width, Height) / 2);

                // Crea un percorso arrotondato più visibile e preciso
                path.AddArc(0, 0, radius * 2, radius * 2, 180, 90); // Alto sinistra
                path.AddArc(Width - radius * 2, 0, radius * 2, radius * 2, 270, 90); // Alto destra
                path.AddArc(Width - radius * 2, Height - radius * 2, radius * 2, radius * 2, 0, 90); // Basso destra
                path.AddArc(0, Height - radius * 2, radius * 2, radius * 2, 90, 90); // Basso sinistra
                path.CloseFigure();

                // Applica la regione arrotondata
                Region = new Region(path);
                
                // Forza il ridisegno per mostrare gli angoli arrotondati
                Invalidate();
                Update();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore nell'applicare gli angoli arrotondati: {ex.Message}");
                // Fallback: usa una regione rettangolare
                Region = new Region(new Rectangle(0, 0, Width, Height));
            }
        }

        private static string NormalizeFileExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return string.Empty;

            return extension.StartsWith(".") ? extension : "." + extension;
        }

        private async void OnFormLoad(object sender, EventArgs e)
        {
            if (_config.EnableAnimations)
            {
                _animationCancellation = new CancellationTokenSource();
                await AnimateOpen(_animationCancellation.Token);
            }
        }

        private async Task AnimateOpen(CancellationToken cancellationToken)
        {
            const int steps = 10;

            try
            {
                for (int i = 0; i <= steps && !cancellationToken.IsCancellationRequested; i++)
                {
                    Opacity = (double)i / steps;
                    await Task.Delay(_config.AnimationSpeed, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Animazione cancellata
            }
        }

        private void OnFileButtonClick(object sender, EventArgs e)
        {
            if (sender is Button button && button.Tag is string filePath)
            {
                try
                {
                    var startInfo = new ProcessStartInfo(filePath)
                    {
                        UseShellExecute = true,
                        Verb = "open"
                    };

                    Process.Start(startInfo);
                    Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Impossibile aprire il file:\n{ex.Message}", "Errore",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Close();
        }

        private void OnFormDeactivate(object sender, EventArgs e)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(Close));
            }
            else
            {
                Close();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _animationCancellation?.Cancel();
            _animationCancellation?.Dispose();

            foreach (var disposable in _disposables)
            {
                disposable?.Dispose();
            }
            _disposables.Clear();

            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            
            if (_config.EnableGlassEffect && _config.CornerRadius > 0)
            {
                DrawModernEffect(e.Graphics);
            }
        }

        private void DrawModernEffect(Graphics g)
        {
            if (Width <= 0 || Height <= 0) return;

            // Crea un percorso per gli angoli arrotondati
            using var path = new GraphicsPath();
            int radius = Math.Min(_config.CornerRadius, Math.Min(Width, Height) / 2);
            
            // Assicurati che tutti gli angoli siano arrotondati correttamente
            path.AddArc(0, 0, radius * 2, radius * 2, 180, 90); // Alto sinistra
            path.AddArc(Width - radius * 2, 0, radius * 2, radius * 2, 270, 90); // Alto destra
            path.AddArc(Width - radius * 2, Height - radius * 2, radius * 2, radius * 2, 0, 90); // Basso destra
            path.AddArc(0, Height - radius * 2, radius * 2, radius * 2, 90, 90); // Basso sinistra
            path.CloseFigure();

            // Disegna un'ombra sofisticata
            DrawShadowEffect(g, path, radius);

            // Disegna un bordo sottile e moderno
            if (_config.BorderThickness > 0)
            {
                var borderColor = ColorTranslator.FromHtml(_config.BorderColorHex);
                using var borderPen = new Pen(borderColor, _config.BorderThickness);
                g.DrawPath(borderPen, path);
            }

            // Aggiungi un effetto di gradiente moderno
            DrawModernGradient(g, path);
        }

        private void DrawShadowEffect(Graphics g, GraphicsPath path, int radius)
        {
            // Crea un'ombra morbida attorno al pannello
            for (int i = 0; i < 3; i++)
            {
                using var shadowPath = new GraphicsPath();
                int offset = i + 1;
                
                shadowPath.AddArc(offset, offset, radius * 2, radius * 2, 180, 90);
                shadowPath.AddArc(Width - radius * 2 - offset, offset, radius * 2, radius * 2, 270, 90);
                shadowPath.AddArc(Width - radius * 2 - offset, Height - radius * 2 - offset, radius * 2, radius * 2, 0, 90);
                shadowPath.AddArc(offset, Height - radius * 2 - offset, radius * 2, radius * 2, 90, 90);
                shadowPath.CloseFigure();

                using var shadowBrush = new SolidBrush(Color.FromArgb(20 - i * 5, Color.Black));
                g.FillPath(shadowBrush, shadowPath);
            }
        }

        private void DrawModernGradient(Graphics g, GraphicsPath path)
        {
            // Gradiente moderno che crea profondità
            using var gradientBrush = new LinearGradientBrush(
                new Rectangle(0, 0, Width, Height),
                Color.FromArgb(250, _config.PanelColor),
                ControlPaint.Light(_config.PanelColor, 0.05f),
                LinearGradientMode.Vertical);
            
            g.FillPath(gradientBrush, path);

            // Aggiungi un leggero riflesso in alto per modernità
            using var highlightBrush = new LinearGradientBrush(
                new Rectangle(0, 0, Width, Height / 4),
                Color.FromArgb(40, Color.White),
                Color.Transparent,
                LinearGradientMode.Vertical);
            
            g.FillPath(highlightBrush, path);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED per trasparenza
                return cp;
            }
        }
    }
}
