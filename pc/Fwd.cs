using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Fit3Manager
{
    /// <summary>Validator of the FWD package the Galaxy Fit3 OTA accepts (port of the checks of https://github.com/yuriyurin/fit3-flasher): a package that fails here is never sent.</summary>
    public static class Fwd
    {
        public const string StockVersion = "R390XXU0AZA3";
        public const long StockSize = 18265369L;
        public const string StockSha256 = "25e692badec82afe111c517c19c5dbdb1d28c8558c2b027143c43f5c8d05998a";
        public const string UpdaterSha256 = "122d0946e0776e9e6b01b300da3be077f47fa97acd44ddf501c302518eb069e4";
        public const int MaxPackage = 32 * 1024 * 1024;
        const long MaxExpanded = 128L * 1024 * 1024;

        public sealed class Info { public string Source, Target, Sha256; public int Count; public long Size; public bool Stock; }
        public sealed class Bad : Exception { public Bad(string m) : base(m) { } }

        static void Require(bool ok, string text) { if (!ok) throw new Bad(text); }
        static long U32(byte[] b, long o) { Require(o >= 0 && o + 4 <= b.Length, Lang.T("Arquivo truncado.")); int i = (int)o; return (long)b[i] | ((long)b[i + 1] << 8) | ((long)b[i + 2] << 16) | ((long)b[i + 3] << 24); }
        static int U16(byte[] b, long o) { Require(o >= 0 && o + 2 <= b.Length, Lang.T("Arquivo truncado.")); int i = (int)o; return b[i] | (b[i + 1] << 8); }
        static string ZStr(byte[] b, int from, int to)
        {
            int end = -1; for (int i = from; i < to; i++) if (b[i] == 0) { end = i; break; }
            Require(end > from, Lang.T("Campo de texto inválido no pacote."));
            try { return new UTF8Encoding(false, true).GetString(b, from, end - from); } catch (Exception) { throw new Bad(Lang.T("Campo de texto inválido no pacote.")); }
        }
        static uint[] table;
        public static uint Crc32(byte[] b, int off, int len)
        {
            if (table == null) { uint[] t = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1; t[n] = c; } table = t; }
            uint crc = 0xffffffffu; for (int i = off; i < off + len; i++) crc = table[(crc ^ b[i]) & 255] ^ (crc >> 8); return crc ^ 0xffffffffu;
        }
        static void Checksum(byte[] b, int off, int len, long expected, string what) { Require((long)Crc32(b, off, len) == expected, Lang.T("Dano em: ") + what + Lang.T(". Use o arquivo original.")); }
        public static string Sha256(byte[] b, int off, int len)
        {
            using (SHA256 s = SHA256.Create()) { byte[] d = s.ComputeHash(b, off, len); StringBuilder sb = new StringBuilder(); foreach (byte x in d) sb.Append(x.ToString("x2")); return sb.ToString(); }
        }

        static byte[] UnzipOne(byte[] pk, int zoff, int zlen, long budget)
        {
            byte[] z = new byte[zlen]; Buffer.BlockCopy(pk, zoff, z, 0, zlen);
            Require(z.Length >= 52 && U32(z, 0) == 0x04034b50L, Lang.T("Componente não é um ZIP."));
            int end = -1;
            for (int i = z.Length - 22; i >= Math.Max(0, z.Length - 65557); i--) if (U32(z, i) == 0x06054b50L && i + 22 + U16(z, i + 20) == z.Length) { end = i; break; }
            Require(end >= 0, Lang.T("Fim do ZIP não encontrado."));
            Require(U16(z, end + 4) == 0 && U16(z, end + 6) == 0 && U16(z, end + 8) == 1 && U16(z, end + 10) == 1, Lang.T("ZIP não suportado."));
            long centralSize = U32(z, end + 12), central = U32(z, end + 16);
            Require(central + centralSize == end && centralSize >= 46 && U32(z, central) == 0x02014b50L, Lang.T("Diretório do ZIP danificado."));
            int flags = U16(z, central + 8), method = U16(z, central + 10);
            long crc = U32(z, central + 16), packed = U32(z, central + 20), size = U32(z, central + 24);
            int nameLen = U16(z, central + 28), extraLen = U16(z, central + 30), commentLen = U16(z, central + 32);
            Require((flags & ~0x808) == 0 && (method == 0 || method == 8), Lang.T("Compressão do ZIP não suportada."));
            Require(size > 0 && size <= budget, Lang.T("Componente expandido grande demais."));
            Require(centralSize == 46 + nameLen + extraLen + commentLen && U16(z, central + 34) == 0 && U32(z, central + 42) == 0, Lang.T("Estrutura do ZIP ambígua."));
            Require(flags == U16(z, 6) && method == U16(z, 8), Lang.T("Cabeçalhos do ZIP diferentes."));
            int localNameLen = U16(z, 26); long start = 30 + localNameLen + U16(z, 28);
            Require(localNameLen == nameLen && start + packed <= central, Lang.T("Limites do ZIP inválidos."));
            for (int i = 0; i < nameLen; i++) Require(z[30 + i] == z[(int)central + 46 + i], Lang.T("Nomes do ZIP diferentes."));
            if ((flags & 8) == 0) Require(start + packed == central && U32(z, 14) == crc && U32(z, 18) == packed && U32(z, 22) == size, Lang.T("Tamanhos do ZIP inválidos."));
            else
            {
                long d = start + packed; if (U32(z, d) == 0x08074b50L) d += 4;
                Require(d + 12 == central && U32(z, d) == crc && U32(z, d + 4) == packed && U32(z, d + 8) == size, Lang.T("Descritor do ZIP inválido."));
            }
            byte[] raw;
            if (method == 0) { raw = new byte[packed]; Buffer.BlockCopy(z, (int)start, raw, 0, (int)packed); }
            else
            {
                raw = new byte[size];
                try
                {
                    using (DeflateStream ds = new DeflateStream(new MemoryStream(z, (int)start, (int)packed), CompressionMode.Decompress))
                    {
                        int n = 0; while (n < raw.Length) { int r = ds.Read(raw, n, raw.Length - n); if (r <= 0) break; n += r; }
                        Require(n == raw.Length, Lang.T("Componente descompactado truncado."));
                        Require(ds.ReadByte() < 0, Lang.T("Tamanho declarado do ZIP excedido."));
                    }
                }
                catch (InvalidDataException) { throw new Bad(Lang.T("Dados do ZIP danificados.")); }
            }
            Require(raw.Length == size, Lang.T("Tamanho do ZIP diferente."));
            Checksum(raw, 0, raw.Length, crc, Lang.T("componente ZIP"));
            return raw;
        }

        static void ValidateMain(byte[] raw)
        {
            Require(raw.Length >= 0x1010 && raw.Length <= 0x380000, Lang.T("Tamanho do main não suportado."));
            Require(U32(raw, 0x1000) == 0x11223344L && U32(raw, 0x1004) == 0xdeaddeadL, Lang.T("Cabeçalho do main inválido."));
            long size = U32(raw, 0x100c); Require(size >= 0x1010 && size <= raw.Length, Lang.T("Comprimento do main inválido."));
            byte[] n = new byte[size]; Buffer.BlockCopy(raw, 0, n, 0, (int)size);
            for (int i = 0; i < 4; i++) n[i] = 255; for (int i = 0x1000; i < 0x1010; i++) n[i] = 0;
            Checksum(n, 0, n.Length, U32(raw, 0x1008), Lang.T("imagem main"));
        }

        public static Info Validate(byte[] bytes, Action<double> progress)
        {
            Require(bytes.Length >= 92 && bytes.Length <= MaxPackage, Lang.T("Escolha um arquivo FWD .bin de até 32 MB."));
            Require(U32(bytes, 0) == 0x7e5a5a60L, Lang.T("É preciso um pacote FWD, não uma imagem main nem um ZIP."));
            string source = ZStr(bytes, 4, 68); long flags = U32(bytes, 72);
            long header = U32(bytes, 76), updaterSize = U32(bytes, 80), offset = U32(bytes, 84), size = U32(bytes, 88);
            Require(source == StockVersion && flags == 2, Lang.T("Só pacotes com verificação da versão de origem R390XXU0AZA3 são suportados."));
            Require(header == 92 && updaterSize > 0 && updaterSize <= 0x38000 && offset == header + updaterSize + 4 && size >= 80 && offset + size + 4 == bytes.Length, Lang.T("Estrutura FWD inválida."));
            int updOff = (int)header, updLen = (int)(offset - 4 - header), pOff = (int)offset, pLen = (int)size;
            Checksum(bytes, updOff, updLen, U32(bytes, offset - 4), "updater");
            Checksum(bytes, pOff, pLen, U32(bytes, offset + size), Lang.T("pacote"));
            Require(Sha256(bytes, updOff, updLen) == UpdaterSha256, Lang.T("O pacote troca o updater original. Isso não é suportado aqui."));
            byte[] packet = new byte[pLen]; Buffer.BlockCopy(bytes, pOff, packet, 0, pLen);
            Require(U32(packet, 0) == 0x7e5a5a5aL && packet[69] == 0x18, Lang.T("Este formato de atualização não é suportado."));
            string target = ZStr(packet, 4, 68); int count = packet[70]; int tableEnd = 80 + count * 128;
            Require(target == StockVersion, Lang.T("Este instalador é para a AZA3 e modificações dela."));
            Require(count > 0 && tableEnd + 4 <= packet.Length, Lang.T("Sem tabela de componentes."));
            Checksum(packet, 0, tableEnd, U32(packet, tableEnd), Lang.T("catálogo de componentes"));
            long cursor = tableEnd + 4, expanded = 0, firmwareSize = 0, resourceSize = 0; int mainCount = 0;
            HashSet<string> dests = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                int o = 80 + i * 128, kind = U16(packet, o), attr = U16(packet, o + 2);
                long start = U32(packet, o + 4), stored = U32(packet, o + 8); string path = ZStr(packet, o + 12, o + 128);
                Require((kind == 0 && attr == 0x34) || ((kind == 1 || kind == 4 || kind == 7) && attr == 0x74), Lang.T("Tipo de componente não suportado."));
                bool okPath = path.StartsWith("/");
                for (int k = 0; k < path.Length && okPath; k++) { char c = path[k]; if (c == '\\' || c == ':' || c < 0x20) okPath = false; }
                if (okPath) foreach (string part in path.Substring(1).Split('/')) if (part.Length == 0 || part == "." || part == "..") okPath = false;
                Require(okPath, Lang.T("Caminho de componente inseguro."));
                Require(dests.Add(path.ToLowerInvariant()), Lang.T("Caminho de componente repetido."));
                string image = kind == 1 ? "/user/ota/app/best1502x_b319_user.bin" : kind == 4 ? "/user/ota/sensorhub/sensor_hub.bin" : kind == 7 ? "/user/ota/tp/tp_firmware_stx.bin" : null;
                Require(kind == 0 ? (path.StartsWith("/nand/system/") || path.StartsWith("/user/rescue/")) : path == image, Lang.T("Destino do componente não suportado."));
                Require(start == cursor && stored > 0 && start + stored + 4 <= packet.Length, Lang.T("Componentes se sobrepõem ou estão truncados."));
                byte[] raw = UnzipOne(packet, (int)start, (int)stored, MaxExpanded - expanded);
                Checksum(raw, 0, raw.Length, U32(packet, start + stored), Lang.T("componente de firmware"));
                if (kind == 1) { ValidateMain(raw); mainCount++; }
                expanded += raw.Length;
                if (kind != 0) firmwareSize += raw.Length; else resourceSize += raw.Length;
                cursor = start + stored + 4;
                if (progress != null) progress((i + 1) / (double)count);
            }
            Require(cursor == packet.Length && mainCount == 1 && firmwareSize == U32(packet, 72) && resourceSize == U32(packet, 76), Lang.T("A composição do pacote não bate com o cabeçalho."));
            Info info = new Info(); info.Source = source; info.Target = target; info.Count = count; info.Size = bytes.Length; info.Sha256 = Sha256(bytes, 0, bytes.Length);
            info.Stock = info.Sha256 == StockSha256 && bytes.Length == StockSize; return info;
        }
    }
}
