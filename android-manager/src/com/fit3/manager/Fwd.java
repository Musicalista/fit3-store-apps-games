package com.fit3.manager;

import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.HashSet;
import java.util.Set;
import java.util.zip.CRC32;
import java.util.zip.Inflater;

/**
 * Validator of the firmware package ("FWD") the Galaxy Fit3 OTA accepts. A port of the checks of the web flasher this app is based on
 * (https://github.com/yuriyurin/fit3-flasher): same structure, same integrity checks, same restrictions (AZA3 -> AZA3, stock updater, known components).
 * It never changes protocol bytes: a package that fails here is never sent to the watch.
 */
public final class Fwd {
    private Fwd() {}
    public static final String STOCK_VERSION = "R390XXU0AZA3";
    public static final long STOCK_SIZE = 18265369L;
    public static final String STOCK_SHA256 = "25e692badec82afe111c517c19c5dbdb1d28c8558c2b027143c43f5c8d05998a";
    public static final String UPDATER_SHA256 = "122d0946e0776e9e6b01b300da3be077f47fa97acd44ddf501c302518eb069e4";
    public static final int MAX_PACKAGE = 32 * 1024 * 1024;
    private static final long MAX_EXPANDED = 128L * 1024 * 1024;

    public static final class Info { public String source, target, sha256; public int count; public long size; public boolean stock; }
    public interface Progress { void fraction(double f); }
    public static final class Bad extends Exception { public Bad(String m) { super(m); } }

    private static void require(boolean ok, String text) throws Bad { if (!ok) throw new Bad(text); }
    private static long u32(byte[] b, long o) throws Bad {
        require(o >= 0 && o + 4 <= b.length, L.t("Arquivo truncado.", "The file is truncated."));
        int i = (int) o; return ((long) (b[i] & 255)) | ((long) (b[i + 1] & 255) << 8) | ((long) (b[i + 2] & 255) << 16) | ((long) (b[i + 3] & 255) << 24);
    }
    private static int u16(byte[] b, long o) throws Bad {
        require(o >= 0 && o + 2 <= b.length, L.t("Arquivo truncado.", "The file is truncated."));
        int i = (int) o; return (b[i] & 255) | ((b[i + 1] & 255) << 8);
    }
    private static String zstr(byte[] b, int from, int to) throws Bad {
        int end = -1; for (int i = from; i < to; i++) if (b[i] == 0) { end = i; break; }
        require(end > from, L.t("Campo de texto invalido no pacote.", "Invalid text field in the package."));
        try { return StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(java.nio.ByteBuffer.wrap(b, from, end - from)).toString(); }
        catch (CharacterCodingException e) { throw new Bad(L.t("Campo de texto invalido no pacote.", "Invalid text field in the package.")); }
    }
    public static long crc32(byte[] b, int off, int len) { CRC32 c = new CRC32(); c.update(b, off, len); return c.getValue(); }
    private static void checksum(byte[] b, int off, int len, long expected, String what) throws Bad {
        require(crc32(b, off, len) == expected, L.t("Dano em: ", "Corrupted: ") + what + L.t(". Use o arquivo original.", ". Use the original file."));
    }
    public static String sha256(byte[] b, int off, int len) {
        try { MessageDigest md = MessageDigest.getInstance("SHA-256"); md.update(b, off, len); byte[] d = md.digest(); StringBuilder s = new StringBuilder(); for (byte x : d) s.append(String.format("%02x", x & 255)); return s.toString(); }
        catch (Exception e) { throw new RuntimeException(e); }
    }

    /** One ZIP member, stored or deflated, no ZIP64 and no encryption. Returns the checked bytes. */
    private static byte[] unzipOne(byte[] zip, int zoff, int zlen, long budget) throws Bad {
        byte[] z = java.util.Arrays.copyOfRange(zip, zoff, zoff + zlen);
        require(z.length >= 52 && u32(z, 0) == 0x04034b50L, L.t("Componente nao e um ZIP.", "Component is not a ZIP."));
        int end = -1;
        for (int i = z.length - 22; i >= Math.max(0, z.length - 65557); i--) {
            if (u32(z, i) == 0x06054b50L && i + 22 + u16(z, i + 20) == z.length) { end = i; break; }
        }
        require(end >= 0, L.t("Fim do ZIP nao encontrado.", "ZIP end not found."));
        require(u16(z, end + 4) == 0 && u16(z, end + 6) == 0 && u16(z, end + 8) == 1 && u16(z, end + 10) == 1, L.t("ZIP nao suportado.", "Unsupported ZIP."));
        long centralSize = u32(z, end + 12), central = u32(z, end + 16);
        require(central + centralSize == end && centralSize >= 46 && u32(z, central) == 0x02014b50L, L.t("Diretorio do ZIP danificado.", "Damaged ZIP directory."));
        int flags = u16(z, central + 8), method = u16(z, central + 10);
        long crc = u32(z, central + 16), packed = u32(z, central + 20), size = u32(z, central + 24);
        int nameLen = u16(z, central + 28), extraLen = u16(z, central + 30), commentLen = u16(z, central + 32);
        require((flags & ~0x808) == 0 && (method == 0 || method == 8), L.t("Compressao do ZIP nao suportada.", "Unsupported ZIP compression."));
        require(size > 0 && size <= budget, L.t("Componente expandido grande demais.", "Expanded component too large."));
        require(centralSize == 46 + nameLen + extraLen + commentLen && u16(z, central + 34) == 0 && u32(z, central + 42) == 0, L.t("Estrutura do ZIP ambigua.", "Ambiguous ZIP structure."));
        require(flags == u16(z, 6) && method == u16(z, 8), L.t("Cabecalhos do ZIP diferentes.", "ZIP headers differ."));
        int localNameLen = u16(z, 26); long start = 30 + localNameLen + u16(z, 28);
        require(localNameLen == nameLen && start + packed <= central, L.t("Limites do ZIP invalidos.", "Invalid ZIP bounds."));
        for (int i = 0; i < nameLen; i++) require(z[30 + i] == z[(int) central + 46 + i], L.t("Nomes do ZIP diferentes.", "ZIP names differ."));
        if ((flags & 8) == 0) {
            require(start + packed == central && u32(z, 14) == crc && u32(z, 18) == packed && u32(z, 22) == size, L.t("Tamanhos do ZIP invalidos.", "Invalid ZIP sizes."));
        } else {
            long d = start + packed; if (u32(z, d) == 0x08074b50L) d += 4;
            require(d + 12 == central && u32(z, d) == crc && u32(z, d + 4) == packed && u32(z, d + 8) == size, L.t("Descritor do ZIP invalido.", "Invalid ZIP descriptor."));
        }
        byte[] raw;
        if (method == 0) raw = java.util.Arrays.copyOfRange(z, (int) start, (int) (start + packed));
        else {
            raw = new byte[(int) size]; Inflater inf = new Inflater(true);
            try {
                inf.setInput(z, (int) start, (int) packed); int n = 0;
                while (n < raw.length) { int r = inf.inflate(raw, n, raw.length - n); if (r == 0) { if (inf.finished() || inf.needsInput() || inf.needsDictionary()) break; } n += r; }
                require(n == raw.length, L.t("Componente descompactado truncado.", "Truncated decompressed component."));
                byte[] extra = new byte[1]; require(inf.inflate(extra) == 0, L.t("Tamanho declarado do ZIP excedido.", "Declared ZIP size exceeded."));
            } catch (java.util.zip.DataFormatException e) { throw new Bad(L.t("Dados do ZIP danificados.", "Damaged ZIP data.")); }
            finally { inf.end(); }
        }
        require(raw.length == size, L.t("Tamanho do ZIP diferente.", "ZIP size mismatch."));
        checksum(raw, 0, raw.length, crc, L.t("componente ZIP", "ZIP component"));
        return raw;
    }

    private static void validateMain(byte[] raw) throws Bad {
        require(raw.length >= 0x1010 && raw.length <= 0x380000, L.t("Tamanho do main nao suportado.", "Unsupported main size."));
        require(u32(raw, 0x1000) == 0x11223344L && u32(raw, 0x1004) == 0xdeaddeadL, L.t("Cabecalho do main invalido.", "Invalid main header."));
        long size = u32(raw, 0x100c);
        require(size >= 0x1010 && size <= raw.length, L.t("Comprimento do main invalido.", "Invalid main length."));
        byte[] normalized = java.util.Arrays.copyOfRange(raw, 0, (int) size);
        for (int i = 0; i < 4; i++) normalized[i] = (byte) 255;
        for (int i = 0x1000; i < 0x1010; i++) normalized[i] = 0;
        checksum(normalized, 0, normalized.length, u32(raw, 0x1008), L.t("imagem main", "main image"));
    }

    public static Info validate(byte[] bytes, Progress progress) throws Bad {
        require(bytes.length >= 92 && bytes.length <= MAX_PACKAGE, L.t("Escolha um arquivo FWD .bin de ate 32 MB.", "Choose a FWD .bin file of up to 32 MB."));
        require(u32(bytes, 0) == 0x7e5a5a60L, L.t("E preciso um pacote FWD, nao uma imagem main nem um ZIP.", "A FWD package is required, not a bare main image or a ZIP."));
        String source = zstr(bytes, 4, 68); long flags = u32(bytes, 72);
        long header = u32(bytes, 76), updaterSize = u32(bytes, 80), offset = u32(bytes, 84), size = u32(bytes, 88);
        require(source.equals(STOCK_VERSION) && flags == 2, L.t("So pacotes com verificacao da versao de origem R390XXU0AZA3 sao suportados.", "Only packages that check source version R390XXU0AZA3 are supported."));
        require(header == 92 && updaterSize > 0 && updaterSize <= 0x38000 && offset == header + updaterSize + 4 && size >= 80 && offset + size + 4 == bytes.length, L.t("Estrutura FWD invalida.", "Invalid FWD structure."));
        int updOff = (int) header, updLen = (int) (offset - 4 - header), pOff = (int) offset, pLen = (int) size;
        checksum(bytes, updOff, updLen, u32(bytes, offset - 4), "updater");
        checksum(bytes, pOff, pLen, u32(bytes, offset + size), L.t("pacote", "package"));
        require(sha256(bytes, updOff, updLen).equals(UPDATER_SHA256), L.t("O pacote troca o updater original. Isso nao e suportado aqui.", "The package replaces the stock updater. That is not supported here."));
        byte[] packet = java.util.Arrays.copyOfRange(bytes, pOff, pOff + pLen);
        require(u32(packet, 0) == 0x7e5a5a5aL && (packet[69] & 255) == 0x18, L.t("Este formato de atualizacao nao e suportado.", "This update format is not supported."));
        String target = zstr(packet, 4, 68); int count = packet[70] & 255; int tableEnd = 80 + count * 128;
        require(target.equals(STOCK_VERSION), L.t("Este instalador e para a AZA3 e modificacoes dela.", "This installer is for AZA3 and modifications of it."));
        require(count > 0 && tableEnd + 4 <= packet.length, L.t("Sem tabela de componentes.", "No component table."));
        checksum(packet, 0, tableEnd, u32(packet, tableEnd), L.t("catalogo de componentes", "component catalogue"));
        long cursor = tableEnd + 4, expanded = 0, firmwareSize = 0, resourceSize = 0; int mainCount = 0;
        Set<String> dests = new HashSet<>();
        for (int i = 0; i < count; i++) {
            int o = 80 + i * 128, kind = u16(packet, o), attr = u16(packet, o + 2);
            long start = u32(packet, o + 4), stored = u32(packet, o + 8); String path = zstr(packet, o + 12, o + 128);
            require((kind == 0 && attr == 0x34) || ((kind == 1 || kind == 4 || kind == 7) && attr == 0x74), L.t("Tipo de componente nao suportado.", "Unsupported component type."));
            boolean okPath = path.startsWith("/");
            for (int k = 0; k < path.length() && okPath; k++) { char c = path.charAt(k); if (c == '\\' || c == ':' || c < 0x20) okPath = false; }
            if (okPath) for (String part : path.substring(1).split("/", -1)) if (part.isEmpty() || part.equals(".") || part.equals("..")) okPath = false;
            require(okPath, L.t("Caminho de componente inseguro.", "Unsafe component path."));
            require(dests.add(path.toLowerCase()), L.t("Caminho de componente repetido.", "Duplicate component path."));
            String image = kind == 1 ? "/user/ota/app/best1502x_b319_user.bin" : kind == 4 ? "/user/ota/sensorhub/sensor_hub.bin" : kind == 7 ? "/user/ota/tp/tp_firmware_stx.bin" : null;
            require(kind == 0 ? (path.startsWith("/nand/system/") || path.startsWith("/user/rescue/")) : path.equals(image), L.t("Destino do componente nao suportado.", "Unsupported component destination."));
            require(start == cursor && stored > 0 && start + stored + 4 <= packet.length, L.t("Componentes se sobrepoem ou estao truncados.", "Components overlap or are truncated."));
            byte[] raw = unzipOne(packet, (int) start, (int) stored, MAX_EXPANDED - expanded);
            checksum(raw, 0, raw.length, u32(packet, start + stored), L.t("componente de firmware", "firmware component"));
            if (kind == 1) { validateMain(raw); mainCount++; }
            expanded += raw.length;
            if (kind != 0) firmwareSize += raw.length; else resourceSize += raw.length;
            cursor = start + stored + 4;
            if (progress != null) progress.fraction((i + 1) / (double) count);
        }
        require(cursor == packet.length && mainCount == 1 && firmwareSize == u32(packet, 72) && resourceSize == u32(packet, 76), L.t("A composicao do pacote nao bate com o cabecalho.", "The package contents do not match the header."));
        Info info = new Info(); info.source = source; info.target = target; info.count = count; info.size = bytes.length; info.sha256 = sha256(bytes, 0, bytes.length);
        info.stock = info.sha256.equals(STOCK_SHA256) && bytes.length == STOCK_SIZE;
        return info;
    }
}
