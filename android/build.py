"""build.py: compile Fit3 Apps for Android into an APK without Gradle (javac -> d8 -> aapt2 -> zipalign -> apksigner).

Environment: ANDROID_SDK_ROOT (platform android-34 + build-tools 34.x) and JAVA_HOME (JDK 17); default SDK folder ~/android-build/sdk.
Output: dist/fit3-apps.apk (debug-signed with a local key, flasher-android/fit3apps-debug.keystore, never committed) and a copy in the Downloads folder if it exists."""
import os, secrets, shutil, subprocess, sys, zipfile
H = os.path.dirname(os.path.abspath(__file__))
SDK = os.environ.get("ANDROID_SDK_ROOT") or os.environ.get("ANDROID_HOME") or os.path.join(os.path.expanduser("~"), "android-build", "sdk")
JAVA = os.environ.get("JAVA_HOME") or sys.exit("set JAVA_HOME to a JDK 17")
NT = os.name == "nt"; EXE = ".exe" if NT else ""; BAT = ".bat" if NT else ""
def newest(path, prefix):
    names = sorted(n for n in os.listdir(path) if n.startswith(prefix)); return os.path.join(path, names[-1]) if names else sys.exit("not found in " + path)
BT = newest(os.path.join(SDK, "build-tools"), ""); AJAR = os.path.join(newest(os.path.join(SDK, "platforms"), "android-"), "android.jar")
OUT = os.path.join(H, "out"); DIST = os.path.join(H, "dist")
def run(*a, **k):
    r = subprocess.run(a, capture_output=True, text=True, **k)
    if r.returncode: print(r.stdout + r.stderr); raise SystemExit("FAILED: " + " ".join(str(x) for x in a[:3]))
    return r.stdout
env = dict(os.environ, JAVA_HOME=JAVA)
shutil.rmtree(OUT, ignore_errors=True); os.makedirs(os.path.join(OUT, "cls")); shutil.copytree(os.path.join(H, "..", "apps", "dist") if os.path.isdir(os.path.join(H, "..", "apps", "dist")) else os.path.join(H, "assets"), os.path.join(OUT, "assets")); os.makedirs(DIST, exist_ok=True)
src = os.path.join(H, "src", "com", "fit3", "apps")
srcs = [os.path.join(src, f) for f in os.listdir(src) if f.endswith(".java") and not f.endswith("Test.java")]
run(os.path.join(JAVA, "bin", "javac" + EXE), "-encoding", "UTF-8", "--release", "8", "-classpath", AJAR, "-d", os.path.join(OUT, "cls"), *srcs)
classes = [os.path.join(dp, f) for dp, _, fs in os.walk(os.path.join(OUT, "cls")) for f in fs if f.endswith(".class")]
run(os.path.join(BT, "d8" + BAT), "--release", "--min-api", "24", "--lib", AJAR, "--output", OUT, *classes, env=env)
run(os.path.join(BT, "aapt2" + EXE), "compile", "--dir", os.path.join(H, "res"), "-o", os.path.join(OUT, "res.zip"))
run(os.path.join(BT, "aapt2" + EXE), "link", "-o", os.path.join(OUT, "base.apk"), "-I", AJAR, "--manifest", os.path.join(H, "AndroidManifest.xml"), "-A", os.path.join(OUT, "assets"), os.path.join(OUT, "res.zip"), "--min-sdk-version", "24", "--target-sdk-version", "33")
with zipfile.ZipFile(os.path.join(OUT, "base.apk"), "a") as z: z.write(os.path.join(OUT, "classes.dex"), "classes.dex")
run(os.path.join(BT, "zipalign" + EXE), "-f", "4", os.path.join(OUT, "base.apk"), os.path.join(OUT, "aligned.apk"))
ks = os.path.join(H, "fit3apps-debug.keystore"); pw = open(ks + ".pw").read().strip() if os.path.exists(ks + ".pw") else secrets.token_hex(8)
if not os.path.exists(ks):
    open(ks + ".pw", "w").write(pw)
    run(os.path.join(JAVA, "bin", "keytool" + EXE), "-genkeypair", "-keystore", ks, "-storepass", pw, "-keypass", pw, "-alias", "fit3apps", "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000", "-dname", "CN=Fit3 Apps")
apk = os.path.join(DIST, "fit3-apps.apk")
run(os.path.join(BT, "apksigner" + BAT), "sign", "--ks", ks, "--ks-pass", "pass:" + pw, "--key-pass", "pass:" + pw, "--out", apk, os.path.join(OUT, "aligned.apk"), env=env)
run(os.path.join(BT, "apksigner" + BAT), "verify", apk, env=env)
print("APK: %s (%d bytes)" % (apk, os.path.getsize(apk)))
dl = os.path.join(os.path.expanduser("~"), "Downloads")
if os.environ.get("FIT3_COPY_TO_DOWNLOADS") and os.path.isdir(dl): shutil.copy(apk, os.path.join(dl, "fit3-apps.apk")); print("copy:", os.path.join(dl, "fit3-apps.apk"))
