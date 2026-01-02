using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Windows;

namespace EasyInstallerV2;

public partial class MainWindow : Window
{
    const string BASE_URL = "https://manifest.simplyblk.xyz";
    const int CHUNK_SIZE = 67108864;

    static HttpClient httpClient = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadVersions();
    }

    async void LoadVersions()
    {
        var versions = await GetVersionsAsync();
        VersionBox.ItemsSource = versions;
        VersionBox.SelectedIndex = 0;
    }

    async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (VersionBox.SelectedItem == null || string.IsNullOrEmpty(PathBox.Text))
            return;

        string version = VersionBox.SelectedItem.ToString()!.Split("-")[1];
        var manifest = await GetManifestAsync(version);

        await Download(manifest, version, PathBox.Text, UpdateProgress);

        StatusText.Text = "Finished!";
    }

    void UpdateProgress(long done, long total)
    {
        Dispatcher.Invoke(() =>
        {
            double percent = (double)done / total * 100;
            Progress.Value = percent;
            StatusText.Text = $"{FormatBytes(done)} / {FormatBytes(total)} ({percent:0.00}%)";
        });
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "Select Folder",
            Filter = "Folder|.",
        };

        if (dlg.ShowDialog() == true)
            PathBox.Text = Path.GetDirectoryName(dlg.FileName);
    }

    // ---------------- LOGIC ----------------

    static async Task Download(ManifestFile manifest, string version, string path,
        Action<long, long> progress)
    {
        long total = manifest.Size;
        long completed = 0;

        Directory.CreateDirectory(path);

        foreach (var file in manifest.Chunks)
        {
            string outPath = Path.Combine(path, file.File);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

            using FileStream fs = File.OpenWrite(outPath);
            WebClient wc = new();

            foreach (int id in file.ChunksIds)
            {
                byte[] data = await wc.DownloadDataTaskAsync($"{BASE_URL}/{version}/{id}.chunk");
                using var ms = new MemoryStream(data);
                using var gz = new GZipStream(ms, CompressionMode.Decompress);

                byte[] buffer = new byte[CHUNK_SIZE];
                int read;

                while ((read = await gz.ReadAsync(buffer)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, read));
                    completed += read;
                    progress(completed, total);
                }
            }
        }
    }

    static async Task<string[]> GetVersionsAsync()
    {
        var json = await httpClient.GetStringAsync(BASE_URL + "/versions.json");
        return JsonSerializer.Deserialize<string[]>(json)!;
    }

    static async Task<ManifestFile> GetManifestAsync(string version)
    {
        return await httpClient.GetFromJsonAsync<ManifestFile>(
            $"{BASE_URL}/{version}/{version}.manifest")!;
    }

    static string FormatBytes(long b)
    {
        string[] suf = { "B", "KB", "MB", "GB" };
        int i = 0;
        double d = b;
        while (d >= 1024 && i < suf.Length - 1)
        {
            d /= 1024;
            i++;
        }
        return $"{d:0.##} {suf[i]}";
    }
}
