# BoggleSolver (C# .NET 8, Konsole)

Ein einfaches Boggle-Löseprogramm, das alle gültigen Wörter auf einem flexiblen Spielfeld findet.
- **Argument 1:** Pfad zur Spielfeld-Textdatei
- **Argument 2:** Pfad zur Wörterbuch-Textdatei
- **Optional:** Minimale Wortlänge (Standard: 3)

## Build & Run (Visual Studio / dotnet CLI)

```bash
dotnet build
dotnet run --project BoggleSolver/BoggleSolver.csproj -- samples/board4x4.txt samples/woerterbuch_de_klein.txt 3
```

## Format der Spielfeld-Datei

- Eine Zeile pro Reihe.
- Entweder einzelne Zeichen ohne Leerzeichen (z. B. `ABCD`), **oder** Tokens mit Leerzeichen (z. B. `A B C D` oder `Qu A ß E`).
- Groß/Kleinschreibung ist egal.

Beispiel (`samples/board4x4.txt`):
```
B O G G
L E S P
I E L E
T E S T
```

## Format des Wörterbuchs
- Ein Wort pro Zeile, Groß/Kleinschreibung egal, Umlaute/ß erlaubt.
- Beispiel: `samples/woerterbuch_de_klein.txt`

## Hinweise
- Es werden alle 8 Nachbarschaften (horizontal/vertikal/diagonal) berücksichtigt.
- Das Board unterstützt mehrbuchstabige Felder (z. B. `Qu`), wenn sie in der Board-Datei als ein Token (durch Leerzeichen getrennt) eingetragen sind.
- Wörter werden **groß** verglichen (kulturell invariant). Umlaute/ß bleiben erhalten.

## Ausgabe
- Alle gefundenen Wörter, nach Länge (absteigend) und dann alphabetisch sortiert, plus Gesamtzahl.

## Option: Felder mehrfach verwenden
Standard ist **einmal pro Wortpfad** (Boggle-Regel). Du kannst optional eine höhere Nutzung erlauben:

```bash
# Bedeutet: Jedes Feld darf bis zu 2x in demselben Wortpfad verwendet werden
dotnet run --project BoggleSolver/BoggleSolver.csproj -- samples/board4x4.txt samples/woerterbuch_de_klein.txt 3 --reuse=2
# Alternativ
dotnet run --project BoggleSolver/BoggleSolver.csproj -- samples/board4x4.txt samples/woerterbuch_de_klein.txt 3 --reuse-per-cell 2
```
Hinweis: Ein Wert von `1` entspricht dem Standard (kein Wiederverwenden desselben Feldes im selben Wortpfad).
