using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using HKBuild;
using HKX2E;
using Microsoft.Win32;

namespace HKBuildUI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // =========================================================================
    //  DECOMPILE TAB  —  HKX → YAML
    // =========================================================================

    private void DecompileDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void DecompileDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var path = files[0];
        if (File.Exists(path) && path.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase))
        {
            DecompileInput.Text = path;
            SetDefaultDecompileOutput(path);
        }
        else
        {
            AppendLog(DecompileLog, "Drop a .hkx file (not a folder).");
        }
        e.Handled = true;
    }

    private void DecompileInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var path = DecompileInput.Text.Trim();
        DecompileBtn.IsEnabled = File.Exists(path) &&
                                 path.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase);
    }

    private void BrowseHkx_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select HKX file",
            Filter = "HKX files (*.hkx)|*.hkx|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        DecompileInput.Text = dlg.FileName;
        SetDefaultDecompileOutput(dlg.FileName);
    }

    private void BrowseDecompileOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description         = "Select output folder for YAML source tree",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (DecompileOutput.Text.Trim() is { Length: > 0 } cur)
            dlg.InitialDirectory = Path.GetDirectoryName(cur) ?? cur;

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DecompileOutput.Text = dlg.SelectedPath;
    }

    private void DecompileDefault_Click(object sender, RoutedEventArgs e) =>
        SetDefaultDecompileOutput(DecompileInput.Text.Trim());

    private void SetDefaultDecompileOutput(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath)) return;
        // Default: <inputname>.hkx/ folder alongside the source file
        var dir = Path.GetDirectoryName(inputPath) ?? "";
        var name = Path.GetFileName(inputPath);          // e.g. "defaultmale.hkx"
        DecompileOutput.Text = Path.Combine(dir, name); // folder named "defaultmale.hkx"
    }

    private void Decompile_Click(object sender, RoutedEventArgs e)
    {
        var inputPath = DecompileInput.Text.Trim();
        var outputDir = DecompileOutput.Text.Trim();

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            SetDefaultDecompileOutput(inputPath);
            outputDir = DecompileOutput.Text.Trim();
        }
        if (string.IsNullOrWhiteSpace(outputDir)) return;

        DecompileBtn.IsEnabled = false;
        DecompileLog.Clear();
        AppendLog(DecompileLog, $"Input:  {inputPath}");
        AppendLog(DecompileLog, $"Output: {outputDir}");
        AppendLog(DecompileLog, "");

        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                Log(DecompileLog, "Reading HKX binary…");
                var header = HKXHeader.SkyrimSE();
                hkRootLevelContainer root;
                using (var fs = File.OpenRead(inputPath))
                {
                    var br  = new BinaryReaderEx(fs);
                    var des = new PackFileDeserializer();
                    root = (hkRootLevelContainer)des.Deserialize(br);
                }

                Log(DecompileLog, "Serializing to XML…");
                string xmlContent;
                using (var ms = new System.IO.MemoryStream())
                {
                    var xs = new HavokXmlSerializer();
                    xs.Serialize(root, header, ms);
                    xmlContent = System.Text.Encoding.ASCII.GetString(ms.ToArray());
                }

                Log(DecompileLog, "Extracting YAML source tree…");
                BehaviorExtractor.Extract(xmlContent, outputDir, force: true,
                    log: msg => Log(DecompileLog, msg));

                Log(DecompileLog, "");
                Log(DecompileLog, "✓ Decompile complete.");
            }
            catch (Exception ex)
            {
                Log(DecompileLog, $"");
                Log(DecompileLog, $"✗ Error: {ex.Message}");
            }
            finally
            {
                Dispatcher.Invoke(() => DecompileBtn.IsEnabled = true);
            }
        });
    }

    // =========================================================================
    //  COMPILE TAB  —  YAML → HKX
    // =========================================================================

    private void CompileDrop_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CompileDrop_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var path = files[0];
        if (Directory.Exists(path))
        {
            CompileInput.Text = path;
            SetDefaultCompileOutput(path);
        }
        else
        {
            AppendLog(CompileLog, "Drop a source folder (e.g. defaultmale.hkx/).");
        }
        e.Handled = true;
    }

    private void CompileInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var path = CompileInput.Text.Trim();
        CompileBtn.IsEnabled = Directory.Exists(path) &&
            (File.Exists(Path.Combine(path, "behavior.yaml")) ||
             File.Exists(Path.Combine(path, "character.yaml")));
    }

    private void BrowseSourceFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description         = "Select YAML source folder",
            UseDescriptionForTitle = true
        };
        if (CompileInput.Text.Trim() is { Length: > 0 } cur)
            dlg.InitialDirectory = Directory.Exists(cur) ? cur : (Path.GetDirectoryName(cur) ?? "");

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            CompileInput.Text = dlg.SelectedPath;
            SetDefaultCompileOutput(dlg.SelectedPath);
        }
    }

    private void BrowseCompileOutput_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title  = "Save HKX file",
            Filter = "HKX files (*.hkx)|*.hkx|All files (*.*)|*.*"
        };
        if (CompileOutput.Text.Trim() is { Length: > 0 } cur)
        {
            dlg.InitialDirectory = Path.GetDirectoryName(cur) ?? "";
            dlg.FileName = Path.GetFileName(cur);
        }
        if (dlg.ShowDialog() == true)
            CompileOutput.Text = dlg.FileName;
    }

    private void CompileDefault_Click(object sender, RoutedEventArgs e) =>
        SetDefaultCompileOutput(CompileInput.Text.Trim());

    private void SetDefaultCompileOutput(string sourceDir)
    {
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir)) return;
        // Source folder is e.g. "path/defaultmale.hkx/" — output is "path/defaultmale.hkx"
        var parent = Path.GetDirectoryName(sourceDir.TrimEnd(Path.DirectorySeparatorChar,
                                                              Path.AltDirectorySeparatorChar)) ?? "";
        var folderName = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar,
                                                             Path.AltDirectorySeparatorChar));
        CompileOutput.Text = Path.Combine(parent, folderName);
    }

    private void Compile_Click(object sender, RoutedEventArgs e)
    {
        var sourceDir  = CompileInput.Text.Trim();
        var outputPath = CompileOutput.Text.Trim();

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            SetDefaultCompileOutput(sourceDir);
            outputPath = CompileOutput.Text.Trim();
        }
        if (string.IsNullOrWhiteSpace(outputPath)) return;

        CompileBtn.IsEnabled = false;
        CompileLog.Clear();
        AppendLog(CompileLog, $"Source: {sourceDir}");
        AppendLog(CompileLog, $"Output: {outputPath}");
        AppendLog(CompileLog, "");

        var capturedOutput = outputPath;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                bool isCharacter = File.Exists(Path.Combine(sourceDir, "character.yaml"));
                bool isBehavior  = File.Exists(Path.Combine(sourceDir, "behavior.yaml"));

                string xml;
                if (isCharacter)
                {
                    Log(CompileLog, "Reading character YAML…");
                    var data    = CharacterReader.Load(sourceDir);
                    var emitter = new CharacterXmlEmitter(data);
                    xml = emitter.Emit();
                    Log(CompileLog, "Emitted character XML.");
                }
                else if (isBehavior)
                {
                    Log(CompileLog, "Reading behavior YAML…");
                    var data    = BehaviorReader.Load(sourceDir);
                    var emitter = new BehaviorXmlEmitter(data);
                    xml = emitter.Emit();
                    Log(CompileLog, "Emitted behavior XML.");
                }
                else
                {
                    Log(CompileLog, "✗ Source folder contains neither behavior.yaml nor character.yaml.");
                    Dispatcher.Invoke(() => CompileBtn.IsEnabled = true);
                    return;
                }

                xml = xml.Replace("\r\n", "\n");
                Log(CompileLog, "Packing to HKX binary…");

                Exception? packEx = null;
                var thread = new System.Threading.Thread(() =>
                {
                    try
                    {
                        var header = HKXHeader.SkyrimSE();
                        hkRootLevelContainer packRoot;
                        using (var ms = new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes(xml)))
                        {
                            var des = new HavokXmlDeserializer();
                            packRoot = (hkRootLevelContainer)des.Deserialize(ms, header);
                        }

                        var outDir = Path.GetDirectoryName(capturedOutput);
                        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                        using (var fs = File.Create(capturedOutput))
                        {
                            var bw  = new BinaryWriterEx(fs);
                            var ser = new PackFileSerializer();
                            ser.Serialize(packRoot, bw, header);
                        }
                    }
                    catch (Exception ex) { packEx = ex; }
                }, 256 * 1024 * 1024); // 256 MB stack for deep recursion

                thread.Start();
                thread.Join();

                if (packEx != null) throw packEx;

                var size = new FileInfo(capturedOutput).Length;
                Log(CompileLog, "");
                Log(CompileLog, $"✓ Compile complete.  ({size:N0} bytes → {capturedOutput})");
            }
            catch (Exception ex)
            {
                Log(CompileLog, "");
                Log(CompileLog, $"✗ Error: {ex.Message}");
            }
            finally
            {
                Dispatcher.Invoke(() => CompileBtn.IsEnabled = true);
            }
        });
    }

    // =========================================================================
    //  Shared helpers
    // =========================================================================

    private void Log(TextBox box, string message) =>
        Dispatcher.Invoke(() => AppendLog(box, message));

    private static void AppendLog(TextBox box, string message)
    {
        box.AppendText(message + Environment.NewLine);
        box.ScrollToEnd();
    }
}
