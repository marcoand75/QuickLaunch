# QuickLaunch

![video](https://github.com/user-attachments/assets/c0390d49-c89b-4217-87c2-07dfad98cd00)

## Descrizione
**QuickLaunch** è un'applicazione Windows che mostra un popup con le icone dei file presenti in una cartella specificata. Permette di filtrare i file in base all'estensione e di personalizzare l'aspetto del popup attraverso un file di configurazione JSON.

## Utilizzo

```bash
QuickLaunch.exe -folder "C:\PercorsoCartella" [-config config.json] [-ext .exe]
```

### Parametri
- `-folder <percorso_cartella>`: Specifica la cartella contenente i file (obbligatorio).
- `-config <file_config>`: Specifica il file di configurazione JSON (opzionale).
- `-ext <estensione>`: Filtra i file per estensione (opzionale, es. `.exe`).

### Esempio
```bash
QuickLaunch.exe -folder "C:\MyFolder" -config config.json -ext .exe
```

## Configurazione
Il file di configurazione JSON permette di personalizzare i colori, la velocità dell'animazione e il raggio di arrotondamento del popup.

### Esempio di `config.json`
```json
{
  "CornerRadius": 15,
  "AnimationSpeed": 30,
  "TextColorHex": "#000000",
  "BackgroundColorHex": "#F0F0F0",
  "PanelColorHex": "#FFFFFF"
}
```

## Licenza
Questo progetto è rilasciato sotto la licenza MIT. Vedi il file `LICENSE` per maggiori dettagli.

---

# QuickLaunch (English)

## Description
**QuickLaunch** is a Windows application that displays a popup with file icons from a specified folder. It allows filtering files by extension and customizing the popup appearance via a JSON configuration file.

## Usage

```bash
QuickLaunch.exe -folder "C:\FolderPath" [-config config.json] [-ext .exe]
```

### Parameters
- `-folder <folder_path>`: Specifies the folder containing the files (mandatory).
- `-config <config_file>`: Specifies the JSON configuration file (optional).
- `-ext <extension>`: Filters files by extension (optional, e.g., `.exe`).

### Example
```bash
QuickLaunch.exe -folder "C:\MyFolder" -config config.json -ext .exe
```

## Configuration
The JSON configuration file allows customization of colors, animation speed, and popup corner radius.

### Example `config.json`
```json
{
  "CornerRadius": 15,
  "AnimationSpeed": 30,
  "TextColorHex": "#000000",
  "BackgroundColorHex": "#F0F0F0",
  "PanelColorHex": "#FFFFFF"
}
```

## License
This project is licensed under the MIT License. See the `LICENSE` file for details.

