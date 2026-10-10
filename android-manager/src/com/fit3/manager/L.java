package com.fit3.manager;

import java.util.Locale;

/** Tiny bilingual helper: Portuguese when the phone language is Portuguese, English otherwise. */
public final class L {
    private L() {}
    public static volatile boolean forcePt;
    public static boolean pt() { return forcePt || Locale.getDefault().getLanguage().startsWith("pt"); }
    public static String t(String pt, String en) { return pt() ? pt : en; }
}
