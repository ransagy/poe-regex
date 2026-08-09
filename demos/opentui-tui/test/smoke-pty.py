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
        time.sleep(timeout)
        all_bytes += read_all(master, 0.5)
        return strip(all_bytes)

    # Phase A: boot (classes + mods + initial generate).
    a = gather(3.0)

    # Phase B: type a mod search (suggestions filter).
    os.write(master, b"dexter")
    b = gather(1.2)

    # Phase C: Enter to include the highlighted suggestion.
    os.write(master, b"\r")
    c = gather(2.5)

    # Phase D: Tab to the suggestion select, then 'x' to exclude it.
    os.write(master, b"\t")
    time.sleep(0.4)
    os.write(master, b"x")
    d = gather(2.5)

    # Phase E: 'e' to explain the current query.
    os.write(master, b"e")
    e = gather(2.5)

    # Phase F: game toggle shortcut ('g' — focus is not on the input now).
    os.write(master, b"g")
    f = gather(3.0)

    # Phase G: quit.
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

    for name, raw in [("boot", a), ("typed", b), ("included", c), ("excluded", d), ("explained", e)]:
        pass  # raw captures are only used for the substring checks above

    checks = []
    checks.append(("boot: engine mode label", b"SYSTEM ENGINE MODE" in a, a[:200]))
    checks.append(("boot: base equipment label", b"BASE EQUIPMENT" in a, a[:200]))
    checks.append(("boot: output panel label", b"COMPILED OUTPUT STRINGS" in a, a[:200]))
    checks.append(("boot: suggestions present", b"Evasion Rating" in a or b"Dexterity" in a, a[:400]))
    checks.append(("boot: no-output placeholder", b"(no output)" in a, a[:200]))
    checks.append(("boot: controls footer", b"Enter include" in a and b"q quit" in a, a[-300:]))
    checks.append(("typed: suggestions filtered", b"Falco" in b or b"Fox" in b, b[-400:]))
    checks.append(("enter: regenerated output", b"PAYLOAD CAPACITY" in c and b"chars" in c, c[-500:]))
    checks.append(("exclude: still regenerates", b"PAYLOAD CAPACITY" in d and b"EXCLUDED" in d, d[-600:]))
    checks.append(("explain: panel updated", b"candidate mods" in e or b"[include]" in e, e[-700:]))
    checks.append(("game toggle: still responsive", b"SYSTEM ENGINE MODE" in f and b"COMPILED OUTPUT STRINGS" in f, f[:300]))

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
