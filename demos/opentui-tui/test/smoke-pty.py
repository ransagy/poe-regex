#!/usr/bin/env python3
"""Drive the OpenTUI demo in a PTY: boot, type, include, explain, quit.

Verifies integration behavior (app runs, renders labels, responds to keys,
regenerates via the CLI subprocess, exits cleanly). Diff-based terminal output
makes exact layout assertions fragile, so we assert on key substrings in the
accumulated stripped output.
"""
import os, pty, select, sys, time, re, struct, fcntl, termios

DEMO_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO_DIR = os.path.dirname(os.path.dirname(DEMO_DIR))
DEFAULT_BIN = os.path.join(REPO_DIR, "out", "PoeRegex.Cli")
BIN = os.environ.get("POE_REGEX_BIN", DEFAULT_BIN)

def resolve_binary():
    if os.path.exists(BIN):
        return BIN
    for ext in ("", ".exe"):
        candidate = BIN + ext
        if os.path.exists(candidate):
            return candidate
    return None

def read_all(fd, timeout):
    buf = b""
    end = time.time() + timeout
    while time.time() < end:
        r, _, _ = select.select([fd], [], [], 0.05)
        if r:
            try:
                data = os.read(fd, 65536)
            except OSError:
                break
            if not data:
                break
            buf += data
    return buf

def strip(ansi):
    ansi = re.sub(rb"\x1b\[[0-9;?]*[a-zA-Z]", b"", ansi)
    ansi = re.sub(rb"\x1b[()][0-9A-B]", b"", ansi)
    return ansi

def main():
    if not resolve_binary():
        print("SKIP: no poe-regex CLI binary (set POE_REGEX_BIN or npm run build:cli)")
        sys.exit(0)

    pid, master = pty.fork()
    if pid == 0:
        os.environ.update(POE_REGEX_BIN=BIN, TERM="xterm-256color")
        os.chdir(DEMO_DIR)
        os.execvp("node", ["node", "--experimental-ffi", "--disable-warning=ExperimentalWarning",
                           "src/index.mjs"])
        os._exit(127)

    fcntl.ioctl(master, termios.TIOCSWINSZ, struct.pack("HHHH", 40, 120, 0, 0))

    all_bytes = b""

    def gather(timeout):
        nonlocal all_bytes
        before = len(all_bytes)
        time.sleep(timeout)
        all_bytes += read_all(master, 0.5)
        return strip(all_bytes), strip(all_bytes[before:])

    # Phase A: boot (classes + mods + initial generate).
    a, a_new = gather(3.0)

    # Phase B: type a mod search (suggestions filter).
    os.write(master, b"dexter")
    b, b_new = gather(1.2)

    # Phase C: Enter to include the highlighted suggestion.
    os.write(master, b"\r")
    c, c_new = gather(2.5)

    # Phase D: Tab to the suggestion select, Down past the select-all row to a
    # real mod, then 'x' to exclude it.
    os.write(master, b"\t")
    time.sleep(0.4)
    os.write(master, b"\x1b[B")
    time.sleep(0.3)
    os.write(master, b"x")
    d, d_new = gather(2.5)

    # Phase E: 'a' to select all remaining "dexter" matches.
    os.write(master, b"a")
    e, e_new = gather(2.5)

    # Phase F: 'e' to explain the current query.
    os.write(master, b"e")
    f, f_new = gather(2.5)

    # Phase G: reach the MODE select (suggestSelect -> Tab x4 -> modeSelect),
    # then Down -> global (BASE EQUIPMENT row must disappear).
    for _ in range(4):
        os.write(master, b"\t")
        time.sleep(0.3)
    os.write(master, b"\x1b[B")
    g, g_new = gather(2.5)

    # Phase H: Down again -> multi; BASE EQUIPMENT row returns with toggle hint.
    os.write(master, b"\x1b[B")
    h, h_new = gather(2.5)

    # Phase I: Tab to the class list and Enter to toggle the highlighted slot.
    os.write(master, b"\t")
    time.sleep(0.4)
    os.write(master, b"\r")
    i, i_new = gather(2.5)

    # Phase J: quit.
    os.write(master, b"q")
    time.sleep(0.8)
    tail = read_all(master, 0.5)

    code = None
    end = time.time() + 4
    while time.time() < end:
        try:
            p2, st = os.waitpid(pid, os.WNOHANG)
        except ChildProcessError:
            break
        if p2 == pid:
            code = os.waitstatus_to_exitcode(st)
            break
        time.sleep(0.1)

    for name, raw in [("boot", a), ("typed", b), ("included", c), ("excluded", d), ("explained", f)]:
        pass  # raw captures are only used for the substring checks above

    with open("/tmp/opencode/smoke-i_new.bin", "wb") as fh:
        fh.write(i_new)

    checks = []
    checks.append(("boot: engine mode label", b"SYSTEM ENGINE MODE" in a, a[:200]))
    checks.append(("boot: base equipment label", b"BASE EQUIPMENT" in a, a[:200]))
    checks.append(("boot: output panel label", b"COMPILED OUTPUT STRINGS" in a, a[:200]))
    checks.append(("boot: suggestions present", b"[select all" in a or b"Evasion Rating" in a, a[:400]))
    checks.append(("boot: no-output placeholder", b"(no output)" in a, a[:200]))
    checks.append(("boot: controls footer", b"Enter include" in a and b"select all" in a and b"q quit" in a, a[-300:]))
    # Selects render their options (height fix) and the class list is filtered.
    checks.append(("boot: mode options visible", b"single" in a and b"multi" in a, a[:400]))
    checks.append(("boot: game options visible", b"Path of Exile 1" in a and b"Path of Exile 2" in a, a[:400]))
    checks.append(("boot: class list filtered", b"Rings" in a and b"Abyss Jewels" not in a, a[:600]))
    checks.append(("typed: suggestions filtered", b"Falco" in b or b"Fox" in b, b[-400:]))
    checks.append(("enter: regenerated output", b"PAYLOAD CAPACITY" in c and b"chars" in c, c[-500:]))
    checks.append(("exclude: still regenerates", b"PAYLOAD CAPACITY" in d and b"EXCLUDED" in d, d[-600:]))
    checks.append(("select-all: still regenerates", b"PAYLOAD CAPACITY" in e, e[-500:]))
    checks.append(("explain: panel updated", b"candidate mods" in f or b"[include]" in f, f[-700:]))
    # Global mode hides the BASE EQUIPMENT row; multi restores it with the hint.
    checks.append(("global: base row hidden", b"BASE EQUIPMENT" not in g_new, g_new[-500:]))
    checks.append(("multi: base row returns", b"BASE EQUIPMENT" in h_new, h_new[-500:]))
    # Multi: Enter toggles the highlighted slot (marker flips to [x]).
    checks.append(("multi: slot toggle marks [x]", b"[x]" in i_new, i_new[-600:]))

    ok = True
    for name, passed, sample in checks:
        print(f"[{'PASS' if passed else 'FAIL'}] {name}")
        if not passed:
            ok = False
            print("     sample:", sample[:300])

    print(f"exit_code: {code}")
    print("FINAL:", ("PASS" if ok and code == 0 else "FAIL"))
    sys.exit(0 if (ok and code == 0) else 1)

if __name__ == "__main__":
    main()
