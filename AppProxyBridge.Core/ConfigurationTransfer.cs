using System.Text.Json;

namespace AppProxyBridge.Core;
public sealed record ImportResult(IReadOnlyList<ProxyProfile> Profiles,int MissingSecrets);
public static class ConfigurationTransfer
{
    public static ImportResult ImportLegacy(string source,ConfigurationStore destination)
    {
        using var json=JsonDocument.Parse(File.ReadAllText(source));
        if(json.RootElement.GetProperty("Version").GetInt32()!=1)throw new IOException("仅支持旧项目版本 1 的配置。");
        var old=JsonSerializer.Deserialize<Configuration>(json.RootElement.GetRawText())??throw new IOException("配置为空。");
        var validated=old.Profiles.Select(p=>p.Normalize()).ToArray();
        var original=new ConfigurationStore(Path.GetDirectoryName(Path.GetFullPath(source))!);
        var result=new List<ProxyProfile>();int missing=0;
        foreach(var p in validated){
            var copy=p with{Id=Guid.NewGuid(),CredentialReference=""};string password="";
            if(p.Authentication)try{password=original.ReadPassword(p);}catch(Exception ex) when(ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException){missing++;}
            if(password.Length>0)copy=copy with{CredentialReference=destination.SavePassword(copy.Id,password)};
            result.Add(copy);
        }
        return new(result,missing);
    }
    public static string Export(Configuration data)=>JsonSerializer.Serialize(data with{Profiles=data.Profiles.Select(p=>p with{CredentialReference=""}).ToList()},new JsonSerializerOptions{WriteIndented=true});
}
