using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AppProxyBridge.Core;

public sealed class ConfigurationStore(string directory)
{
    public static ConfigurationStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AppProxyBridge.Next"));
    public string DirectoryPath => directory;
    readonly JsonSerializerOptions options = new() { WriteIndented = true };
    public Configuration Load()
    {
        var path = Path.Combine(directory, "profiles.json");
        if (!File.Exists(path)) return new();
        var data = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(path), options) ?? throw new IOException("配置文件为空。");
        if (data.Version != 2) throw new IOException("配置格式版本不兼容，请保留原文件。");
        return data;
    }
    public void Save(Configuration data)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "profiles.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(data, options), new UTF8Encoding(false));
        File.Move(path + ".tmp", path, true);
    }
    public string ReadPassword(ProxyProfile profile)
    {
        if (!profile.Authentication) return "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(profile.CredentialReference,"^"+profile.Id.ToString("N")+@"(\.[0-9a-f]{32})?\.secret$",System.Text.RegularExpressions.RegexOptions.CultureInvariant)) throw new IOException("配置的凭据引用无效。");
        return Encoding.UTF8.GetString(Protect(File.ReadAllBytes(Path.Combine(directory, profile.CredentialReference)), false));
    }
    public void DeletePassword(Guid id)
    {
        var path = Path.Combine(directory, id.ToString("N") + ".secret");
        if (File.Exists(path)) File.Delete(path);
        if(Directory.Exists(directory))foreach(var file in Directory.GetFiles(directory,id.ToString("N")+".*.secret"))File.Delete(file);
    }
    // Immutable credential files make the JSON replacement the transaction commit point.
    // A failed save never overwrites the secret referenced by the previous JSON.
    public ProxyProfile SaveProfile(Configuration prospective,ProxyProfile profile,string password)
    {
        profile = profile.Normalize();
        if (prospective.Profiles.Count(p => p.Id == profile.Id) != 1)
            throw new ArgumentException("待保存配置必须在配置列表中且仅出现一次。");
        if (profile.Authentication && (password.Length == 0 || Encoding.UTF8.GetByteCount(password) > 255))
            throw new ArgumentException("请输入密码，长度不能超过 255 个 UTF-8 字节。");
        Directory.CreateDirectory(directory);
        var saved=profile with{CredentialReference=profile.Authentication?profile.Id.ToString("N")+"."+Guid.NewGuid().ToString("N")+".secret":""};
        string? candidate=saved.Authentication?Path.Combine(directory,saved.CredentialReference):null;
        try{
            if(candidate!=null)File.WriteAllBytes(candidate,Protect(Encoding.UTF8.GetBytes(password),true));
            prospective.Profiles=prospective.Profiles.Select(p=>p.Id==saved.Id?saved:p).ToList();
            Save(prospective);
        }catch{if(candidate!=null)try{File.Delete(candidate);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}throw;}
        var obsolete=Directory.GetFiles(directory,profile.Id.ToString("N")+"*.secret");
        foreach(var file in obsolete)if(!string.Equals(file,candidate,StringComparison.OrdinalIgnoreCase))
            try{File.Delete(file);}catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){}
        return saved;
    }
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr ptr);
    static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = encrypt ? CryptProtectData(ref input, "App Proxy Bridge", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法访问当前 Windows 用户的加密凭据。");
            byte[] result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        } finally { Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length); Marshal.FreeHGlobal(input.Data); if(output.Data != IntPtr.Zero) LocalFree(output.Data); Array.Clear(bytes); }
    }
}

