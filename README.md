# N.I.N.A-Plugin-for-Touch-N-Stars

[English version](README_en.md)

Dieses Plugin dient zur Integration von [Touch'N'Stars](https://github.com/Touch-N-Stars/Touch-N-Stars) in der Astrosoftware **NINA** (Nighttime Imaging 'N' Astronomy).

### 🚀 **Aktueller Status: Beta-Version**  
Das Plugin befindet sich aktuell in der Entwicklung und kann noch Fehler enthalten.

### 🔧 **Installieren**
Um das Plugin nutzen zu können, muss der Inhalt der ZIP-Datei in das Pluginverzeichnis von NINA kopiert werden.
Dies befindet sich meist unter: %LOCALAPPDATA%/NINA/Plugins/3.0.0/Touch-N-Stars
Sollte der Ordner nicht vorhanden sein, bitte vorher erstellen.

### 🧩 **Wichtige Hinweise** 
- Es wird das Plugin **Advanced API** in der aktuellsten Version benötigt.
  Der Port der API muss auf 1888 eingestellt sein und die V2 muss aktiv sein.
  Zusätzlich muss "Use Access-Control-Allow-Origin Header" aktiv sein.
- Für das Three Point Polar Alignment wird die Version 2.2.2.0 oder neuer benötigt.

### 🔌 **Für Plugin-Entwickler**
Du möchtest dein eigenes N.I.N.A.-Plugin in Touch'N'Stars einbinden? Nutze die **Plugin-Bridge**:
Dein Plugin spricht über den N.I.N.A.-MessageBroker mit Touch'N'Stars und braucht keinen eigenen
Webserver, keinen Port und keine DLL-Abhängigkeit. Anleitung (Englisch), die Kopiervorlage
`TnsBridge.cs` und ein vollständiges Beispiel: [docs/plugin-bridge](docs/plugin-bridge/README.md).
