using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;

class Program
{
    static int Main()
    {
        try
        {
            var projectDir = Directory.GetCurrentDirectory();
            var repoRoot = Path.GetFullPath(Path.Combine(projectDir, "..", ".."));

            var imagesDir  = Path.Combine(repoRoot, "data", "images");
            var resultsDir = Path.Combine(repoRoot, "data", "results");
            var outputsDir = Path.Combine(repoRoot, "data", "outputs");

            Directory.CreateDirectory(resultsDir);
            Directory.CreateDirectory(outputsDir);

            var captionScript = Path.Combine(repoRoot, "python", "caption.py");
            var mapScript     = Path.Combine(repoRoot, "python", "map_caption.py");
            var musicScript   = Path.Combine(repoRoot, "python", "musicgen.py");
            var analyzeScript = Path.Combine(repoRoot, "python", "analyze.py");

            if (!File.Exists(captionScript))
                throw new FileNotFoundException("caption.py nicht gefunden", captionScript);
            if (!File.Exists(mapScript))
                throw new FileNotFoundException("map_caption.py nicht gefunden", mapScript);
            if (!File.Exists(musicScript))
                throw new FileNotFoundException("musicgen.py nicht gefunden", musicScript);
            if (!File.Exists(analyzeScript))
                throw new FileNotFoundException("analyze.py nicht gefunden", analyzeScript);
            if (!Directory.Exists(imagesDir))
                throw new DirectoryNotFoundException($"images-Ordner nicht gefunden: {imagesDir}");

            var venvPython = Path.Combine(repoRoot, ".venv", "Scripts", "python.exe");
            var pythonExe = File.Exists(venvPython) ? venvPython : "python";

            var allImages = Directory.EnumerateFiles(imagesDir)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (allImages.Count == 0)
            {
                Console.WriteLine($"Keine Bilder in {imagesDir} gefunden.");
                return 0;
            }

            // --- User-Auswahl ---
            Console.WriteLine("Welche Bilder willst du verarbeiten?");
            Console.WriteLine("-> 'all' für alle Bilder");
            Console.WriteLine("-> oder Dateinamen ohne Endung, z.B.: test");
            Console.WriteLine("   (mehrere möglich: test, auto, bild2)");
            Console.Write("> ");
            var input = (Console.ReadLine() ?? "").Trim();

            List<string> selectedImages;
            if (string.Equals(input, "all", StringComparison.OrdinalIgnoreCase))
            {
                selectedImages = allImages;
            }
            else
            {
                var wantedNames = input
                    .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (wantedNames.Count == 0)
                {
                    Console.WriteLine("Keine gültige Eingabe. Abbruch.");
                    return 0;
                }

                selectedImages = allImages
                    .Where(path => wantedNames.Contains(Path.GetFileNameWithoutExtension(path)))
                    .ToList();

                var foundBaseNames = selectedImages
                    .Select(p => Path.GetFileNameWithoutExtension(p))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var missing = wantedNames.Where(n => !foundBaseNames.Contains(n)).ToList();
                if (missing.Count > 0)
                {
                    Console.WriteLine("Diese Dateien wurden nicht gefunden (ohne Endung): " + string.Join(", ", missing));
                    Console.WriteLine("Verfügbare Dateien sind z.B.: " +
                        string.Join(", ", allImages.Select(p => Path.GetFileNameWithoutExtension(p)).Take(10)) +
                        (allImages.Count > 10 ? ", ..." : ""));
                }

                if (selectedImages.Count == 0)
                {
                    Console.WriteLine("Keine passenden Bilder gefunden. Abbruch.");
                    return 0;
                }
            }

            // --- Pipeline ausführen ---
            foreach (var img in selectedImages)
            {
                var name = Path.GetFileNameWithoutExtension(img);

                var captionJson = Path.Combine(resultsDir, $"{name}.json");
                var mappedJson  = Path.Combine(resultsDir, $"{name}_mapped.json");
                var outWav      = Path.Combine(outputsDir, $"{name}.wav"); // musicgen.py macht daraus _v1/_v2/_v3

                Console.WriteLine($"\n=== {name} ===");

                RunPython(pythonExe, captionScript, $"\"{img}\" \"{captionJson}\"");
                RunPython(pythonExe, mapScript,     $"\"{captionJson}\" \"{mappedJson}\"");
                RunPython(pythonExe, musicScript,   $"\"{mappedJson}\" \"{outWav}\"");

                Console.WriteLine($"OK -> {mappedJson}");
                Console.WriteLine($"WAV(s) -> {Path.Combine(outputsDir, name + "_v1.wav")} / _v2.wav / _v3.wav");
            }

            // --- ANALYSE am Ende ---
            Console.WriteLine("\n=== Analyse ===");

            RunPython(pythonExe, analyzeScript, "");

            Console.WriteLine("\nFertig ✅ (inkl. Analyse)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("\nFEHLER ❌");
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }

    static void RunPython(string pythonExe, string scriptPath, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = string.IsNullOrWhiteSpace(args)
                ? $"\"{scriptPath}\""
                : $"\"{scriptPath}\" {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null) throw new Exception("Konnte Python-Prozess nicht starten.");

        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine(stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine(stderr.Trim());

        if (p.ExitCode != 0)
            throw new Exception($"Python-Skript fehlgeschlagen: {Path.GetFileName(scriptPath)} (ExitCode {p.ExitCode})");
    }
}
