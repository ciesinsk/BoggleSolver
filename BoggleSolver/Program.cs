namespace BoggleSolverApp
{
    internal class Program
    {
        static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    PrintUsage();
                    return 1;
                }

                string boardPath = args[0];
                string dictPath  = args[1];

                int minLen = 3;
                int reusePerCell = 1; // Standard-Boggle: jedes Feld max. 1x pro Wortpfad

                // Zusätzliche optionale Argumente parsen (beliebige Reihenfolge)
                for (int i = 2; i < args.Length; i++)
                {
                    var a = args[i];
                    if (int.TryParse(a, out int parsedMin))
                    {
                        minLen = parsedMin;
                        continue;
                    }

                    if (a.StartsWith("--reuse-per-cell", StringComparison.OrdinalIgnoreCase) ||
                        a.StartsWith("--reuse", StringComparison.OrdinalIgnoreCase))
                    {
                        string? value = null;
                        var eqIdx = a.IndexOf('=');
                        if (eqIdx >= 0 && eqIdx < a.Length - 1)
                        {
                            value = a[(eqIdx + 1)..];
                        }
                        else if (i + 1 < args.Length && int.TryParse(args[i + 1], out _))
                        {
                            value = args[++i];
                        }

                        if (string.IsNullOrEmpty(value) || !int.TryParse(value, out reusePerCell) || reusePerCell < 1)
                        {
                            Console.Error.WriteLine("Ungültiger Wert für --reuse/--reuse-per-cell. Erwarte Ganzzahl >= 1.");
                            return 1;
                        }
                        continue;
                    }

                    Console.Error.WriteLine($"Unbekanntes Argument: {a}");
                    PrintUsage();
                    return 1;
                }

                if (!File.Exists(boardPath))
                {
                    Console.Error.WriteLine($"Spielfeld-Datei nicht gefunden: {boardPath}");
                    return 1;
                }
                if (!File.Exists(dictPath))
                {
                    Console.Error.WriteLine($"Wörterbuch-Datei nicht gefunden: {dictPath}");
                    return 1;
                }

                var board = Board.Load(boardPath);
                var trie = Trie.FromDictionaryFile(dictPath);

                var solver = new Solver(board, trie, minLen, reusePerCell);
                var words = solver.FindAllWords();

                Console.WriteLine($"Gefundene Wörter (min. Länge {minLen}, reuse/feld {reusePerCell}): {words.Count}");
                foreach (var w in words
                    .OrderByDescending(w => w.Length)
                    .ThenBy(w => w, StringComparer.InvariantCultureIgnoreCase))
                {
                    Console.WriteLine(w);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fehler: " + ex.Message);
                return 2;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Verwendung:");
            Console.WriteLine("  BoggleSolver <pfad-zum-spielfeld.txt> <pfad-zum-woerterbuch.txt> [minWortlaenge] [--reuse[=N]]");
            Console.WriteLine("  BoggleSolver <pfad-zum-spielfeld.txt> <pfad-zum-woerterbuch.txt> [minWortlaenge] [--reuse-per-cell N]");
            Console.WriteLine();
            Console.WriteLine("Standard: minWortlaenge=3, reuse-per-cell=1 (Standard-Boggle-Regel: kein Feld mehrfach pro Wortpfad).");
            Console.WriteLine();
            Console.WriteLine("Format der Spielfeld-Datei:");
            Console.WriteLine("  - Eine Zeile pro Reihe.");
            Console.WriteLine("  - Entweder einzelne Zeichen ohne Leerzeichen (z.B. 'ABCD')");
            Console.WriteLine("    oder Tokens durch Leerzeichen getrennt (z.B. 'A B C D' oder 'Qu A ß E').");
            Console.WriteLine("  - Groß/Kleinschreibung ist egal.");
            Console.WriteLine();
            Console.WriteLine("Format des Wörterbuchs:");
            Console.WriteLine("  - Ein Wort pro Zeile, Groß/Kleinschreibung egal, Umlaute/ß erlaubt.");
            Console.WriteLine();
            Console.WriteLine("Beispiel:");
            Console.WriteLine("  BoggleSolver samples/board4x4.txt samples/woerterbuch_de_klein.txt 3 --reuse=2");
        }
    }

    internal sealed class Board
    {
        public string[,] Cells { get; }
        public int Rows { get; }
        public int Cols { get; }

        private Board(string[,] cells)
        {
            Cells = cells;
            Rows = cells.GetLength(0);
            Cols = cells.GetLength(1);
        }

        public static Board Load(string path)
        {
            var lines = File.ReadAllLines(path)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
            if (lines.Length == 0)
                throw new InvalidOperationException("Leere Spielfeld-Datei.");

            // Parse rows: support both "A B C" and "ABC"
            List<string[]> tokensPerRow = new();
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                string[] tokens;
                if (line.Contains(' '))
                {
                    tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                }
                else
                {
                    // split into grapheme-like units (fallback: single char)
                    tokens = line.Select(ch => ch.ToString()).ToArray();
                }
                tokensPerRow.Add(tokens);
            }

            int cols = tokensPerRow[0].Length;
            if (tokensPerRow.Any(r => r.Length != cols))
                throw new InvalidOperationException("Alle Zeilen müssen gleich viele Spalten haben.");

            var cells = new string[lines.Length, cols];
            for (int r = 0; r < lines.Length; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    cells[r, c] = tokensPerRow[r][c].ToUpperInvariant();
                }
            }
            return new Board(cells);
        }

        public IEnumerable<(int r, int c)> Neighbors(int r, int c)
        {
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = r + dr, nc = c + dc;
                    if (nr >= 0 && nr < Rows && nc >= 0 && nc < Cols)
                        yield return (nr, nc);
                }
        }
    }

    internal sealed class Solver
    {
        private readonly Board _board;
        private readonly Trie _trie;
        private readonly int _minLen;
        private readonly int _reusePerCell;
        private readonly HashSet<string> _results;

        public Solver(Board board, Trie trie, int minLen, int reusePerCell)
        {
            _board = board;
            _trie = trie;
            _minLen = Math.Max(1, minLen);
            _reusePerCell = Math.Max(1, reusePerCell);
            _results = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        }

        public HashSet<string> FindAllWords()
        {
            int R = _board.Rows, C = _board.Cols;
            var used = new int[R, C];

            for (int r = 0; r < R; r++)
            {
                for (int c = 0; c < C; c++)
                {
                    used[r, c] = 1;
                    Dfs(r, c, used, _board.Cells[r, c]);
                    used[r, c] = 0;
                }
            }

            return _results;
        }

        private void Dfs(int r, int c, int[,] used, string current)
        {
            if (!_trie.HasPrefix(current))
                return;

            if (current.Length >= _minLen && _trie.Contains(current))
                _results.Add(current);

            foreach (var (nr, nc) in _board.Neighbors(r, c))
            {
                if (used[nr, nc] < _reusePerCell)
                {
                    used[nr, nc]++;
                    string next = current + _board.Cells[nr, nc];
                    Dfs(nr, nc, used, next);
                    used[nr, nc]--;
                }
            }
        }
    }

    internal sealed class Trie
    {
        private sealed class Node
        {
            public Dictionary<char, Node> Next = new Dictionary<char, Node>();
            public bool IsWord;
        }

        private readonly Node _root = new Node();

        public void Insert(string word)
        {
            var cur = _root;
            foreach (var ch in word)
            {
                if (!cur.Next.TryGetValue(ch, out var nxt))
                {
                    nxt = new Node();
                    cur.Next[ch] = nxt;
                }
                cur = nxt;
            }
            cur.IsWord = true;
        }

        public bool Contains(string word)
        {
            var node = FindNode(word);
            return node != null && node.IsWord;
        }

        public bool HasPrefix(string prefix)
        {
            return FindNode(prefix) != null;
        }

        private Node? FindNode(string s)
        {
            var cur = _root;
            foreach (var ch in s)
            {
                if (!cur.Next.TryGetValue(ch, out var nxt))
                    return null;
                cur = nxt;
            }
            return cur;
        }

        public static Trie FromDictionaryFile(string path)
        {
            var trie = new Trie();
            foreach (var raw in File.ReadAllLines(path))
            {
                var w = raw.Trim();
                if (w.Length == 0) continue;
                trie.Insert(w.ToUpperInvariant());
            }
            return trie;
        }
    }
}