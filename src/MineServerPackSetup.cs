// Установщик Mine Server Pack для официального лаунчера Minecraft.
// Скачивает последний .mrpack из GitHub Releases, ставит Fabric-профиль,
// моды и шейдеры (с проверкой SHA-512), прописывает сервер в список серверов.
// Собирается встроенным в Windows компилятором, см. scripts/build_exe.ps1.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

static class Program
{
    const string ReleaseApi = "https://api.github.com/repos/LonyLock/mine-server-pack/releases/latest";
    const string FabricProfileUrl = "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json";
    const string McVersion = "26.2";
    const string LoaderVersion = "0.19.3";
    const string ProfileId = "mine-server-pack";
    const string ProfileName = "Mine Server Pack";
    const string ServerName = "Mine Server";
    const string ServerIp = "88.204.150.46";

    static readonly string VersionId = "fabric-loader-" + LoaderVersion + "-" + McVersion;

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
        try
        {
            Run(args);
            Console.WriteLine();
            Console.WriteLine("Готово! Открой лаунчер, выбери профиль \"" + ProfileName + "\" и заходи на сервер.");
            Pause(args);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ОШИБКА: " + ex.Message);
            Console.WriteLine("Если не получается — скачай .mrpack со страницы релизов и установи через Modrinth App.");
            Pause(args);
            return 1;
        }
    }

    static void Pause(string[] args)
    {
        foreach (string a in args) if (a == "--no-pause") return;
        Console.WriteLine("Нажми любую клавишу для выхода...");
        try { Console.ReadKey(true); } catch (InvalidOperationException) { }
    }

    static void Run(string[] args)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

        string mcDir = null;
        foreach (string a in args)
            if (a.StartsWith("--minecraft-dir=")) mcDir = a.Substring("--minecraft-dir=".Length).Trim('"');
        if (mcDir == null)
            mcDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

        Console.WriteLine("=== Установка " + ProfileName + " ===");
        Console.WriteLine("Папка Minecraft: " + mcDir);
        if (!Directory.Exists(mcDir))
        {
            Console.WriteLine();
            Console.WriteLine("ВНИМАНИЕ: папка .minecraft не найдена — официальный лаунчер ещё не установлен.");
            Console.WriteLine("Установи его с https://www.minecraft.net, запусти один раз, затем запусти установщик снова.");
            Directory.CreateDirectory(mcDir);
        }

        Console.WriteLine();
        Console.WriteLine("[1/4] Устанавливаю Fabric " + LoaderVersion + " для Minecraft " + McVersion + "...");
        string versionDir = Path.Combine(Path.Combine(mcDir, "versions"), VersionId);
        Directory.CreateDirectory(versionDir);
        string fabricJson = HttpGetString(string.Format(FabricProfileUrl, McVersion, LoaderVersion));
        File.WriteAllText(Path.Combine(versionDir, VersionId + ".json"), fabricJson, new UTF8Encoding(false));

        Console.WriteLine("[2/4] Получаю последний выпуск пака с GitHub...");
        var ser = new JavaScriptSerializer();
        var release = (Dictionary<string, object>)ser.DeserializeObject(HttpGetString(ReleaseApi));
        string tag = (string)release["tag_name"];
        string mrpackUrl = null;
        foreach (object o in (object[])release["assets"])
        {
            var asset = (Dictionary<string, object>)o;
            if (((string)asset["name"]).EndsWith(".mrpack")) { mrpackUrl = (string)asset["browser_download_url"]; break; }
        }
        if (mrpackUrl == null) throw new Exception("в релизе " + tag + " не найден .mrpack");
        byte[] mrpack = HttpGetBytes(mrpackUrl);
        Console.WriteLine("       Версия пака: " + tag);

        string gameDir = Path.Combine(mcDir, ProfileId);
        Directory.CreateDirectory(gameDir);
        Console.WriteLine("[3/4] Скачиваю моды и шейдеры...");
        using (var zip = new ZipArchive(new MemoryStream(mrpack), ZipArchiveMode.Read))
        {
            var indexEntry = zip.GetEntry("modrinth.index.json");
            if (indexEntry == null) throw new Exception("повреждённый mrpack: нет modrinth.index.json");
            string indexText;
            using (var r = new StreamReader(indexEntry.Open(), Encoding.UTF8)) indexText = r.ReadToEnd();
            var index = (Dictionary<string, object>)ser.DeserializeObject(indexText);

            foreach (object o in (object[])index["files"])
            {
                var f = (Dictionary<string, object>)o;
                if (f.ContainsKey("env"))
                {
                    var env = (Dictionary<string, object>)f["env"];
                    if (env.ContainsKey("client") && (string)env["client"] == "unsupported") continue;
                }
                string rel = ((string)f["path"]).Replace('/', '\\');
                if (rel.Contains("..")) throw new Exception("подозрительный путь в паке: " + rel);
                string dest = Path.Combine(gameDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                string url = (string)((object[])f["downloads"])[0];
                string sha512 = (string)((Dictionary<string, object>)f["hashes"])["sha512"];
                Console.WriteLine("       + " + Path.GetFileName(dest));
                byte[] data = HttpGetBytes(url);
                if (!Sha512Hex(data).Equals(sha512, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("не совпал хеш файла " + rel + " — попробуй ещё раз");
                File.WriteAllBytes(dest, data);
            }

            foreach (var entry in zip.Entries)
            {
                if (!entry.FullName.StartsWith("overrides/") || entry.FullName.EndsWith("/")) continue;
                string rel = entry.FullName.Substring("overrides/".Length).Replace('/', '\\');
                if (rel.Contains("..")) continue;
                string dest = Path.Combine(gameDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using (var src = entry.Open())
                using (var dst = File.Create(dest)) src.CopyTo(dst);
            }
        }

        string serversDat = Path.Combine(gameDir, "servers.dat");
        if (!File.Exists(serversDat)) File.WriteAllBytes(serversDat, BuildServersDat());

        Console.WriteLine("[4/4] Добавляю профиль в лаунчер...");
        AddLauncherProfile(mcDir, gameDir, ser);
    }

    static string HttpGetString(string url) { return Encoding.UTF8.GetString(HttpGetBytes(url)); }

    static byte[] HttpGetBytes(string url)
    {
        using (var wc = new WebClient())
        {
            wc.Headers[HttpRequestHeader.UserAgent] = "MineServerPack-Setup/1.0";
            return wc.DownloadData(url);
        }
    }

    static string Sha512Hex(byte[] data)
    {
        using (var sha = SHA512.Create())
        {
            var sb = new StringBuilder();
            foreach (byte b in sha.ComputeHash(data)) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }

    static void AddLauncherProfile(string mcDir, string gameDir, JavaScriptSerializer ser)
    {
        string path = Path.Combine(mcDir, "launcher_profiles.json");
        Dictionary<string, object> root;
        if (File.Exists(path))
            root = (Dictionary<string, object>)ser.DeserializeObject(File.ReadAllText(path));
        else
            root = new Dictionary<string, object>();

        Dictionary<string, object> profiles;
        if (root.ContainsKey("profiles") && root["profiles"] is Dictionary<string, object>)
            profiles = (Dictionary<string, object>)root["profiles"];
        else { profiles = new Dictionary<string, object>(); root["profiles"] = profiles; }

        string now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var p = new Dictionary<string, object>();
        p["name"] = ProfileName;
        p["type"] = "custom";
        p["created"] = now;
        p["lastUsed"] = now;
        p["lastVersionId"] = VersionId;
        p["gameDir"] = gameDir;
        p["icon"] = "Furnace";
        profiles[ProfileId] = p;

        File.WriteAllText(path, ser.Serialize(root), new UTF8Encoding(false));
    }

    // servers.dat — несжатый NBT: compound { list "servers" [ { ip, name } ] }
    static byte[] BuildServersDat()
    {
        using (var ms = new MemoryStream())
        {
            ms.WriteByte(10); WriteNbtString(ms, "");
            ms.WriteByte(9); WriteNbtString(ms, "servers");
            ms.WriteByte(10);
            WriteNbtInt(ms, 1);
            ms.WriteByte(8); WriteNbtString(ms, "ip"); WriteNbtString(ms, ServerIp);
            ms.WriteByte(8); WriteNbtString(ms, "name"); WriteNbtString(ms, ServerName);
            ms.WriteByte(0);
            ms.WriteByte(0);
            return ms.ToArray();
        }
    }

    static void WriteNbtInt(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }

    static void WriteNbtString(Stream s, string text)
    {
        byte[] b = Encoding.UTF8.GetBytes(text);
        s.WriteByte((byte)(b.Length >> 8)); s.WriteByte((byte)(b.Length & 0xFF));
        s.Write(b, 0, b.Length);
    }
}
