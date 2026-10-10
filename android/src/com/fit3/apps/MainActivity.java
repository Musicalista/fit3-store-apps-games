package com.fit3.apps;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.res.Configuration;
import android.database.Cursor;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.OpenableColumns;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.widget.ArrayAdapter;
import android.widget.CheckBox;
import android.widget.CompoundButton;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;

/**
 * Fit3 Apps: installs and removes small apps (.f3a) on a Galaxy Fit3 whose firmware has the "Meus apps" launcher.
 * Unofficial; not affiliated with Samsung. The apps run as native code inside the watch: install only apps you trust.
 */
public final class MainActivity extends Activity {
    private static final int REQ_FILE = 41;
    private static final class AppItem { String id, pt, en, bg = "D0DCF8", fg = "3454A8"; byte[] data; boolean builtin; }

    private LinearLayout col, listCard, watchCard; private TextView chip, statusTitle, statusDetail, hint; private ProgressBar bar; private Spinner devices;
    private TextView btnConnect, btnRefresh;
    private final List<BluetoothDevice> bonded = new ArrayList<>(); private final List<AppItem> catalog = new ArrayList<>(); private List<String> installed = new ArrayList<>();
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final java.util.HashSet<String> picked = new java.util.HashSet<>(); private TextView pickInfo;
    private boolean dark, busy, watchOk, installedKnown; private Store store;
    private int cBg, cCard, cText, cMuted, cAccent, cAccentText, cOk, cBad, cChip;

    private int dp(int v) { return (int) TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_DIP, v, getResources().getDisplayMetrics()); }
    private GradientDrawable shape(int color, int r) { GradientDrawable g = new GradientDrawable(); g.setColor(color); g.setCornerRadius(dp(r)); return g; }
    private TextView label(String s, int sp, int color, boolean bold) { TextView t = new TextView(this); t.setText(s); t.setTextSize(sp); t.setTextColor(color); if (bold) t.setTypeface(Typeface.DEFAULT_BOLD); return t; }
    private LinearLayout.LayoutParams lp(int w, int h, int l, int t, int r, int b) { LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(w, h); p.setMargins(dp(l), dp(t), dp(r), dp(b)); return p; }
    private LinearLayout card(String title, String sub) {
        LinearLayout c = new LinearLayout(this); c.setOrientation(LinearLayout.VERTICAL); c.setPadding(dp(16), dp(14), dp(16), dp(16)); c.setBackground(shape(cCard, 20));
        if (title != null) c.addView(label(title, 17, cText, true));
        if (sub != null) { TextView s = label(sub, 13, cMuted, false); s.setPadding(0, dp(2), 0, dp(8)); c.addView(s); }
        return c;
    }
    private TextView button(String s, boolean primary, final Runnable r) {
        TextView b = label(s, 14, primary ? cAccentText : cText, true); b.setGravity(Gravity.CENTER); b.setPadding(dp(14), dp(11), dp(14), dp(11)); b.setBackground(shape(primary ? cAccent : cChip, 14)); b.setClickable(true);
        b.setOnClickListener(new View.OnClickListener() { public void onClick(View v) { r.run(); } }); return b;
    }
    private void enable(TextView b, boolean on) { b.setEnabled(on); b.setAlpha(on ? 1f : 0.4f); }
    private static int hex(String h, int def) { try { return 0xFF000000 | Integer.parseInt(h, 16); } catch (Exception e) { return def; } }

    @Override protected void onCreate(Bundle b) {
        super.onCreate(b);
        dark = (getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES;
        setTheme(dark ? android.R.style.Theme_Material_NoActionBar : android.R.style.Theme_Material_Light_NoActionBar);
        cBg = dark ? 0xFF0F1318 : 0xFFF1F5F7; cCard = dark ? 0xFF1A2027 : 0xFFFFFFFF; cText = dark ? 0xFFEAF0F4 : 0xFF14202A; cMuted = dark ? 0xFF93A1AD : 0xFF5F7080;
        cAccent = dark ? 0xFF4FD1C5 : 0xFF0E8F8A; cAccentText = dark ? 0xFF05211F : 0xFFFFFFFF; cOk = 0xFF2BB673; cBad = 0xFFE5534B; cChip = dark ? 0xFF28313A : 0xFFE4ECEF;
        Window w = getWindow(); w.setStatusBarColor(cBg); w.setNavigationBarColor(cCard); if (Build.VERSION.SDK_INT >= 23 && !dark) w.getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR);

        LinearLayout root = new LinearLayout(this); root.setOrientation(LinearLayout.VERTICAL); root.setBackgroundColor(cBg);
        ScrollView sv = new ScrollView(this); col = new LinearLayout(this); col.setOrientation(LinearLayout.VERTICAL); col.setPadding(dp(14), dp(14), dp(14), dp(20)); sv.addView(col);
        root.addView(sv, new LinearLayout.LayoutParams(-1, 0, 1f));
        LinearLayout bottom = new LinearLayout(this); bottom.setOrientation(LinearLayout.VERTICAL); bottom.setBackgroundColor(cCard); bottom.setPadding(dp(18), dp(10), dp(18), dp(10));
        statusTitle = label("Pronto.", 14, cText, true); statusDetail = label("", 12, cMuted, false); bar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal); bar.setMax(1000);
        bottom.addView(statusTitle); bottom.addView(statusDetail); bottom.addView(bar, lp(-1, dp(6), 0, 6, 0, 0)); root.addView(bottom, new LinearLayout.LayoutParams(-1, -2));
        setContentView(root);

        LinearLayout head = new LinearLayout(this); head.setOrientation(LinearLayout.VERTICAL); head.setPadding(dp(20), dp(20), dp(20), dp(18));
        GradientDrawable hg = new GradientDrawable(GradientDrawable.Orientation.TL_BR, dark ? new int[] { 0xFF0E5C63, 0xFF2A3D8F } : new int[] { 0xFF12A39A, 0xFF3659D6 }); hg.setCornerRadius(dp(26)); head.setBackground(hg);
        head.addView(label("Fit3 Apps", 28, 0xFFFFFFFF, true)); head.addView(label(L.t("Instale apps no seu Galaxy Fit3 em segundos, sem regravar o firmware.", "Install apps on your Galaxy Fit3 in seconds, without reflashing."), 13, 0xDDFFFFFF, false), lp(-2, -2, 0, 4, 0, 10));
        chip = label("", 12, 0xFFFFFFFF, true); chip.setPadding(dp(12), dp(5), dp(12), dp(5)); chip.setBackground(shape(0x33000000, 20)); head.addView(chip, lp(-2, -2, 0, 0, 0, 0));
        col.addView(head, lp(-1, -2, 0, 0, 0, 12));

        watchCard = card(L.t("Seu relógio", "Your watch"), L.t("Pareie o Galaxy Fit3 no Bluetooth do celular e feche o Galaxy Wearable.", "Pair the Galaxy Fit3 in the phone's Bluetooth settings and close Galaxy Wearable."));
        devices = new Spinner(this); devices.setBackground(shape(cChip, 12)); devices.setPadding(dp(8), dp(6), dp(8), dp(6)); watchCard.addView(devices, lp(-1, dp(48), 0, 0, 0, 8));
        LinearLayout r1 = new LinearLayout(this); btnConnect = button(L.t("Conectar", "Connect"), true, new Runnable() { public void run() { connect(); } }); btnRefresh = button(L.t("Ver apps no relógio", "Show watch apps"), false, new Runnable() { public void run() { refreshInstalled(); } });
        r1.addView(btnConnect, lp(0, -2, 0, 0, 4, 0)); r1.addView(btnRefresh, lp(0, -2, 4, 0, 0, 0)); ((LinearLayout.LayoutParams) btnConnect.getLayoutParams()).weight = 1; ((LinearLayout.LayoutParams) btnRefresh.getLayoutParams()).weight = 1; watchCard.addView(r1);
        col.addView(watchCard, lp(-1, -2, 0, 0, 0, 12));

        TextView t = label(L.t("Apps disponíveis", "Available apps"), 18, cText, true); t.setPadding(dp(6), dp(4), 0, dp(8)); col.addView(t);
        LinearLayout bar2 = new LinearLayout(this); TextView bAll = button(L.t("Marcar todos", "Select all"), false, new Runnable() { public void run() { for (AppItem a : catalog) picked.add(a.id); render(); } });
        TextView bInst = button(L.t("Instalar marcados", "Install selected"), true, new Runnable() { public void run() { installPicked(); } });
        bar2.addView(bAll, lp(0, -2, 0, 0, 4, 0)); bar2.addView(bInst, lp(0, -2, 4, 0, 0, 0)); ((LinearLayout.LayoutParams) bAll.getLayoutParams()).weight = 1; ((LinearLayout.LayoutParams) bInst.getLayoutParams()).weight = 1; col.addView(bar2);
        pickInfo = label("", 12, cMuted, false); pickInfo.setPadding(dp(6), dp(6), 0, dp(8)); col.addView(pickInfo);
        listCard = new LinearLayout(this); listCard.setOrientation(LinearLayout.VERTICAL); col.addView(listCard);
        col.addView(button(L.t("+  Adicionar um app do celular (.f3a)", "+  Add an app from the phone (.f3a)"), false, new Runnable() { public void run() { pickFile(); } }), lp(-1, -2, 0, 0, 0, 12));

        hint = label(L.t("Só o Galaxy Fit3 SM-R390 (firmware R390XXU0AZA3) é suportado; em outros modelos o app pode não funcionar ou causar reinicializações. Precisa do firmware com o ícone \"Meus apps\" (pacotes \"store-*\"). No relógio: Apps extras > Meus apps. Os apps rodam como código nativo no relógio: instale só os que você confia. Nada aqui altera o firmware.", "Only the Galaxy Fit3 SM-R390 (firmware R390XXU0AZA3) is supported; on other models the app may not work or may cause restarts. Needs the firmware with the \"Meus apps\" tile (\"store-*\" packages). On the watch: Extra apps > Meus apps. Apps run as native code on the watch: install only ones you trust. Nothing here changes the firmware."), 12, cMuted, false);
        hint.setPadding(dp(6), 0, dp(6), 0); col.addView(hint);

        loadAssets(); askPermissions(); loadDevices(); render();
        status(L.t("Pronto.", "Ready."), L.t("Escolha o relógio e toque em Conectar.", "Pick the watch and tap Connect."), 0);
    }

    // ------------------------------------------------------------------ data
    private void loadAssets() {
        try {
            String[] names = getAssets().list("");
            for (String n : names) if (n.endsWith(".f3a")) {
                AppItem a = new AppItem(); a.id = n.substring(0, n.length() - 4); a.data = readAll(getAssets().open(n)); a.builtin = true; a.pt = a.id; a.en = a.id;
                try { String[] p = new String(readAll(getAssets().open(a.id + ".meta")), "UTF-8").trim().split(";"); if (p.length >= 5) { a.bg = p[1]; a.fg = p[2]; a.pt = p[3]; a.en = p[4]; } } catch (Exception ignored) {}
                if (Store.isF3a(a.data)) catalog.add(a);
            }
        } catch (Exception e) { status("Erro", String.valueOf(e.getMessage()), -1); }
    }
    private static byte[] readAll(InputStream in) throws java.io.IOException { ByteArrayOutputStream o = new ByteArrayOutputStream(); byte[] b = new byte[8192]; int n; while ((n = in.read(b)) > 0) o.write(b, 0, n); in.close(); return o.toByteArray(); }
    private static String z(byte[] b, int o) { int n = 0; while (n < 16 && b[o + n] != 0) n++; return new String(b, o, n, java.nio.charset.StandardCharsets.US_ASCII); }
    private String name(AppItem a) { return L.pt() ? a.pt : a.en; }
    private String lineOf(AppItem a) { return a.id + ";" + a.bg + ";" + a.fg + ";" + a.pt + ";" + a.en; }
    private boolean isInstalled(String id) { for (String s : installed) if (s.startsWith(id + ";")) return true; return false; }

    private void askPermissions() {
        if (Build.VERSION.SDK_INT >= 31 && (checkSelfPermission(Manifest.permission.BLUETOOTH_CONNECT) != PackageManager.PERMISSION_GRANTED || checkSelfPermission(Manifest.permission.BLUETOOTH_SCAN) != PackageManager.PERMISSION_GRANTED))
            requestPermissions(new String[] { Manifest.permission.BLUETOOTH_CONNECT, Manifest.permission.BLUETOOTH_SCAN }, 5);
    }
    @Override public void onRequestPermissionsResult(int code, String[] p, int[] r) { loadDevices(); }
    private void loadDevices() {
        bonded.clear(); List<String> names = new ArrayList<>(); int pick = 0, i = 0;
        try { BluetoothAdapter ad = BluetoothAdapter.getDefaultAdapter(); if (ad != null) { Set<BluetoothDevice> set = ad.getBondedDevices(); if (set != null) bonded.addAll(set); } } catch (SecurityException e) { /* asked again after the permission dialog */ }
        for (BluetoothDevice d : bonded) { String n = d.getName() == null ? d.getAddress() : d.getName(); names.add(n + "  (" + d.getAddress() + ")"); if (n.contains("Fit3") || n.contains("Galaxy Fit")) pick = i; i++; }
        if (names.isEmpty()) names.add(L.t("(nenhum dispositivo pareado)", "(no paired devices)"));
        devices.setAdapter(new ArrayAdapter<>(this, android.R.layout.simple_spinner_dropdown_item, names)); devices.setSelection(pick);
    }
    private BluetoothDevice selected() { int p = devices.getSelectedItemPosition(); return p >= 0 && p < bonded.size() ? bonded.get(p) : null; }

    // ------------------------------------------------------------------ rendering
    private void render() {
        chip.setText(watchOk ? "●  " + L.t("Relógio conectado", "Watch connected") : "●  " + L.t("Relógio não conectado", "Watch not connected"));
        enable(btnConnect, !busy && selected() != null); enable(btnRefresh, !busy && selected() != null);
        if (busy) getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON); else getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        listCard.removeAllViews(); updatePickInfo();
        for (final AppItem a : catalog) listCard.addView(appCard(a), lp(-1, -2, 0, 0, 0, 10));
        // everything the watch lists, including apps that are not in this phone's list: each can be removed
        List<String> extra = new ArrayList<>();
        for (String s : installed) { boolean known = false; for (AppItem a : catalog) if (s.startsWith(a.id + ";")) known = true; if (!known) extra.add(s); }
        if (installedKnown) {
            LinearLayout c = card(L.t("Instalados no relógio", "Installed on the watch"), installed.isEmpty() ? L.t("Nenhum app instalado ainda.", "No apps installed yet.") : L.t("Toque em Remover para tirar um app da lista do relógio.", "Tap Remove to take an app off the watch's list."));
            for (String s : installed) { final String[] p = s.split(";"); if (p.length < 5) continue; c.addView(installedRow(p), lp(-1, -2, 0, 4, 0, 0)); }
            listCard.addView(c, lp(-1, -2, 0, 4, 0, 10));
        }
    }
    private View iconTile(String bg, String fg, String initial, int size) {
        TextView t = label(initial, size / 2, hex(bg, 0xFFD0DCF8), true); t.setGravity(Gravity.CENTER); t.setBackground(shape(hex(fg, 0xFF3454A8), 16)); t.setLayoutParams(lp(dp(size), dp(size), 0, 0, 12, 0)); return t;
    }
    private String initial(String s) { return s.length() > 0 ? s.substring(0, 1).toUpperCase() : "?"; }
    private View appCard(final AppItem a) {
        LinearLayout c = card(null, null); LinearLayout top = new LinearLayout(this); top.setGravity(Gravity.CENTER_VERTICAL);
        top.addView(iconTile(a.bg, a.fg, initial(name(a)), 56));
        LinearLayout txt = new LinearLayout(this); txt.setOrientation(LinearLayout.VERTICAL); txt.addView(label(name(a), 16, cText, true));
        txt.addView(label(a.data.length + " bytes" + (a.builtin ? "" : "  ·  " + L.t("do celular", "from phone")), 12, cMuted, false));
        boolean inst = isInstalled(a.id); if (installedKnown || inst) txt.addView(label(inst ? "✔ " + L.t("Instalado", "Installed") : L.t("Não instalado", "Not installed"), 12, inst ? cOk : cMuted, true));
        top.addView(txt, new LinearLayout.LayoutParams(0, -2, 1f));
        CheckBox pk = new CheckBox(this); pk.setText(L.t("Marcar", "Select")); pk.setTextColor(cText); pk.setTextSize(12); pk.setChecked(picked.contains(a.id));
        pk.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() { public void onCheckedChanged(CompoundButton v, boolean on) { if (on) picked.add(a.id); else picked.remove(a.id); updatePickInfo(); } });
        top.addView(pk); c.addView(top);
        LinearLayout r = new LinearLayout(this); TextView i1 = button(inst ? L.t("Atualizar", "Update") : L.t("Instalar", "Install"), true, new Runnable() { public void run() { install(a); } });
        TextView r1 = button(L.t("Remover", "Remove"), false, new Runnable() { public void run() { confirmRemove(a.id, name(a)); } });
        r.addView(i1, lp(0, -2, 0, 0, 4, 0)); r.addView(r1, lp(0, -2, 4, 0, 0, 0)); ((LinearLayout.LayoutParams) i1.getLayoutParams()).weight = 1; ((LinearLayout.LayoutParams) r1.getLayoutParams()).weight = 1;
        enable(i1, !busy); enable(r1, !busy); c.addView(r, lp(-1, -2, 0, 12, 0, 0)); return c;
    }
    private View installedRow(final String[] p) {
        LinearLayout row = new LinearLayout(this); row.setGravity(Gravity.CENTER_VERTICAL); row.setPadding(0, dp(6), 0, dp(6));
        String nm = L.pt() ? p[3] : p[4]; row.addView(iconTile(p[1], p[2], initial(nm), 40));
        LinearLayout txt = new LinearLayout(this); txt.setOrientation(LinearLayout.VERTICAL); txt.addView(label(nm, 15, cText, true)); txt.addView(label(p[0], 11, cMuted, false)); row.addView(txt, new LinearLayout.LayoutParams(0, -2, 1f));
        TextView rm = button(L.t("Remover", "Remove"), false, new Runnable() { public void run() { confirmRemove(p[0], L.pt() ? p[3] : p[4]); } }); rm.setTextColor(cBad); enable(rm, !busy); row.addView(rm); return row;
    }

    // ------------------------------------------------------------------ status helpers
    private void status(final String title, final String detail, final int permille) { ui.post(new Runnable() { public void run() { statusTitle.setText(title); statusDetail.setText(detail); if (permille >= 0) bar.setProgress(permille); } }); }
    private void begin() { busy = true; render(); }
    private void done() { ui.post(new Runnable() { public void run() { busy = false; render(); } }); }
    private void fail(Exception e) { String m = e.getMessage() == null ? e.toString() : e.getMessage(); status(L.t("Falhou", "Failed"), m, 0); }
    private void alert(final String title, final String msg) { ui.post(new Runnable() { public void run() { new AlertDialog.Builder(MainActivity.this).setTitle(title).setMessage(msg).setPositiveButton("OK", null).show(); } }); }
    private Store newStore() { BluetoothDevice d = selected(); if (d == null) { alert("Fit3 Apps", L.t("Pareie o relógio no Bluetooth do celular primeiro.", "Pair the watch in the phone's Bluetooth settings first.")); return null; } store = new Store(BtConn.factory(d)); return store; }
    private String help() {
        return L.t("O relógio recusou a conexão. Ele costuma aceitar um cliente por vez:\n• Em Bluetooth, desconecte o Fit3 do celular e force a parada do Galaxy Wearable e do Samsung Accessory Service.\n• Reinicie o relógio (desligue e ligue).\n• Desligue e ligue o Bluetooth do celular e tente de novo.",
            "The watch refused the connection. It usually accepts one client at a time:\n• In Bluetooth, disconnect the Fit3 and force-stop Galaxy Wearable and Samsung Accessory Service.\n• Restart the watch (power off and on).\n• Turn the phone's Bluetooth off and on and try again.");
    }

    // ------------------------------------------------------------------ actions
    private void connect() {
        if (busy) return; final Store s = newStore(); if (s == null) return; begin(); watchOk = false; render();
        status(L.t("Conectando…", "Connecting…"), L.t("Tenta por até 30 s.", "Retries for up to 30 s."), 0);
        new Thread(new Runnable() { public void run() {
            Exception last = null; long end = System.currentTimeMillis() + 30000; int n = 0;
            while (System.currentTimeMillis() < end && !s.isAborted()) {
                n++; try { s.checkConnection(); last = null; watchOk = true; break; } catch (Exception e) { last = e; status(L.t("Tentando conectar…", "Trying to connect…"), L.t("Tentativa ", "Attempt ") + n + ": " + e.getMessage(), (int) ((30000 - (end - System.currentTimeMillis())) / 30)); }
                try { Thread.sleep(2500); } catch (InterruptedException ie) { break; }
            }
            if (watchOk) { status(L.t("Relógio conectado", "Watch connected"), L.t("Pronto para instalar apps.", "Ready to install apps."), 1000); loadIndexQuietly(s); } else { status(L.t("Não conectou", "Could not connect"), last == null ? "" : String.valueOf(last.getMessage()), 0); alert(L.t("Não conectou", "Could not connect"), help()); }
            done();
        } }).start();
    }
    private void loadIndexQuietly(Store s) { try { Thread.sleep(1200); installed = s.readIndex(); installedKnown = true; } catch (Exception e) { /* the list is optional */ } }
    private void refreshInstalled() {
        if (busy) return; final Store s = newStore(); if (s == null) return; begin(); status(L.t("Lendo a lista do relógio…", "Reading the watch's list…"), "", -1);
        new Thread(new Runnable() { public void run() {
            try { installed = s.readIndex(); installedKnown = true; watchOk = true; status(L.t("Pronto", "Done"), installed.size() + L.t(" app(s) no relógio.", " app(s) on the watch."), 1000); } catch (Exception e) { fail(e); }
            done();
        } }).start();
    }
    private void install(final AppItem a) {
        if (busy) return;
        new AlertDialog.Builder(this).setTitle(L.t("Instalar ", "Install ") + name(a)).setMessage(L.t("O app será copiado para o relógio e roda como código nativo dentro dele. Instale só apps em que você confia. O relógio precisa ter o firmware com \"Meus apps\".", "The app is copied to the watch and runs as native code there. Only install apps you trust. The watch needs the firmware with \"Meus apps\"."))
            .setNegativeButton(L.t("Cancelar", "Cancel"), null).setPositiveButton(L.t("Instalar", "Install"), new DialogInterface.OnClickListener() { public void onClick(DialogInterface d, int w) { doInstall(a); } }).show();
    }
    private void doInstall(final AppItem a) {
        final Store s = newStore(); if (s == null) return; begin(); status(L.t("Instalando ", "Installing ") + name(a), L.t("Não afaste o relógio do celular.", "Keep the watch close to the phone."), 0);
        new Thread(new Runnable() { public void run() {
            try {
                installed = s.install(a.id, a.data, lineOf(a), new Store.Progress() { public void on(long d, long t, long bps) { status(L.t("Enviando ", "Sending ") + name(a), d + " / " + t + " bytes", (int) (d * 1000 / Math.max(1, t))); } });
                installedKnown = true; watchOk = true; status(name(a) + L.t(" instalado", " installed"), L.t("No relógio: Apps extras > Meus apps.", "On the watch: Extra apps > Meus apps."), 1000);
                alert(name(a), L.t("Instalado! No relógio abra Apps extras > Meus apps.", "Installed! On the watch open Extra apps > Meus apps."));
            } catch (Exception e) { fail(e); if (!watchOk || String.valueOf(e.getMessage()).toLowerCase().contains("serial") || String.valueOf(e.getMessage()).contains("recusou")) alert(L.t("Falhou", "Failed"), e.getMessage() + "\n\n" + help()); }
            done();
        } }).start();
    }
    private void updatePickInfo() { if (pickInfo != null) pickInfo.setText(picked.size() + L.t(" marcado(s). O relógio aceita até 12 apps.", " selected. The watch holds up to 12 apps.")); }
    private void installPicked() {
        if (busy) return; final List<AppItem> todo = new ArrayList<>(); for (AppItem a : catalog) if (picked.contains(a.id)) todo.add(a);
        if (todo.isEmpty()) { alert("Fit3 Apps", L.t("Marque pelo menos um app (caixa \"Marcar\" em cada cartão).", "Select at least one app (the \"Select\" box on each card).")); return; }
        StringBuilder nm = new StringBuilder(); for (AppItem a : todo) { if (nm.length() > 0) nm.append(", "); nm.append(name(a)); }
        new AlertDialog.Builder(this).setTitle(L.t("Instalar ", "Install ") + todo.size() + L.t(" app(s)?", " app(s)?")).setMessage(nm + L.t("\n\nOs apps rodam como código nativo no relógio: instale só apps em que você confia. O relógio precisa ter o firmware com \"Meus apps\".", "\n\nApps run as native code on the watch: install only ones you trust. The watch needs the firmware with \"Meus apps\"."))
            .setNegativeButton(L.t("Cancelar", "Cancel"), null).setPositiveButton(L.t("Instalar", "Install"), new DialogInterface.OnClickListener() { public void onClick(DialogInterface d, int w) { doInstallPicked(todo); } }).show();
    }
    private void doInstallPicked(final List<AppItem> todo) {
        final Store s = newStore(); if (s == null) return; begin();
        new Thread(new Runnable() { public void run() {
            int done = 0;
            try {
                for (final AppItem a : todo) {
                    final int idx = done; status(L.t("Instalando ", "Installing ") + name(a) + " (" + (done + 1) + "/" + todo.size() + ")", L.t("Não afaste o relógio do celular.", "Keep the watch close to the phone."), 0);
                    installed = s.install(a.id, a.data, lineOf(a), new Store.Progress() { public void on(long d, long t, long bps) { status(L.t("Enviando ", "Sending ") + name(a) + " (" + (idx + 1) + "/" + todo.size() + ")", d + " / " + t + " bytes", (int) (d * 1000 / Math.max(1, t))); } });
                    done++; installedKnown = true; watchOk = true; try { Thread.sleep(1200); } catch (InterruptedException ie) { break; }
                }
                status(done + L.t(" app(s) instalado(s)", " app(s) installed"), L.t("No relógio: Apps extras > Meus apps.", "On the watch: Extra apps > Meus apps."), 1000);
                alert("Fit3 Apps", done + L.t(" app(s) instalado(s)! No relógio abra Apps extras > Meus apps.", " app(s) installed! On the watch open Extra apps > Meus apps."));
            } catch (Exception e) { fail(new java.io.IOException(done + "/" + todo.size() + " " + L.t("instalados. ", "installed. ") + e.getMessage())); }
            done();
        } }).start();
    }
    private void confirmRemove(final String id, final String nm) {
        if (busy) return;
        new AlertDialog.Builder(this).setTitle(L.t("Remover ", "Remove ") + nm + "?").setMessage(L.t("O app sai da lista do relógio e não aparece mais em \"Meus apps\". Você pode instalar de novo quando quiser.", "The app leaves the watch's list and no longer shows in \"Meus apps\". You can install it again any time."))
            .setNegativeButton(L.t("Cancelar", "Cancel"), null).setPositiveButton(L.t("Remover", "Remove"), new DialogInterface.OnClickListener() { public void onClick(DialogInterface d, int w) { doRemove(id, nm); } }).show();
    }
    private void doRemove(final String id, final String shown) {
        final Store s = newStore(); if (s == null) return; begin(); status(L.t("Removendo…", "Removing…"), id, -1);
        new Thread(new Runnable() { public void run() {
            try { installed = s.remove(id); installedKnown = true; watchOk = true; status(L.t("Removido", "Removed"), id, 1000); } catch (Exception e) { fail(e); }
            done();
        } }).start();
    }

    // ------------------------------------------------------------------ add from phone
    private void pickFile() { Intent i = new Intent(Intent.ACTION_OPEN_DOCUMENT); i.addCategory(Intent.CATEGORY_OPENABLE); i.setType("*/*"); try { startActivityForResult(i, REQ_FILE); } catch (Exception e) { status("Erro", String.valueOf(e.getMessage()), -1); } }
    @Override protected void onActivityResult(int req, int res, Intent data) {
        if (req != REQ_FILE || res != RESULT_OK || data == null || data.getData() == null) return;
        try {
            Uri u = data.getData(); byte[] b = readAll(getContentResolver().openInputStream(u)); String fn = "app";
            Cursor c = getContentResolver().query(u, null, null, null, null); if (c != null) { try { if (c.moveToFirst()) { int ix = c.getColumnIndex(OpenableColumns.DISPLAY_NAME); if (ix >= 0) fn = c.getString(ix); } } finally { c.close(); } }
            if (!Store.isF3a(b)) { alert(L.t("Arquivo inválido", "Invalid file"), L.t("Este arquivo não é um app .f3a (cabeçalho F3A1) ou é maior que 60 KB.", "This file is not a .f3a app (F3A1 header) or it is over 60 KB.")); return; }
            AppItem a = new AppItem(); a.data = b; String base = fn.toLowerCase(); int dot = base.lastIndexOf('.'); if (dot > 0) base = base.substring(0, dot);
            StringBuilder id = new StringBuilder(); for (int i = 0; i < base.length() && id.length() < 11; i++) { char ch = base.charAt(i); if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-') id.append(ch); }
            a.id = id.length() == 0 ? "app" : id.toString(); a.pt = z(b, 32); a.en = z(b, 48); if (a.pt.length() == 0) a.pt = a.id; if (a.en.length() == 0) a.en = a.pt;
            a.pt = a.pt.replace(';', ' '); a.en = a.en.replace(';', ' ');
            for (int i = catalog.size() - 1; i >= 0; i--) if (catalog.get(i).id.equals(a.id) && !catalog.get(i).builtin) catalog.remove(i);
            for (AppItem o : catalog) if (o.id.equals(a.id)) { alert(L.t("Nome já usado", "Name already used"), L.t("Já existe um app com o identificador \"", "An app with the id \"") + a.id + L.t("\". Renomeie o arquivo.", "\" already exists. Rename the file.")); return; }
            catalog.add(a); render(); status(L.t("App adicionado", "App added"), name(a) + " (" + b.length + " bytes)", 0);
        } catch (Exception e) { fail(e); }
    }
}
