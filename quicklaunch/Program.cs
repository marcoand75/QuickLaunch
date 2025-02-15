using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text.Json;

class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        string folderPath = string.Empty;
        string configFileName = string.Empty;
        string fileExtension = string.Empty;

        // Mostra l'help se non ci sono parametri
        if (args.Length == 0)
        {
            MessageBox.Show(
                "Utilizzo:\n\n" +
                "-folder <percorso_cartella>\nSpecifica la cartella contenente i file.\n\n" +
                "-config <file_config>\nSpecifica il file di configurazione JSON (opzionale).\n\n" +
                "-ext <estensione>\nFiltra i file per estensione (opzionale).\n\n\n" +
                "Esempio:\n" +
                "IconPopup.exe -folder \"C:\\MyFolder\" -config config.json -ext .exe\n",
                "Help",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        // Parsing dei parametri dalla linea di comando
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-folder" && i + 1 < args.Length)
            {
                folderPath = args[i + 1].Trim('"');
                i++; // Salta il parametro successivo
            }
            else if (args[i] == "-config" && i + 1 < args.Length)
            {
                configFileName = args[i + 1].Trim('"');
                i++;
            }
            else if (args[i] == "-ext" && i + 1 < args.Length)
            {
                fileExtension = args[i + 1].Trim('"');
                i++;
            }
        }

        // Verifica se la cartella è stata specificata
        if (string.IsNullOrEmpty(folderPath))
        {
            MessageBox.Show("Specifica una cartella.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!Directory.Exists(folderPath))
        {
            MessageBox.Show("Cartella non trovata.", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Se non viene specificato un file di configurazione, usiamo le impostazioni predefinite
        IconPopupConfig config;
        if (string.IsNullOrEmpty(configFileName) || !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configFileName)))
        {
            config = new IconPopupConfig
            {
                CornerRadius = 15, // Arrotondamento di default
                PanelColor = SystemColors.Window, // Colore di default del pannello
                TextColor = SystemColors.ControlText, // Colore di default del testo
                AnimationSpeed = 30 // Velocità di animazione
            };
        }
        else
        {
            // Legge e deserializza il file di configurazione
            try
            {
                string currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(currentDirectory, configFileName);
                string configJson = File.ReadAllText(configPath);
                config = JsonSerializer.Deserialize<IconPopupConfig>(configJson) ?? new IconPopupConfig();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore nella lettura del file di configurazione: {ex.Message}", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        // Verifica ed eventualmente modifica l'estensione dei file
        if (!string.IsNullOrEmpty(fileExtension))
        {
            fileExtension = fileExtension.StartsWith(".") ? fileExtension : "." + fileExtension;
        }

        // Avvia l'applicazione
        Application.Run(new IconPopup(folderPath, config, fileExtension));
    }
}

class IconPopupConfig
{
    public int CornerRadius { get; set; } = 15; // Raggio di arrotondamento
    public int AnimationSpeed { get; set; } = 30; // Raggio di arrotondamento

    private string textColorHex;
    public string TextColorHex
    {
        get => textColorHex;
        set
        {
            textColorHex = value;
            TextColor = string.IsNullOrEmpty(value) ? Color.Black : ColorTranslator.FromHtml(value);
        }
    }

    public Color TextColor { get; set; } = Color.Black; // Default

    // Altri colori già corretti
    private string backgroundColorHex;
    public string BackgroundColorHex
    {
        get => backgroundColorHex;
        set
        {
            backgroundColorHex = value;
            BackgroundColor = string.IsNullOrEmpty(value) ? SystemColors.Control : ColorTranslator.FromHtml(value);
        }
    }

    private string panelColorHex;
    public string PanelColorHex
    {
        get => panelColorHex;
        set
        {
            panelColorHex = value;
            PanelColor = string.IsNullOrEmpty(value) ? SystemColors.Window : ColorTranslator.FromHtml(value);
        }
    }

    public Color BackgroundColor { get; set; } = SystemColors.Control;
    public Color PanelColor { get; set; } = SystemColors.Window;
}

class IconPopup : Form
{
    private int _cornerRadius;
    private int _animationSpeed;

    public IconPopup(string folderPath, IconPopupConfig config, string fileExtension)
    {
        Height = Height * 5;
        _cornerRadius = config.CornerRadius;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None; // Finestra non ridimensionabile
        BackColor = SystemColors.Control; // Colore di sfondo fisso
        Padding = new Padding(10);
        _animationSpeed = config.AnimationSpeed;

        // Imposta la regione arrotondata
        if (_cornerRadius > 0)
        {
            SetRoundedRegion();
        }

        FlowLayoutPanel panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(5),
            BackColor = config.PanelColor,
            BorderStyle = BorderStyle.None
        };
        Controls.Add(panel);

        // Verifica che l'estensione inizi con un "."
        if (!string.IsNullOrEmpty(fileExtension) && !fileExtension.StartsWith("."))
        {
            fileExtension = "." + fileExtension;
        }

        var files = Directory.GetFiles(folderPath, "*" + fileExtension).ToArray();

        foreach (var file in files)
        {
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(file);
            Panel itemPanel = new Panel
            {
                AutoSize = true,
                Padding = new Padding(5),
                BackColor = config.PanelColor
            };

            // Estrazione dell'icona
            Icon icon = null;
            try
            {
                icon = Icon.ExtractAssociatedIcon(file);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Errore durante l'estrazione dell'icona per {file}: {ex.Message}");
            }

            Bitmap iconBitmap;
            if (icon != null)
            {
                iconBitmap = new Bitmap(icon.ToBitmap(), new Size(32, 32));  // IconSize fisso a 32
            }
            else
            {
                Debug.WriteLine($"Icona non trovata per {file}, verrà usata un'icona di default.");
                iconBitmap = new Bitmap(SystemIcons.Warning.ToBitmap(), new Size(32, 32));  // Icona di default
            }

            Button iconButton = new Button
            {
                Size = new Size(132, 42), // IconSize fisso a 32 + margine
                Text = fileNameWithoutExt,
                Image = iconBitmap,
                ImageAlign = ContentAlignment.MiddleLeft, // Testo in alto
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(5),
                FlatStyle = FlatStyle.Flat,
                BackColor = config.PanelColor,
                ForeColor = config.TextColor, // Colore del testo
                Tag = file,
                TextImageRelation = TextImageRelation.ImageBeforeText // Testo non sovrapposto all'icona
            };

            // Rimuove i bordi del pulsante
            iconButton.FlatAppearance.BorderSize = 0;

            iconButton.Click += (s, e) => Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });

            itemPanel.Controls.Add(iconButton);
            panel.Controls.Add(itemPanel);
        }

        // Calcola la posizione del popup
        Point mousePos = Cursor.Position;
        Screen screen = Screen.FromPoint(mousePos);
        Rectangle workingArea = screen.WorkingArea;

        // Ridimensiona la finestra in base al contenuto
        Size = new Size(panel.PreferredSize.Width + Padding.Horizontal - 20, panel.PreferredSize.Height + Padding.Vertical - 20);

        // Imposta l'altezza massima della finestra per non superare l'area di lavoro
        int maxHeight = workingArea.Height - 20; // Lascia un margine di 20 pixel
        if (Height > maxHeight)
        {
            Height = maxHeight;
            panel.AutoScroll = true; // Abilita lo scroll se il contenuto è troppo alto
        }

        int x = mousePos.X - Width / 2;
        int y = workingArea.Bottom - Height;

        // Assicurati che il popup non esca dallo schermo
        if (x < workingArea.Left)
            x = workingArea.Left;
        else if (x + Width > workingArea.Right)
            x = workingArea.Right - Width;

        Location = new Point(x, y);

        Opacity = 0;
        Load += async (s, e) => await AnimateOpen();
        Deactivate += (s, e) => Close();
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    private void SetRoundedRegion()
    {
        GraphicsPath path = new GraphicsPath();
        path.AddArc(0, 0, _cornerRadius * 2, _cornerRadius * 2, 180, 90); // Angolo superiore sinistro
        path.AddArc(Width - _cornerRadius * 2, 0, _cornerRadius * 2, _cornerRadius * 2, 270, 90); // Angolo superiore destro
        path.AddArc(Width  - _cornerRadius * 2, Height - _cornerRadius * 2, _cornerRadius * 2, _cornerRadius * 2, 0, 90); // Angolo inferiore destro
        path.AddArc(0, Height - _cornerRadius * 2, _cornerRadius * 2, _cornerRadius * 2, 90, 90); // Angolo inferiore sinistro
        path.CloseFigure();
        Region = new Region(path);
    }

    private async Task AnimateOpen()
    {
        int steps = 20; // Più passaggi per un'animazione più fluida
        for (int i = 0; i <= steps; i++)
        {
            Opacity = i / (double)steps;
            await Task.Delay(_animationSpeed); // Ritardo maggiore per un'animazione più lenta
        }
    }
}