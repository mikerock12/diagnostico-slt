using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace DiagnosticoReparo
{
    // Passwords stay in memory: never interpolate them into a shell or process command line.
    public static class LocalAccounts
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct UserInfo1
        {
            public string Name, Password;
            public uint PasswordAge, Privilege;
            public string Home, Comment;
            public uint Flags;
            public string Script;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PasswordInfo { public string Password; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct GroupMember { public string Name; }
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        static extern uint NetUserAdd(string server, uint level, ref UserInfo1 user, out uint error);
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        static extern uint NetUserSetInfo(string server, string user, uint level, ref PasswordInfo info, out uint error);
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        static extern uint NetLocalGroupAddMembers(string server, string group, uint level, ref GroupMember member, uint count);

        public static void DefinirSenha(string nome, string senha)
        {
            uint error;
            var info = new PasswordInfo { Password = senha };
            uint result = NetUserSetInfo(null, nome, 1003, ref info, out error);
            if (result != 0) throw new Win32Exception((int)result);
        }

        public static EtapaResultado CriarAdministrador(string nome, string senha)
        {
            uint error;
            var info = new UserInfo1 { Name = nome, Password = senha, Privilege = 1, Flags = 0x201 };
            uint result = NetUserAdd(null, 1, ref info, out error);
            if (result == 2224) return EtapaResultado.Alerta("A conta já existe. Escolha outro nome.");
            if (result != 0) return EtapaResultado.Falha("Não foi possível criar a conta: " + new Win32Exception((int)result).Message);
            string group = ((NTAccount)new SecurityIdentifier("S-1-5-32-544").Translate(typeof(NTAccount))).Value;
            group = group.Substring(group.LastIndexOf('\\') + 1);
            var member = new GroupMember { Name = Environment.MachineName + "\\" + nome };
            result = NetLocalGroupAddMembers(null, group, 3, ref member, 1);
            if (result != 0 && result != 1378)
                return EtapaResultado.Falha("Conta criada, mas sem privilégios administrativos: " + new Win32Exception((int)result).Message);
            return EtapaResultado.Sucesso("Conta administrador criada. Faça logoff e entre com ela.");
        }
    }
}
