using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace Projektarbeit.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // --- Bindings ---
        private string _selectedImagePath = "";
        public string SelectedImageText => string.IsNullOrWhiteSpace(_selectedImagePath) ? "(kein Bild gewählt)" : _selectedImagePath;

        private string _statusText = "Bereit";
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

        private string _logText = "";
        public string LogText { get => _logText; set { _logText = value; OnPropertyChanged(); } }

        public ObservableCollection<string> AudioFiles { get; } = new();
        private string? _selectedAudioFile;
        public string? SelectedAudioFile { get => _selectedAudioFile; set { _selectedAudioFile = value; OnPropertyChanged(); } }

        private readonly MediaPlayer _player = new();
        private string? _lastReportPath;

        // Repo paths
        private readonly string _repoRoot;
        private readonly string _imagesDir;
        private readonly string _resultsDir;
        private readonly string _outputsDir;

        private readonly string _pythonExe;
        private readonly string _captionScript;
        private readonly string _mapScript;
        private readonly string _musicScript;
        private readonly string _analyzeScript;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            _repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

            _imagesDir = Path.Combine(_repoRoot, "data", "images");
            _resultsDir = Path.Combine(_repoRoot, "data", "results");
            _outputsDir = Path.Combine(_repoRoot, "data", "outputs");

            Directory.CreateDirectory(_imagesDir);
            Directory.CreateDirectory(_resultsDir);
            Directory.CreateDirectory(_outputsDir);

            _captionScript = Path.Combine(_repoRoot, "python", "caption.py");
            _mapScript = Path.Combine(_repoRoot, "python", "map_caption.py");
            _musicScript = Path.Combine(_repoRoot, "python", "musicgen.py");
            _analyzeScript = Path.Combine(_repoRoot, "python", "analyze.py");

            var venvPython = Path.Combine(_repoRoot, ".venv", "Scripts", "python.exe");
            _pythonExe = File.Exists(venvPython) ? venvPython : "python";

            AppendLog($"RepoRoot: {_repoRoot}");
        }

        private void PickImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Images|*.jpg;*.jpeg;*.png;*.webp",
                Title = "Bild auswählen"
            };

            if (dlg.ShowDialog() == true)
            {
                _selectedImagePath = dlg.FileName;
                OnPropertyChanged(nameof(SelectedImageText));
                StatusText = "Bild gewählt";
                AppendLog($"Bild: {_selectedImagePath}");
            }
        }

        private async void RunPipeline_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_selectedImagePath) || !File.Exists(_selectedImagePath))
            {
                MessageBox.Show("Bitte zuerst ein Bild auswählen.");
                return;
            }

            try
            {
                StatusText = "Pipeline läuft…";
                AppendLog("=== Pipeline Start ===");

                // Bild in data/images kopieren
                var name = Path.GetFileNameWithoutExtension(_selectedImagePath);
                var destImg = Path.Combine(_imagesDir, Path.GetFileName(_selectedImagePath));

                // Wenn das Bild schon im data/images-Ordner liegt dann doch nicht
                var srcFull = Path.GetFullPath(_selectedImagePath);
                var destFull = Path.GetFullPath(destImg);

                if (!string.Equals(srcFull, destFull, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(_selectedImagePath, destImg, overwrite: true);
                }
                else
                {
                    AppendLog("Bild liegt bereits in data/images, Kopieren übersprungen.");
                }
                var runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                var captionJson = Path.Combine(_resultsDir, $"{name}.json");
                var mappedJson = Path.Combine(_resultsDir, $"{name}_mapped.json");
                var outWavBase = Path.Combine(_outputsDir, $"{name}.wav"); // musicgen macht _v1/_v2/_v3

                // Python-Skripte nacheinander in asycn
                await RunPythonAsync(_captionScript, $"\"{destImg}\" \"{captionJson}\"");
                await RunPythonAsync(_mapScript, $"\"{captionJson}\" \"{mappedJson}\"");
                await RunPythonAsync(_musicScript, $"\"{mappedJson}\" \"{outWavBase}\"");
                await RunPythonAsync(_analyzeScript, $"{name} {runId}"); // analysiert alle outputs

                _lastReportPath = Path.Combine(_resultsDir, $"comparison_report_{name}_{runId}.md");

                // WAVs ins Dropdown laden
                LoadAudioChoices(name);

                // Analyse-Report anzeigen
                if (File.Exists(_lastReportPath))
                {
                    AppendLog("\n=== Analyse-Report ===\n" + File.ReadAllText(_lastReportPath));
                }

                StatusText = "Fertig";
                AppendLog("=== Pipeline Ende ===");
            }
            catch (Exception ex)
            {
                StatusText = "Fehler";
                AppendLog("FEHLER: " + ex);
                MessageBox.Show(ex.Message);
            }
        }

        private void LoadAudioChoices(string baseName)
        {
            AudioFiles.Clear();
            var files = Directory.EnumerateFiles(_outputsDir, $"{baseName}_v*.wav")
                                 .OrderBy(p => p)
                                 .ToList();

            foreach (var f in files)
                AudioFiles.Add(f);

            SelectedAudioFile = AudioFiles.FirstOrDefault();
            AppendLog($"Gefundene WAVs: {AudioFiles.Count}");
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedAudioFile) || !File.Exists(SelectedAudioFile))
            {
                MessageBox.Show("Keine WAV ausgewählt/gefunden.");
                return;
            }

            _player.Open(new Uri(SelectedAudioFile));
            _player.Play();
            AppendLog("Play: " + SelectedAudioFile);
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _player.Stop();
            AppendLog("Stop");
        }

        private void SaveWav_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedAudioFile) || !File.Exists(SelectedAudioFile))
            {
                MessageBox.Show("Keine WAV ausgewählt.");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "WAV|*.wav",
                FileName = Path.GetFileName(SelectedAudioFile)
            };

            if (dlg.ShowDialog() == true)
            {
                File.Copy(SelectedAudioFile, dlg.FileName, overwrite: true);
                AppendLog("WAV gespeichert: " + dlg.FileName);
            }
        }

        private void SaveAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_lastReportPath) || !File.Exists(_lastReportPath))
            {
                MessageBox.Show("Report nicht gefunden. Erst Pipeline laufen lassen.");
                return;
            }

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Markdown|*.md",
                FileName = Path.GetFileName(_lastReportPath)
            };

            if (dlg.ShowDialog() == true)
            {
                File.Copy(_lastReportPath, dlg.FileName, overwrite: true);
                AppendLog("Report gespeichert: " + dlg.FileName);
            }

        }

        private Task RunPythonAsync(string scriptPath, string args)
        {
            return Task.Run(() =>
            {
                if (!File.Exists(scriptPath))
                    throw new FileNotFoundException("Python-Skript nicht gefunden", scriptPath);

                var psi = new ProcessStartInfo
                {
                    FileName = _pythonExe,
                    Arguments = string.IsNullOrWhiteSpace(args) ? $"\"{scriptPath}\"" : $"\"{scriptPath}\" {args}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _repoRoot
                };

                using var p = Process.Start(psi);
                if (p == null) throw new Exception("Konnte Python-Prozess nicht starten.");

                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (!string.IsNullOrWhiteSpace(stdout)) AppendLog(stdout.Trim());
                if (!string.IsNullOrWhiteSpace(stderr)) AppendLog(stderr.Trim());

                if (p.ExitCode != 0)
                    throw new Exception($"Python fehlgeschlagen: {Path.GetFileName(scriptPath)} (ExitCode {p.ExitCode})");
            });
        }

        private void AppendLog(string text)
        {
            // UI-thread safe
            Dispatcher.Invoke(() =>
            {
                LogText += (LogText.Length == 0 ? "" : "\n") + text;
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
