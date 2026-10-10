"""build.py: builds the Fit3 Manager for Windows (C# 5, the compiler that ships with Windows; no SDK or Visual Studio needed).

    python pc/build.py          builds pc/out/Fit3Manager.exe with its sdk/ (headers, templates, examples) and apps/ (the .f3a files from apps/dist)
    python pc/build.py --test   also builds and runs the tests (validator, mock-watch protocol, app compiler vs appcc.py)

The "Create app" tab needs the Arm GNU Toolchain (arm-none-eabi-gcc); everything else needs only Windows and a Bluetooth adapter."""
import os, shutil, subprocess, sys

H = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(H); OUT = os.path.join(H, "out")
CSC = os.environ.get("CSC") or os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe")
if not os.path.exists(CSC): sys.exit("csc.exe not found; set CSC to the path of the .NET Framework 4 compiler")
os.makedirs(OUT, exist_ok=True)

def src(*names): return [os.path.join(H, n) for n in names]
def csc(target, out, files):
    args = [CSC, "-nologo", "-codepage:65001", "-target:" + target, "-out:" + out, "-r:System.IO.Compression.dll"]
    if target == "winexe": args += ["-r:System.Windows.Forms.dll", "-r:System.Drawing.dll", "-win32icon:" + os.path.join(H, "app.ico")]
    r = subprocess.run(args + files, capture_output=True, text=True)
    if r.returncode: sys.exit(r.stdout + r.stderr)

csc("winexe", os.path.join(OUT, "Fit3Manager.exe"), src("Lang.cs", "Fwd.cs", "Watch.cs", "Catalog.cs", "Ui.cs", "Wizard.cs", "AppBuilder.cs", "MainForm.cs", "Creator.cs"))
sdk = os.path.join(OUT, "sdk"); shutil.rmtree(sdk, ignore_errors=True); os.makedirs(os.path.join(sdk, "examples"))
for f in ("f3app.h", "f3game.h", "app.ld"): shutil.copy(os.path.join(ROOT, "apps", f), sdk)
for f in os.listdir(os.path.join(ROOT, "apps", "src")): shutil.copy(os.path.join(ROOT, "apps", "src", f), os.path.join(sdk, "examples"))
shutil.copytree(os.path.join(H, "templates"), os.path.join(sdk, "templates"))
apps = os.path.join(OUT, "apps"); shutil.rmtree(apps, ignore_errors=True); shutil.copytree(os.path.join(ROOT, "apps", "dist"), apps)
print("built", os.path.join(OUT, "Fit3Manager.exe"))
if "--test" in sys.argv:
    t = os.path.join(OUT, "Fit3ManagerTests.exe"); csc("exe", t, src("Lang.cs", "Fwd.cs", "Watch.cs", "Catalog.cs", "AppBuilder.cs", "Tests.cs"))
    sys.exit(subprocess.run([t], cwd=os.path.join(ROOT, "apps")).returncode)
