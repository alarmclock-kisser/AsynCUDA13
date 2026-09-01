# Copilot Instructions

## General Guidelines
- Bei laufender Codearbeit keine Ankündigungen ohne anschließende unmittelbare Ausführung erfolgen.

## Projektrichtlinien
- Das Projekt AsynCUDA13.Runtime muss bei CUDA 13 bleiben; keine Umstellung auf CUDA-12-Pakete oder CUDA-12-Kompatibilität vorschlagen bzw. durchführen.

## Media-Objekte
- Media-Objekte müssen `Id` und `CreatedAt` ausschließlich bei ihrer eigenen Initialisierung unveränderlich setzen. Clone-, Copy- und CreateFromInfo-Pfade dürfen diese Werte niemals übernehmen.

## Tests
- Tests sollen MSTest mit Shouldly, AAA-Struktur, parametrierten DataRow-Tests und aussagekräftigen Mengen-/Prädikatsassertionen verwenden.
- OpenCL-Servicekomponenten sollen jeweils in separaten, feingranularen Testklassen getestet werden; Memory-, Compiler- und Execute-Tests sollen DataRows sowie positive und negative Fälle mit Assertions auf Exceptions und Fehlermeldungen enthalten.