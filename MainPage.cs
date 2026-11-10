using System.IO.Compression;
using System.Text;
using CommunityToolkit.Maui.Storage;
using CriFsV2Lib;
using CriFsV2Lib.Definitions;
using CriFsV2Lib.Definitions.Structs;

namespace CPKExtractor;

public class MainPage : ContentPage
{
    private readonly Button _pickButton;
    private readonly Button _extractButton;
    private readonly Label _status;
    private readonly Label _fileList;
    private CpkFile[] _files = Array.Empty<CpkFile>();
    private string? _cpkPath;

    public MainPage()
    {
        Title = "CPK Extractor";
        BackgroundColor = Color.FromArgb("#10141D");

        _pickButton = new Button { Text = "Choisir un fichier .CPK", BackgroundColor = Color.FromArgb("#315CDB"), TextColor = Colors.White, CornerRadius = 14 };
        _extractButton = new Button { Text = "Extraire en ZIP", BackgroundColor = Color.FromArgb("#18865B"), TextColor = Colors.White, CornerRadius = 14, IsEnabled = false };
        _status = new Label { Text = "Sélectionne une archive CPK pour commencer.", TextColor = Colors.White, FontSize = 15 };
        _fileList = new Label { Text = "", TextColor = Color.FromArgb("#C4CAD5"), FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };

        _pickButton.Clicked += PickCpk;
        _extractButton.Clicked += ExtractCpk;

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 32),
                Spacing = 18,
                Children =
                {
                    new Label { Text = "CPK EXTRACTOR", FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    new Label { Text = "Ouvre et extrait les archives CRIWARE (.cpk)", FontSize = 14, TextColor = Color.FromArgb("#AAB4C5") },
                    _pickButton,
                    _status,
                    _fileList,
                    _extractButton,
                    new Label { Text = "Le ZIP contiendra les fichiers extraits et un rapport détaillé (tailles, réussites et erreurs).", FontSize = 12, TextColor = Color.FromArgb("#8792A5") }
                }
            }
        };
    }

    private async void PickCpk(object? sender, EventArgs e)
    {
        try
        {
            _pickButton.IsEnabled = false;
            _extractButton.IsEnabled = false;
            _status.Text = "Ouverture du sélecteur…";
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choisir une archive CPK",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    [DevicePlatform.Android] = new[] { "application/octet-stream", "application/x-cpk", "*/*" }
                })
            });

            if (picked is null)
            {
                _status.Text = "Aucun fichier sélectionné.";
                return;
            }

            var localPath = Path.Combine(FileSystem.CacheDirectory, $"input_{Guid.NewGuid():N}.cpk");
            await using (var source = await picked.OpenReadAsync())
            await using (var destination = File.Create(localPath))
                await source.CopyToAsync(destination);

            _status.Text = "Analyse de l'archive…";
            using (var stream = File.OpenRead(localPath))
            using (var reader = CriFsLib.Instance.CreateCpkReader(stream, false))
            {
                _files = reader.GetFiles();
            }

            _cpkPath = localPath;
            var preview = _files.Take(120).Select(f =>
            {
                var folder = (f.Directory ?? "").Replace('\\', '/').Trim('/');
                return string.IsNullOrEmpty(folder) ? f.FileName : $"{folder}/{f.FileName}";
            });
            _fileList.Text = string.Join("\n", preview) + (_files.Length > 120 ? $"\n… et {_files.Length - 120} autres fichiers" : "");
            _status.Text = $"Archive chargée : {_files.Length:N0} fichier(s). Taille CPK : {FormatBytes(new FileInfo(localPath).Length)}.";
            _extractButton.IsEnabled = _files.Length > 0;
        }
        catch (Exception ex)
        {
            _status.Text = $"Erreur : {ex.Message}";
            _fileList.Text = "";
        }
        finally
        {
            _pickButton.IsEnabled = true;
        }
    }

    private async void ExtractCpk(object? sender, EventArgs e)
    {
        if (_cpkPath is null || _files.Length == 0)
            return;

        string? zipPath = null;
        try
        {
            _extractButton.IsEnabled = false;
            _pickButton.IsEnabled = false;
            _status.Text = "Extraction en cours…";

            long totalExtractedBytes = 0;
            var successCount = 0;
            var errors = new List<string>();
            var report = new StringBuilder();
            var cpkSize = new FileInfo(_cpkPath).Length;

            zipPath = Path.Combine(FileSystem.CacheDirectory, $"CPK_Extracted_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
            using (var input = File.OpenRead(_cpkPath))
            using (var reader = CriFsLib.Instance.CreateCpkReader(input, false))
            using (var output = File.Create(zipPath))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                for (var i = 0; i < _files.Length; i++)
                {
                    var file = _files[i];
                    var folder = (file.Directory ?? "").Replace('\\', '/').Trim('/');
                    var relative = string.IsNullOrWhiteSpace(folder) ? file.FileName : $"{folder}/{file.FileName}";
                    relative = SafeZipPath(relative);

                    try
                    {
                        using var data = reader.ExtractFile(file);
                        var entry = archive.CreateEntry(relative, CompressionLevel.NoCompression);
                        using (var entryStream = entry.Open())
                            entryStream.Write(data.Span);

                        totalExtractedBytes += data.Length;
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{relative} : {ex.GetType().Name} - {ex.Message}");
                    }

                    if (i % 5 == 0 || i == _files.Length - 1)
                    {
                        var progress = (i + 1) * 100 / _files.Length;
                        _status.Text = $"Extraction : {progress}% ({i + 1:N0}/{_files.Length:N0}) — {FormatBytes(totalExtractedBytes)}";
                        await Task.Yield();
                    }
                }

                report.AppendLine("CPK EXTRACTOR - RAPPORT D'EXTRACTION");
                report.AppendLine($"Taille du CPK sélectionné : {FormatBytes(cpkSize)} ({cpkSize} octets)");
                report.AppendLine($"Fichiers détectés dans le CPK : {_files.Length}");
                report.AppendLine($"Fichiers extraits avec succès : {successCount}");
                report.AppendLine($"Fichiers en erreur : {errors.Count}");
                report.AppendLine($"Taille totale des données extraites : {FormatBytes(totalExtractedBytes)} ({totalExtractedBytes} octets)");
                report.AppendLine();
                report.AppendLine("DÉTAIL DES ERREURS");
                if (errors.Count == 0)
                    report.AppendLine("Aucune erreur signalée par la bibliothèque pendant l'extraction.");
                else
                    foreach (var error in errors)
                        report.AppendLine(error);

                var reportEntry = archive.CreateEntry("CPK_extraction_report.txt", CompressionLevel.Optimal);
                using var reportWriter = new StreamWriter(reportEntry.Open(), Encoding.UTF8);
                reportWriter.Write(report.ToString());
            }

            var zipSize = new FileInfo(zipPath).Length;
            _status.Text = $"Extraction terminée : {successCount}/{_files.Length} fichiers. Données extraites : {FormatBytes(totalExtractedBytes)}. ZIP : {FormatBytes(zipSize)}. Erreurs : {errors.Count}.";

            await using var zipStream = File.OpenRead(zipPath);
            var result = await FileSaver.Default.SaveAsync(Path.GetFileName(zipPath), zipStream, CancellationToken.None);
            _status.Text += result.IsSuccessful
                ? $" Enregistré : {result.FilePath}"
                : $" Enregistrement annulé/échoué : {result.Exception?.Message ?? "aucun détail"}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Erreur pendant l'extraction : {ex.Message}";
        }
        finally
        {
            _extractButton.IsEnabled = _files.Length > 0;
            _pickButton.IsEnabled = true;
            if (zipPath is not null && File.Exists(zipPath))
            {
                try { File.Delete(zipPath); } catch { }
            }
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "o", "Ko", "Mo", "Go", "To" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }

    private static string SafeZipPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p != "." && p != ".." && !p.Contains(':'))
            .ToArray();
        return parts.Length == 0 ? "unnamed_file" : string.Join('/', parts);
    }
}
