using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
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

    /// <summary>
    /// Cache delle icone basato su file system con hash del contenuto della cartella.
    /// La cache si invalida automaticamente quando i file nella cartella cambiano.
    /// </summary>
    public static class IconCache
    {
        private static readonly ConcurrentDictionary<string, Bitmap> _memoryCache = new();
        private static readonly ConcurrentDictionary<string, CacheEntry> _metadata = new();
        private static readonly object _lock = new();
        private static readonly string _cacheDir;

        static IconCache()
        {
            string appName = "QuickLaunch";
            _cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName, "IconCache");

            if (!Directory.Exists(_cacheDir))
                Directory.CreateDirectory(_cacheDir);
        }

        private record CacheEntry(string Hash, long LastWriteTime);

        /// <summary>
        /// Genera un hash univoco basato sul percorso cartella e metadati dei file.
        /// L'hash si modifica quando un file viene aggiunto, rimosso o modificato.
        /// </summary>
        internal static string ComputeFolderHash(string folderPath, string[] files, string extension)
        {
            using var sha256 = SHA256.Create();
            var ms = new MemoryStream();
            using var sw = new StreamWriter(ms);

            sw.WriteLine(folderPath);
            sw.WriteLine(extension);
            sw.WriteLine(files.Length);

            foreach (var f in files)
            {
                try
                {
                    var fi = new FileInfo(f);
                    sw.WriteLine($"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}");
                }
                catch { }
            }

            sw.Flush();
            byte[] hash = sha256.ComputeHash(ms.ToArray());
            return Convert.ToBase64String(hash).Replace('/', '_').Replace('+', '-');
        }

        /// <summary>
        /// Recupera un'icona dalla cache in memoria o dal disco.
        /// Ritorna null se l'icona non è ancora disponibile.
        /// </summary>
        internal static Bitmap? GetFromMemory(string key)
        {
            return _memoryCache.TryGetValue(key, out var bmp) ? bmp : null;
        }

        /// <summary>
        /// Recupera un'icona dalla cache su disco.
        /// </summary>
        internal static Bitmap? GetFromDisk(string key)
        {
            string cachePath = Path.Combine(_cacheDir, $"{key}.png");
            if (!File.Exists(cachePath)) return null;

            try
            {
                return new Bitmap(cachePath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Salva un'icona nella cache in memoria e su disco.
        /// </summary>
        internal static void Store(string key, Bitmap icon)
        {
            _memoryCache[key] = icon;

            try
            {
                string cachePath = Path.Combine(_cacheDir, $"{key}.png");
                icon.Save(cachePath, System.Drawing.Imaging.ImageFormat.Png);
                _metadata.TryAdd(key, new CacheEntry("", 0));
            }
            catch { }
        }

        /// <summary>
        /// Invalida la cache se i metadati della cartella sono cambiati.
        /// </summary>
        internal static bool TryGetCachedHash(string folderPath, string[] files, string extension, out string cachedHash)
        {
            string newHash = ComputeFolderHash(folderPath, files, extension);
            cachedHash = newHash;
            return false;
        }
    }

    /// <summary>
    /// Form principale che mostra i file della cartella con le loro icone.
    /// Usa cache delle icone e caricamento asincrono per tempi di apertura rapidi.
    /// </summary>
    public class IconPopup : Form
    {
        private readonly IconPopupConfig _config;
        private readonly string _folderPath;
        private readonly string _fileExtension;
        private readonly List<IDisposable> _disposables = new();
        private CancellationTokenSource? _animationCancellation;

        private string[] _files = Array.Empty<string>();
        private string _cacheFolderHash = string.Empty;

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
            Padding = new Padding(0);
            TopMost = true;
            AutoScroll = false;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            UpdateStyles();

            if (_config.EnableAnimations)
                Opacity = 0;

            Load += OnFormLoad;
            Deactivate += OnFormDeactivate;
            KeyDown += OnKeyDown;

            ResumeLayout(false);
        }

        private async void CreateUI()
        {
            _files = GetFilteredFiles();
            if (!_files.Any())
            {
                CreateEmptyStateUI();
                return;
            }

            // Calcola hash cache una sola volta
            _cacheFolderHash = IconCache.ComputeFolderHash(_folderPath, _files, _fileExtension);

            var flowPanel = CreateMainPanel();
            var buttonMap = new Dictionary<string, Button>();

            // Crea tutti i bottoni con icona di default IMMEDIATAMENTE
            foreach (string file in _files)
            {
                try
                {
                    var button = CreateFileButton(file);
                    flowPanel.Controls.Add(button);
                    buttonMap[file] = button;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Errore creazione pulsante per {file}: {ex.Message}");
                }
            }

            Controls.Add(flowPanel);
            AdjustFormSize(flowPanel);

            // Carica icone in background e aggiornano i bottoni man mano
            await LoadIconsAsync(buttonMap);
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
            return new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(8),
                BackColor = _config.PanelColor,
                BorderStyle = BorderStyle.None
            };
        }

        private Button CreateFileButton(string filePath)
        {
            string fileName = _config.ShowFileExtensions
                ? Path.GetFileName(filePath)
                : Path.GetFileNameWithoutExtension(filePath);

            fileName = TruncateFileName(fileName);

            // Icona di default immediata (nessuna attesa I/O)
            var defaultIcon = new Bitmap(_config.IconSize, _config.IconSize);
            using (var g = Graphics.FromImage(defaultIcon))
            {
                g.Clear(_config.PanelColor);
                using var defIcon = SystemIcons.Application;
                g.DrawIcon(defIcon, new Rectangle(0, 0, _config.IconSize, _config.IconSize));
            }

            var button = new Button
            {
                Size = new Size(_config.ButtonWidth, _config.ButtonHeight),
                Text = fileName,
                Image = defaultIcon,
                ImageAlign = ContentAlignment.MiddleLeft,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 5, 0),
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

            _disposables.Add(defaultIcon);
            return button;
        }

        /// <summary>
        /// Carica le icone reali in background e aggiorna i bottoni man mano che vengono trovate.
        /// Usa la cache in memoria e su disco per evitare ricerche duplicate.
        /// </summary>
        private async Task LoadIconsAsync(Dictionary<string, Button> buttonMap)
        {
            // Batch size: carica fino a 10 icone alla volta per non bloccare il UI thread troppo a lungo
            const int batchSize = 10;

            for (int i = 0; i < _files.Length; i += batchSize)
            {
                // Piccola pausa per permettere al UI di reagire
                await Task.Delay(1);

                var batch = _files.Skip(i).Take(batchSize).ToList();
                await Task.Run(() =>
                {
                    foreach (string file in batch)
                    {
                        if (!IsHandleCreated) return;

                        string cacheKey = $"{_cacheFolderHash}_{Path.GetFileNameWithoutExtension(file)}";

                        // 1. Controlla cache in memoria
                        Bitmap? cached = IconCache.GetFromMemory(cacheKey);
                        if (cached != null)
                        {
                            UpdateButtonIcon(buttonMap, file, cached);
                            continue;
                        }

                        // 2. Estrai icona (usando cache di sistema Windows già efficiente)
                        Bitmap? icon = ExtractFileIcon(file);
                        if (icon == null) continue;

                        // 3. Salva in cache
                        IconCache.Store(cacheKey, icon);
                        UpdateButtonIcon(buttonMap, file, icon);
                    }
                });
            }
        }

        private void UpdateButtonIcon(Dictionary<string, Button> buttonMap, string filePath, Bitmap icon)
        {
            if (!IsHandleCreated) return;

            BeginInvoke(new Action(() =>
            {
                if (buttonMap.TryGetValue(filePath, out var button) && button.Image != icon)
                {
                    button.Image?.Dispose();
                    button.Image = icon;
                }
            }));
        }

        private Bitmap? ExtractFileIcon(string filePath)
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

        private void AdjustFormSize(Control flowPanel)
        {
            // Dimensione DPI scalata una sola volta
            float dpiScale = DeviceDpi / 96.0f;
            int scaledMaxHeight = (int)(_config.MaxHeight * dpiScale);

            int panelHeight = flowPanel.PreferredSize.Height;
            int panelWidth = flowPanel.PreferredSize.Width;
            int vScrollWidth = SystemInformation.VerticalScrollBarWidth;

            if (panelHeight > scaledMaxHeight)
            {
                int formWidth = panelWidth + vScrollWidth;
                Size = new Size(formWidth, scaledMaxHeight);
                MaximumSize = new Size(formWidth, scaledMaxHeight);

                if (flowPanel is FlowLayoutPanel fp)
                {
                    fp.Width = formWidth;
                    fp.MaximumSize = new Size(formWidth, 0);
                }
            }
            else
            {
                Size = new Size(panelWidth, panelHeight);
                MaximumSize = new Size(0, 0);

                if (flowPanel is FlowLayoutPanel fp)
                {
                    fp.Width = panelWidth;
                    fp.MaximumSize = new Size(panelWidth, 0);
                }
            }

            if (_config.CornerRadius > 0)
            {
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

                path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
                path.AddArc(Width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
                path.AddArc(Width - radius * 2, Height - radius * 2, radius * 2, radius * 2, 0, 90);
                path.AddArc(0, Height - radius * 2, radius * 2, radius * 2, 90, 90);
                path.CloseFigure();

                Region = new Region(path);
                Invalidate();
                Update();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore angoli arrotondati: {ex.Message}");
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
            catch (OperationCanceledException) { }
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
                BeginInvoke(new Action(Close));
            else
                Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _animationCancellation?.Cancel();
            _animationCancellation?.Dispose();

            foreach (var disposable in _disposables)
                disposable?.Dispose();
            _disposables.Clear();

            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_config.EnableGlassEffect && _config.CornerRadius > 0)
            {
                DrawModernEffect(e.Graphics);
            }
        }

        private void DrawModernEffect(Graphics g)
        {
            if (Width <= 0 || Height <= 0) return;

            using var path = new GraphicsPath();
            int radius = Math.Min(_config.CornerRadius, Math.Min(Width, Height) / 2);

            path.AddArc(0, 0, radius * 2, radius * 2, 180, 90);
            path.AddArc(Width - radius * 2, 0, radius * 2, radius * 2, 270, 90);
            path.AddArc(Width - radius * 2, Height - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(0, Height - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();

            // Ombra semplificata: un solo livello invece di 3
            using var shadowBrush = new SolidBrush(Color.FromArgb(15, Color.Black));
            g.FillPath(shadowBrush, path);

            // Bordo
            if (_config.BorderThickness > 0)
            {
                var borderColor = ColorTranslator.FromHtml(_config.BorderColorHex);
                using var borderPen = new Pen(borderColor, _config.BorderThickness);
                g.DrawPath(borderPen, path);
            }

            // Gradiente
            using var gradientBrush = new LinearGradientBrush(
                new Rectangle(0, 0, Width, Height),
                Color.FromArgb(250, _config.PanelColor),
                ControlPaint.Light(_config.PanelColor, 0.05f),
                LinearGradientMode.Vertical);
            g.FillPath(gradientBrush, path);

            // Riflesso
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
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                return cp;
            }
        }
    }

    /// <summary>
    /// Pannello con scrollbar personalizzata. La scrollbar orizzontale è rimossa a livello Win32.
    /// </summary>
    public class ThemedScrollablePanel : Panel
    {
        private readonly Color _panelColor;
        private const int ScrollBtnSize = 15;
        private bool _mouseOverScrollbar;

        public ThemedScrollablePanel(Color panelColor, Color textColor)
        {
            _panelColor = panelColor;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer, true);
            UpdateStyles();
            AutoScroll = false;
            HScroll = false;
            VScroll = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style &= ~0x00100000; // Rimuovi WS_HSCROLL
                return cp;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool overScrollbar = e.X >= Width - SystemInformation.VerticalScrollBarWidth;
            if (_mouseOverScrollbar != overScrollbar)
            {
                _mouseOverScrollbar = overScrollbar;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_mouseOverScrollbar)
            {
                _mouseOverScrollbar = false;
                Invalidate();
            }
            base.OnMouseLeave(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(_panelColor);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (!IsHandleCreated) return;

            int vWidth = SystemInformation.VerticalScrollBarWidth;
            if (VerticalScroll.Visible)
                DrawVerticalScrollbar(e.Graphics, vWidth);
        }

        private void DrawVerticalScrollbar(Graphics g, int vWidth)
        {
            int margin = 1;
            Color thumbColor = _mouseOverScrollbar
                ? ControlPaint.Light(_panelColor, 0.2f)
                : ControlPaint.Light(_panelColor, 0.1f);
            Color trackColor = _mouseOverScrollbar
                ? ControlPaint.Light(_panelColor, 0.12f)
                : ControlPaint.Light(_panelColor, 0.06f);

            Rectangle vTrackRect = new Rectangle(Width - vWidth, 0, vWidth, Height);
            using (var trackBrush = new SolidBrush(trackColor))
                g.FillRectangle(trackBrush, vTrackRect);

            float ratio = (float)(vTrackRect.Height - 2 * ScrollBtnSize) /
                          (VerticalScroll.Maximum - VerticalScroll.Minimum + VerticalScroll.LargeChange);
            int thumbHeight = Math.Max((int)(ratio * VerticalScroll.LargeChange), 20);
            int thumbTop = ScrollBtnSize + (int)((VerticalScroll.Value - VerticalScroll.Minimum) * ratio);
            Rectangle vThumbRect = new Rectangle(Width - vWidth + 1, thumbTop, vWidth - 2, thumbHeight);

            using (var thumbBrush = new SolidBrush(thumbColor))
                g.FillRectangle(thumbBrush, vThumbRect);
            using (var pen = new Pen(ControlPaint.Dark(_panelColor, 0.3f), 1))
                g.DrawRectangle(pen, vThumbRect.X, vThumbRect.Y, vThumbRect.Width - 1, vThumbRect.Height - 1);

            if (VerticalScroll.Value > VerticalScroll.Minimum)
                DrawScrollArrow(g, new Rectangle(Width - vWidth + margin, margin, vWidth - margin * 2, ScrollBtnSize), ScrollArrow.Up);

            if (VerticalScroll.Value < VerticalScroll.Maximum)
                DrawScrollArrow(g, new Rectangle(Width - vWidth + margin, Height - ScrollBtnSize - margin, vWidth - margin * 2, ScrollBtnSize), ScrollArrow.Down);
        }

        private void DrawScrollArrow(Graphics g, Rectangle rect, ScrollArrow arrow)
        {
            using (var brush = new SolidBrush(ControlPaint.Dark(_panelColor, 0.2f)))
                g.FillRectangle(brush, rect);

            using (var pen = new Pen(ControlPaint.Dark(_panelColor, 0.4f), 1))
            {
                int cx = rect.X + rect.Width / 2;
                int cy = rect.Y + rect.Height / 2;
                int size = Math.Min(rect.Width, rect.Height) / 3;

                g.DrawLine(pen, cx - size / 2, cy, cx + size / 2, cy);

                switch (arrow)
                {
                    case ScrollArrow.Up:
                        g.DrawLine(pen, cx, cy - size / 2, cx - size / 2, cy + size / 2);
                        g.DrawLine(pen, cx, cy - size / 2, cx + size / 2, cy + size / 2);
                        break;
                    case ScrollArrow.Down:
                        g.DrawLine(pen, cx, cy + size / 2, cx - size / 2, cy - size / 2);
                        g.DrawLine(pen, cx, cy + size / 2, cx + size / 2, cy - size / 2);
                        break;
                    case ScrollArrow.Left:
                        g.DrawLine(pen, cx - size / 2, cy, cx + size / 2, cy - size / 2);
                        g.DrawLine(pen, cx - size / 2, cy, cx + size / 2, cy + size / 2);
                        break;
                    case ScrollArrow.Right:
                        g.DrawLine(pen, cx + size / 2, cy, cx - size / 2, cy - size / 2);
                        g.DrawLine(pen, cx + size / 2, cy, cx - size / 2, cy + size / 2);
                        break;
                }
            }
        }

        private enum ScrollArrow { Up, Down, Left, Right }

        protected override void WndProc(ref Message m)
        {
            const int WM_SIZE = 0x0005;
            const int WM_DISPLAYCHANGE = 0x007E;
            if (m.Msg == WM_SIZE || m.Msg == WM_DISPLAYCHANGE)
                Invalidate();
            base.WndProc(ref m);
        }
    }
}
