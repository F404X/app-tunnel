using System.Runtime.InteropServices;
using AppTunnel.Core;
using Microsoft.CSharp.RuntimeBinder;
using static AppTunnel.Core.Localized;

namespace AppTunnel.Windows;

internal static class Ipv6Firewall
{
    public static void Add(TunnelSession session)
    {
        if (session.BlockedIpv6Applications is not { Length: > 0 } apps) return;
        Apply(policy =>
        {
            var profiles = (int)policy.CurrentProfileTypes;
            foreach (var profile in new[] { 1, 2, 4 })
            {
                if ((profiles & profile) != 0 && !(bool)policy.FirewallEnabled[profile])
                    throw new InvalidOperationException(Unavailable());
            }
            if ((int)policy.LocalPolicyModifyState != 0)
                throw new InvalidOperationException(Unavailable());
            for (var i = 0; i < apps.Length; i++)
            {
                var path = ApplicationTarget.Validate(apps[i]);
                if (path.EndsWith('\\')) throw new InvalidOperationException(Unavailable());
                dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule", throwOnError: true)!)!;
                rule.Name = Name(session, i);
                rule.ApplicationName = path;
                rule.Protocol = 256; // NET_FW_IP_PROTOCOL_ANY
                rule.RemoteAddresses = "2000::/3";
                rule.Direction = 2; // NET_FW_RULE_DIR_OUT
                rule.Action = 0; // NET_FW_ACTION_BLOCK
                rule.Profiles = 7;
                rule.Enabled = true;
                policy.Rules.Add(rule);
            }
        });
    }

    public static void Remove(TunnelSession session)
    {
        if (session.BlockedIpv6Applications is not { Length: > 0 } apps) return;
        Apply(policy =>
        {
            for (var i = 0; i < apps.Length; i++)
            {
                try { policy.Rules.Remove(Name(session, i)); }
                catch (Exception error) when (error.HResult == unchecked((int)0x80070002)) { }
            }
        });
    }

    private static string Name(TunnelSession session, int index) =>
        $"{Path.GetFileName(Path.GetDirectoryName(session.ConfigPath))} IPv6 {index}";

    private static void Apply(Action<dynamic> action)
    {
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!;
            action(Activator.CreateInstance(type)!);
        }
        catch (Exception error) when (error is COMException or RuntimeBinderException)
        {
            throw new InvalidOperationException(Unavailable(), error);
        }
    }

    private static string Unavailable() => T("Windows Firewall could not apply the IPv6 rules. Check the active firewall profiles and group policy.",
        "O Firewall do Windows não conseguiu aplicar as regras IPv6. Verifique os perfis ativos e a política de grupo.",
        "Брандмауэр Windows не смог применить правила IPv6. Проверьте активные профили и групповую политику.");
}
