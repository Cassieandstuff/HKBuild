using HKBuild;
using HKX2E;

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

// Route to subcommand or default compile mode.
if (args[0] == "convert")
    return RunConvert(args);
if (args[0] == "pack")
    return RunPack(args);
if (args[0] == "extract")
    return RunExtract(args);
if (args[0] == "convert-all")
    return RunConvertAll(args);
if (args[0] == "verify")
    return RunVerify(args);
if (args[0] == "graph")
    return RunGraph(args);

return RunCompile(args);

// ── Compile (default): YAML source → Havok packfile XML ──

static int RunCompile(string[] args)
{
    var inputDir = args[0];
    string? outputPath = null;

    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "-o" && i + 1 < args.Length)
            outputPath = args[++i];
    }

    try
    {
        Console.WriteLine($"HKBuild — Havok Behavior Compiler");
        Console.WriteLine($"Reading: {inputDir}");
        Console.WriteLine();

        bool isCharacter = File.Exists(Path.Combine(inputDir, "character.yaml"));
        bool isBehavior = File.Exists(Path.Combine(inputDir, "behavior.yaml"));

        string xml;
        string defaultName;

        if (isCharacter)
        {
            var data = CharacterReader.Load(inputDir);
            var emitter = new CharacterXmlEmitter(data);
            xml = emitter.Emit();
            defaultName = data.Character.Character.Name;
        }
        else if (isBehavior)
        {
            var data = BehaviorReader.Load(inputDir);
            var emitter = new BehaviorXmlEmitter(data);
            xml = emitter.Emit();
            defaultName = Path.GetFileName(inputDir);
        }
        else
        {
            Console.Error.WriteLine("ERROR: Directory contains neither character.yaml nor behavior.yaml");
            return 1;
        }

        xml = xml.Replace("\r\n", "\n");

        if (outputPath != null)
        {
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllBytes(outputPath, System.Text.Encoding.ASCII.GetBytes(xml));
            Console.WriteLine($"Wrote: {outputPath} ({new FileInfo(outputPath).Length:N0} bytes)");
        }
        else
        {
            var defaultOutput = $"{defaultName}.xml";
            File.WriteAllBytes(defaultOutput, System.Text.Encoding.ASCII.GetBytes(xml));
            Console.WriteLine($"Wrote: {defaultOutput} ({new FileInfo(defaultOutput).Length:N0} bytes)");
        }

        return 0;
    }
    catch (Exception ex)
    {
        ReportError(ex);
        return 1;
    }
}

// ── Graph: YAML source → interactive HTML visualization ──

static int RunGraph(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: hkbuild graph <source-directory> [-o <output.html>]");
        return 1;
    }

    var inputDir = args[1];
    string? outputPath = null;

    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "-o" && i + 1 < args.Length)
            outputPath = args[++i];
    }

    try
    {
        Console.WriteLine("HKBuild — Graph Visualizer");
        Console.WriteLine($"Reading: {inputDir}");

        if (!File.Exists(Path.Combine(inputDir, "behavior.yaml")))
        {
            Console.Error.WriteLine("ERROR: Directory does not contain behavior.yaml (graph only works for behaviors).");
            return 1;
        }

        var data = BehaviorReader.Load(inputDir);
        var emitter = new GraphEmitter(data);
        var html = emitter.Emit();

        outputPath ??= Path.GetFileName(inputDir).Replace(".hkx", "") + "_graph.html";

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(outputPath, html);
        Console.WriteLine($"Wrote: {outputPath} ({new FileInfo(outputPath).Length:N0} bytes)");

        return 0;
    }
    catch (Exception ex)
    {
        ReportError(ex);
        return 1;
    }
}

// ── Convert: binary HKX → XML via HKX2E ──

static int RunConvert(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: hkbuild convert <input.hkx> [-o <output.xml>]");
        return 1;
    }

    var inputFile = args[1];
    string? outputPath = null;

    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "-o" && i + 1 < args.Length)
            outputPath = args[++i];
    }

    if (!File.Exists(inputFile))
    {
        Console.Error.WriteLine($"ERROR: File not found: {inputFile}");
        return 1;
    }

    outputPath ??= Path.ChangeExtension(inputFile, ".xml");

    try
    {
        Console.WriteLine("HKBuild — HKX Binary → XML Converter (via HKX2E)");
        Console.WriteLine($"Reading: {inputFile}");

        var header = HKXHeader.SkyrimSE();

        hkRootLevelContainer root;
        using (var rs = File.OpenRead(inputFile))
        {
            var br = new BinaryReaderEx(rs);
            var des = new PackFileDeserializer();
            root = (hkRootLevelContainer)des.Deserialize(br);
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using (var ws = File.Create(outputPath))
        {
            var xs = new HavokXmlSerializer();
            xs.Serialize(root, header, ws);
        }

        Console.WriteLine($"Wrote: {outputPath} ({new FileInfo(outputPath).Length:N0} bytes)");
        return 0;
    }
    catch (Exception ex)
    {
        ReportError(ex);
        return 1;
    }
}

// ── Pack: XML → binary HKX via HKX2E ──

static int RunPack(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: hkbuild pack <input.xml> [-o <output.hkx>]");
        return 1;
    }

    var inputFile = args[1];
    string? outputPath = null;

    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "-o" && i + 1 < args.Length)
            outputPath = args[++i];
    }

    if (!File.Exists(inputFile))
    {
        Console.Error.WriteLine($"ERROR: File not found: {inputFile}");
        return 1;
    }

    outputPath ??= Path.ChangeExtension(inputFile, ".hkx");

    try
    {
        Console.WriteLine("HKBuild — XML → HKX Binary Packer (via HKX2E)");
        Console.WriteLine($"Reading: {inputFile}");

        // Run on a thread with a large stack to handle deeply nested XML (e.g. mt_behavior).
        int result = 0;
        Exception? threadEx = null;
        var capturedInput = inputFile;
        var capturedOutput = outputPath;
        var thread = new Thread(() =>
        {
            try
            {
                var header = HKXHeader.SkyrimSE();

                hkRootLevelContainer root;
                using (var rs = File.OpenRead(capturedInput))
                {
                    var des = new HavokXmlDeserializer();
                    root = (hkRootLevelContainer)des.Deserialize(rs, header);
                }

                var dir = Path.GetDirectoryName(capturedOutput);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                using (var ws = File.Create(capturedOutput))
                {
                    var bw = new BinaryWriterEx(ws);
                    var ser = new PackFileSerializer();
                    ser.Serialize(root, bw, header);
                }

                Console.WriteLine($"Wrote: {capturedOutput} ({new FileInfo(capturedOutput).Length:N0} bytes)");
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        }, 256 * 1024 * 1024); // 256 MB stack

        thread.Start();
        thread.Join();
        if (threadEx != null)
        {
            ReportError(threadEx);
            return 1;
        }
        return result;
    }
    catch (Exception ex)
    {
        ReportError(ex);
        return 1;
    }
}

// ── Extract: XML → YAML source tree

static int RunExtract(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: hkbuild extract <behavior-name> [--force] [--xml-dir <dir>] [--out-base <dir>]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Extracts a behavior XML into the YAML source tree format for compilation.");
        Console.Error.WriteLine("The behavior name can be with or without the 'behavior' suffix.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Examples:");
        Console.Error.WriteLine("  hkbuild extract bashbehavior");
        Console.Error.WriteLine("  hkbuild extract 1hm_locomotion --force");
        Console.Error.WriteLine("  hkbuild extract 0_master --xml-dir \".reference\\custom\\behaviors\" --out-base \"behavior_src\\custom\"");
        return 1;
    }

    var name = args[1];
    bool force = false;
    string? xmlDir = null;
    string? outBase = null;

    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] is "--force" or "-f")
            force = true;
        else if (args[i] == "--xml-dir" && i + 1 < args.Length)
            xmlDir = args[++i];
        else if (args[i] == "--out-base" && i + 1 < args.Length)
            outBase = args[++i];
    }

    // Resolve the XML file path (mirrors PS1 search order: hkx2e → hkxcmd).
    var hkx2eDir = Path.Combine(".reference", "vanilla skyrim behavior source",
                                "xml", "meshes", "actors", "character", "behaviors");
    var hkxcmdDir = Path.Combine(".reference", "Destructible Behavior XMLs",
                                 "character", "behaviors");
    var defaultOutBase = outBase ?? Path.Combine("behavior_src", "vanilla", "character", "behaviors");

    string? xmlPath = null;
    string? outDir  = null;

    if (xmlDir != null)
    {
        foreach (var candidate in new[] { name, $"{name}behavior" })
        {
            var p = Path.Combine(xmlDir, candidate + ".xml");
            if (File.Exists(p))
            {
                xmlPath = p;
                outDir  = Path.Combine(defaultOutBase, candidate + ".hkx");
                break;
            }
        }
    }
    else
    {
        foreach (var dir in new[] { hkx2eDir, hkxcmdDir })
        {
            foreach (var candidate in new[] { name, $"{name}behavior" })
            {
                var p = Path.Combine(dir, candidate + ".xml");
                if (File.Exists(p))
                {
                    xmlPath = p;
                    outDir  = Path.Combine(defaultOutBase, candidate + ".hkx");
                    break;
                }
            }
            if (xmlPath != null) break;
        }
    }

    if (xmlPath == null)
    {
        Console.Error.WriteLine($"ERROR: Cannot find XML for '{name}'.");
        Console.Error.WriteLine("Searched hkx2e and hkxcmd reference directories.");
        return 1;
    }

    Console.WriteLine("HKBuild — Behavior Extraction (XML → YAML)");
    Console.WriteLine($"Source:  {xmlPath}");
    Console.WriteLine($"Output:  {outDir}");
    Console.WriteLine();

    try
    {
        var xmlContent = File.ReadAllText(xmlPath);
        BehaviorExtractor.Extract(xmlContent, outDir!, force, Console.WriteLine);
        return 0;
    }
    catch (Exception ex)
    {
        ReportError(ex);
        return 1;
    }
}

// ── Convert-All: batch convert all vanilla HKX → XML ──

static int RunConvertAll(string[] args)
{
    bool force = args.Any(a => a is "--force" or "-f");
    string binRoot = ".reference\\vanilla skyrim behavior source\\binaries\\meshes\\actors";
    string xmlRoot = ".reference\\vanilla skyrim behavior source\\xml\\meshes\\actors";

    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] == "--bin-root" && i + 1 < args.Length)
            binRoot = args[++i];
        else if (args[i] == "--xml-root" && i + 1 < args.Length)
            xmlRoot = args[++i];
    }

    if (!Directory.Exists(binRoot))
    {
        Console.Error.WriteLine($"ERROR: Binary source not found: {binRoot}");
        Console.Error.WriteLine("Usage: hkbuild convert-all [--force] [--bin-root <dir>] [--xml-root <dir>]");
        return 1;
    }

    Console.WriteLine("HKBuild — Batch HKX → XML Converter");
    Console.WriteLine($"Source: {binRoot}");
    Console.WriteLine($"Output: {xmlRoot}");
    Console.WriteLine();

    int success = 0, failed = 0, skipped = 0;

    var hkxFiles = Directory.EnumerateFiles(binRoot, "*.hkx", SearchOption.AllDirectories)
        .Where(f =>
        {
            var rel = Path.GetRelativePath(binRoot, f).Replace('/', '\\');
            // Skip animations and first person
            if (rel.Contains("\\animations\\", StringComparison.OrdinalIgnoreCase)) return false;
            if (rel.Contains("\\animations", StringComparison.OrdinalIgnoreCase) && rel.EndsWith("\\")) return false;
            if (rel.Contains("\\_1stperson\\", StringComparison.OrdinalIgnoreCase)) return false;

            var dirName = Path.GetFileName(Path.GetDirectoryName(f)) ?? "";
            // Behaviors, characters, character assets, skeleton files
            if (dirName.Equals("behaviors", StringComparison.OrdinalIgnoreCase)) return true;
            if (dirName.Equals("characters", StringComparison.OrdinalIgnoreCase)) return true;
            if (dirName.StartsWith("character assets", StringComparison.OrdinalIgnoreCase)) return true;
            if (dirName.Equals("characterassets", StringComparison.OrdinalIgnoreCase)) return true;

            // Project files: HKX in creature root folders
            var parentDir = Path.GetDirectoryName(Path.GetDirectoryName(f));
            if (parentDir != null)
            {
                var parentName = Path.GetFileName(parentDir);
                if (parentName != null && !dirName.Equals("behaviors", StringComparison.OrdinalIgnoreCase)
                    && !dirName.Equals("characters", StringComparison.OrdinalIgnoreCase)
                    && !dirName.StartsWith("character assets", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if parent or grandparent is "actors"
                    if (parentName.Equals("actors", StringComparison.OrdinalIgnoreCase))
                        return true;
                    var grandparent = Path.GetFileName(Path.GetDirectoryName(parentDir));
                    if (grandparent != null && grandparent.Equals("actors", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        })
        .ToList();

    Console.WriteLine($"Found {hkxFiles.Count} HKX files to convert");
    Console.WriteLine();

    var header = HKXHeader.SkyrimSE();

    foreach (var hkxFile in hkxFiles)
    {
        var relPath = Path.GetRelativePath(binRoot, hkxFile);
        var xmlPath = Path.Combine(xmlRoot, Path.ChangeExtension(relPath, ".xml"));

        if (File.Exists(xmlPath) && !force)
        {
            skipped++;
            continue;
        }

        Console.Write($"  Converting: {relPath}");

        try
        {
            var xmlDir = Path.GetDirectoryName(xmlPath);
            if (!string.IsNullOrEmpty(xmlDir))
                Directory.CreateDirectory(xmlDir);

            hkRootLevelContainer root;
            using (var rs = File.OpenRead(hkxFile))
            {
                var br = new BinaryReaderEx(rs);
                var des = new PackFileDeserializer();
                root = (hkRootLevelContainer)des.Deserialize(br);
            }

            using (var ws = File.Create(xmlPath))
            {
                var xs = new HavokXmlSerializer();
                xs.Serialize(root, header, ws);
            }

            var size = new FileInfo(xmlPath).Length;
            Console.WriteLine($" -> {size / 1024}KB");
            success++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($" FAILED: {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Done: {success} converted, {skipped} skipped, {failed} failed (of {hkxFiles.Count} total)");
    return failed > 0 ? 1 : 0;
}

// ── Verify: compile + pack + binary diff against vanilla ──

static int RunVerify(string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: hkbuild verify <source-directory> [--ref <vanilla.hkx>]");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Compiles a YAML source directory to XML, packs to HKX, and compares");
        Console.Error.WriteLine("against a reference vanilla binary. Reports size match and byte diffs.");
        Console.Error.WriteLine();
        Console.Error.WriteLine("If --ref is not specified, looks for the reference binary at:");
        Console.Error.WriteLine("  .reference/vanilla skyrim behavior source/binaries/meshes/actors/character/behaviors/<name>.hkx");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Examples:");
        Console.Error.WriteLine("  hkbuild verify behavior_src/vanilla/character/behaviors/bashbehavior.hkx");
        Console.Error.WriteLine("  hkbuild verify behavior_src/vanilla/character/behaviors/1hm_locomotion.hkx --ref vanilla/1hm_locomotion.hkx");
        return 1;
    }

    var inputDir = args[1];
    string? refPath = null;

    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "--ref" && i + 1 < args.Length)
            refPath = args[++i];
    }

    if (!Directory.Exists(inputDir))
    {
        Console.Error.WriteLine($"ERROR: Source directory not found: {inputDir}");
        return 1;
    }

    // Auto-detect reference binary path from the source directory name.
    if (refPath == null)
    {
        var dirName = Path.GetFileName(inputDir); // e.g. "bashbehavior.hkx"
        var behaviorName = dirName.EndsWith(".hkx", StringComparison.OrdinalIgnoreCase)
            ? dirName[..^4] : dirName;
        refPath = Path.Combine(".reference", "vanilla skyrim behavior source", "binaries",
            "meshes", "actors", "character", "behaviors", behaviorName + ".hkx");
    }

    bool hasRef = File.Exists(refPath);

    Console.WriteLine("HKBuild — Verify (Compile + Pack + Binary Diff)");
    Console.WriteLine($"Source:    {inputDir}");
    Console.WriteLine($"Reference: {(hasRef ? refPath : "(not found)")}");
    Console.WriteLine();

    try
    {
        // Step 1: Compile YAML → XML
        Console.Write("  Compiling YAML → XML...");
        bool isCharacter = File.Exists(Path.Combine(inputDir, "character.yaml"));
        bool isBehavior = File.Exists(Path.Combine(inputDir, "behavior.yaml"));

        string xml;
        if (isCharacter)
        {
            var data = CharacterReader.Load(inputDir);
            var emitter = new CharacterXmlEmitter(data);
            xml = emitter.Emit();
        }
        else if (isBehavior)
        {
            var data = BehaviorReader.Load(inputDir);
            var emitter = new BehaviorXmlEmitter(data);
            xml = emitter.Emit();
        }
        else
        {
            Console.Error.WriteLine(" ERROR: No character.yaml or behavior.yaml found");
            return 1;
        }

        xml = xml.Replace("\r\n", "\n");

        var tempDir = Path.Combine(Path.GetTempPath(), "hkbuild_verify");
        Directory.CreateDirectory(tempDir);
        var xmlPath = Path.Combine(tempDir, "verify.xml");
        var hkxPath = Path.Combine(tempDir, "verify.hkx");

        File.WriteAllBytes(xmlPath, System.Text.Encoding.ASCII.GetBytes(xml));
        Console.WriteLine($" OK ({new FileInfo(xmlPath).Length:N0} bytes)");

        // Step 2: Pack XML → HKX (on large-stack thread for deeply nested XML)
        Console.Write("  Packing XML → HKX...");
        Exception? packEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var header = HKXHeader.SkyrimSE();
                hkRootLevelContainer root;
                using (var rs = File.OpenRead(xmlPath))
                {
                    var des = new HavokXmlDeserializer();
                    root = (hkRootLevelContainer)des.Deserialize(rs, header);
                }
                using (var ws = File.Create(hkxPath))
                {
                    var bw = new BinaryWriterEx(ws);
                    var ser = new PackFileSerializer();
                    ser.Serialize(root, bw, header);
                }
            }
            catch (Exception ex) { packEx = ex; }
        }, 256 * 1024 * 1024);
        thread.Start();
        thread.Join();

        if (packEx != null)
        {
            Console.Error.WriteLine($" ERROR: {packEx.Message}");
            return 1;
        }

        var ourSize = new FileInfo(hkxPath).Length;
        Console.WriteLine($" OK ({ourSize:N0} bytes)");

        // Step 3: Binary diff
        if (!hasRef)
        {
            Console.WriteLine();
            Console.WriteLine($"  Result: Compiled successfully ({ourSize:N0} bytes), no reference binary for comparison.");
            // Clean up temp files
            try { File.Delete(xmlPath); File.Delete(hkxPath); } catch { }
            return 0;
        }

        Console.Write("  Comparing against reference...");
        var refBytes = File.ReadAllBytes(refPath);
        var ourBytes = File.ReadAllBytes(hkxPath);

        int byteDiffs = 0;
        int minLen = Math.Min(refBytes.Length, ourBytes.Length);
        for (int i = 0; i < minLen; i++)
        {
            if (refBytes[i] != ourBytes[i])
                byteDiffs++;
        }
        byteDiffs += Math.Abs(refBytes.Length - ourBytes.Length);

        bool sizeMatch = refBytes.Length == ourBytes.Length;
        Console.WriteLine(" done.");
        Console.WriteLine();

        // Step 4: Semantic diff (decompile both, normalize IDs, compare)
        int semanticDiffs = -1;
        try
        {
            Console.Write("  Semantic diff (decompile + compare)...");
            var refXmlPath = Path.Combine(tempDir, "ref_decompiled.xml");
            var ourXmlPath = Path.Combine(tempDir, "our_decompiled.xml");

            var header = HKXHeader.SkyrimSE();

            // Decompile reference
            hkRootLevelContainer refRoot;
            using (var rs = File.OpenRead(refPath))
            {
                var br = new BinaryReaderEx(rs);
                var des = new PackFileDeserializer();
                refRoot = (hkRootLevelContainer)des.Deserialize(br);
            }
            using (var ws = File.Create(refXmlPath))
            {
                var xs = new HavokXmlSerializer();
                xs.Serialize(refRoot, header, ws);
            }

            // Decompile ours
            hkRootLevelContainer ourRoot;
            using (var rs = File.OpenRead(hkxPath))
            {
                var br = new BinaryReaderEx(rs);
                var des = new PackFileDeserializer();
                ourRoot = (hkRootLevelContainer)des.Deserialize(br);
            }
            using (var ws = File.Create(ourXmlPath))
            {
                var xs = new HavokXmlSerializer();
                xs.Serialize(ourRoot, header, ws);
            }

            // Compare lines with normalized IDs
            var refLines = File.ReadAllLines(refXmlPath);
            var ourLines = File.ReadAllLines(ourXmlPath);

            semanticDiffs = 0;
            int maxLines = Math.Max(refLines.Length, ourLines.Length);
            for (int i = 0; i < maxLines; i++)
            {
                var rl = i < refLines.Length
                    ? System.Text.RegularExpressions.Regex.Replace(refLines[i].Trim(), @"#\d+", "#XX")
                    : "";
                var ol = i < ourLines.Length
                    ? System.Text.RegularExpressions.Regex.Replace(ourLines[i].Trim(), @"#\d+", "#XX")
                    : "";
                if (rl != ol) semanticDiffs++;
            }

            Console.WriteLine($" {semanticDiffs} diff(s)");

            // Clean up temp decompiled files
            try { File.Delete(refXmlPath); File.Delete(ourXmlPath); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine($" FAILED: {ex.Message}");
        }

        // Report
        Console.WriteLine();
        Console.WriteLine("  ════════════════════════════════════════");
        Console.WriteLine($"  Size:          ref={refBytes.Length:N0}  ours={ourSize:N0}  {(sizeMatch ? "✓ EXACT MATCH" : "✗ MISMATCH")}");
        Console.WriteLine($"  Byte diffs:    {byteDiffs:N0}");
        if (semanticDiffs >= 0)
            Console.WriteLine($"  Semantic diffs: {semanticDiffs}{(semanticDiffs == 0 ? "  ✓ PERFECT" : "")}");
        Console.WriteLine("  ════════════════════════════════════════");

        // Clean up temp files
        try { File.Delete(xmlPath); File.Delete(hkxPath); } catch { }

        return semanticDiffs > 0 ? 1 : 0;
    }
    catch (Exception ex)
    {
        ReportError(ex);
        return 1;
    }
}

// ── Error reporting ──

/// <summary>
/// Write an exception to stderr in MSBuild error format so Visual Studio
/// parses it into the Error List with a clickable file link.
///
/// If the exception message already begins with a path (i.e. it was thrown
/// by BehaviorReader/BehaviorXmlEmitter with the "path(1): error :" prefix),
/// write it verbatim.  Otherwise wrap it with a generic "(unknown)(1): error :"
/// prefix so VS still puts it in the Error List rather than the Output window.
///
/// The stack trace goes to stdout — it's useful for debugging but should not
/// appear in the Error List.
/// </summary>
static void ReportError(Exception ex)
{
    var msg = ex.Message;
    // Messages from our throw sites already contain "): error :" — write as-is.
    if (msg.Contains("): error :"))
        Console.Error.WriteLine(msg);
    else
        Console.Error.WriteLine($"(unknown)(1): error : {msg}");

    // Stack trace to stdout — visible in Output window, not Error List.
    if (ex.StackTrace is { } st)
        Console.WriteLine(st);
    if (ex.InnerException is { } inner)
        Console.WriteLine($"  Caused by: {inner.Message}");
}

// ── Usage ──

static void PrintUsage()
{
    Console.Error.WriteLine("HKBuild — Havok Behavior Compiler & Converter");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  hkbuild <source-directory> [-o <output.xml>]");
    Console.Error.WriteLine("    Compile a character or behavior YAML source directory to Havok packfile XML.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  hkbuild pack <input.xml> [-o <output.hkx>]");
    Console.Error.WriteLine("    Pack a Havok XML file into binary HKX format using HKX2E.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  hkbuild convert <input.hkx> [-o <output.xml>]");
    Console.Error.WriteLine("    Convert a binary HKX file to XML using HKX2E (for verification).");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  hkbuild extract <behavior-name> [--force] [--xml-dir <dir>] [--out-base <dir>]");
    Console.Error.WriteLine("    Extract a behavior XML into YAML source tree format.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  hkbuild convert-all [--force] [--bin-root <dir>] [--xml-root <dir>]");
    Console.Error.WriteLine("    Batch-convert all vanilla HKX files to XML via HKX2E.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  hkbuild verify <source-directory> [--ref <vanilla.hkx>]");
    Console.Error.WriteLine("    Compile + pack + binary diff against vanilla reference.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  hkbuild graph <source-directory> [-o <output.html>]");
    Console.Error.WriteLine("    Generate an interactive HTML graph visualization of the behavior.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Examples:");
    Console.Error.WriteLine("  hkbuild behavior_src/vanilla/behaviors/staggerbehavior.hkx -o stagger.xml");
    Console.Error.WriteLine("  hkbuild pack stagger.xml -o stagger.hkx");
    Console.Error.WriteLine("  hkbuild convert bashbehavior.hkx -o bash_hkx2e.xml");
    Console.Error.WriteLine("  hkbuild extract bashbehavior");
    Console.Error.WriteLine("  hkbuild convert-all --force");
    Console.Error.WriteLine("  hkbuild verify behavior_src/vanilla/character/behaviors/bashbehavior.hkx");
}
