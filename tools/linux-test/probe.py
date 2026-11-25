#!/usr/bin/env python3
"""Banana Nana Linux feasibility probe: type ';test' then SPACE -> paste a rich snippet.

Needs python-evdev, wl-clipboard (wl-copy / wl-paste), and read access to
/dev/input/event* and /dev/uinput (user in the 'input' group).
Run with:  python3 probe.py [--plain]
"""
import argparse
import base64
import selectors
import string
import struct
import subprocess
import sys
import time
import zlib

try:
    from evdev import InputDevice, UInput, ecodes, list_devices
except ImportError:
    sys.exit("python-evdev is missing: sudo pacman -S python-evdev")

UI_NAME = "BananaNana-probe"
TRIGGER = ";test"
BACKSPACES = len(TRIGGER) + 1      # the code plus the space that fired it
BUF_MAX = 64

LETTERS = {getattr(ecodes, f"KEY_{c.upper()}"): c for c in string.ascii_lowercase}
DIGITS = {getattr(ecodes, f"KEY_{d}"): d for d in string.digits}
SHIFTS = {ecodes.KEY_LEFTSHIFT, ecodes.KEY_RIGHTSHIFT}
# Keys that end the current "word": Enter, keypad Enter, Tab, Esc.
RESET_KEYS = {ecodes.KEY_ENTER, ecodes.KEY_KPENTER, ecodes.KEY_TAB, ecodes.KEY_ESC}

SNIPPET_HTML = (
    '<p><b>Hello from Banana Nana</b> on <i>Linux</i> &mdash; '
    '<span style="color:#c00">red text</span></p>'
    '<ul><li>one</li><li>two</li></ul>'
    '<p><img src="data:image/png;base64,{png}"></p>'
)
SNIPPET_PLAIN = "Hello from Banana Nana on Linux - red text\none\ntwo\n"


def make_png(size=16):
    """Build a solid red PNG in memory (zlib + struct, no PIL)."""
    def chunk(tag, data):
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))
    raw = b"".join(b"\x00" + b"\xff\x00\x00" * size for _ in range(size))  # filter byte + RGB row
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)            # 8-bit RGB
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr)
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b""))


SNIPPET_HTML_BYTES = SNIPPET_HTML.format(
    png=base64.b64encode(make_png()).decode()).encode()


def wl_paste(args):
    """Run wl-paste; return stdout bytes, or None on any failure."""
    try:
        p = subprocess.run(["wl-paste", *args], capture_output=True, timeout=3)
        return p.stdout if p.returncode == 0 else None
    except (OSError, subprocess.SubprocessError):
        return None


def wl_copy(args, data=b""):
    """Run wl-copy with stdin data; True on success. wl-copy forks a server, so no stdout pipe."""
    try:
        p = subprocess.Popen(["wl-copy", *args], stdin=subprocess.PIPE,
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        p.communicate(data, timeout=5)
        return p.returncode == 0
    except (OSError, subprocess.SubprocessError):
        return False


def save_clipboard():
    """Return (mime, bytes) for text/html if present, else text/plain, else (None, None)."""
    listing = wl_paste(["--list-types"])
    if listing is None:
        return None, None
    types = listing.decode(errors="replace").split()
    for want in ("text/html", "text/plain"):
        match = next((t for t in types if t == want or t.startswith(want + ";")), None)
        if match:
            data = wl_paste(["--no-newline", "--type", match])
            if data is not None:
                return match, data
    return None, None


def permission_help(what):
    raise SystemExit(
        f"Permission denied on {what}.\n"
        "Join the 'input' group:  sudo usermod -aG input $USER\n"
        "then LOG OUT and LOG IN again (a reboot also works), and re-run.")


def find_keyboards():
    """Return (keyboards, denied_count): devices that have KEY_A and KEY_SPACE."""
    keyboards, denied = [], 0
    for path in list_devices():
        try:
            dev = InputDevice(path)
        except PermissionError:
            denied += 1
            continue
        except OSError:
            continue
        keys = dev.capabilities().get(ecodes.EV_KEY, [])
        if ecodes.KEY_A in keys and ecodes.KEY_SPACE in keys and dev.name != UI_NAME:
            keyboards.append(dev)
        else:
            dev.close()
    return keyboards, denied


def tap(ui, code):
    """Press and release one key on the virtual keyboard."""
    ui.write(ecodes.EV_KEY, code, 1); ui.syn()
    time.sleep(0.01)
    ui.write(ecodes.EV_KEY, code, 0); ui.syn()
    time.sleep(0.01)


def send_ctrl_v(ui):
    ui.write(ecodes.EV_KEY, ecodes.KEY_LEFTCTRL, 1); ui.syn()
    time.sleep(0.02)
    tap(ui, ecodes.KEY_V)
    ui.write(ecodes.EV_KEY, ecodes.KEY_LEFTCTRL, 0); ui.syn()


def fire(ui, plain):
    """Replace the typed trigger with the snippet, paste it, restore the clipboard."""
    t0 = time.monotonic()

    def lap(msg):
        print(f"  +{(time.monotonic() - t0) * 1000:7.1f} ms  {msg}")

    print("[trigger] ';test' + SPACE")
    for _ in range(BACKSPACES):
        tap(ui, ecodes.KEY_BACKSPACE)
    lap(f"sent {BACKSPACES} backspaces")

    saved_mime, saved = save_clipboard()
    lap(f"clipboard saved: {saved_mime or 'nothing usable'}")

    if plain:
        mime, data = "text/plain", SNIPPET_PLAIN.encode()
    else:
        mime, data = "text/html", SNIPPET_HTML_BYTES
    # Plain text is only a fallback in theory: wl-copy takes ONE type per call
    # and a second call replaces the first, so only this type is offered.
    copied = wl_copy(["--type", mime], data)
    lap(f"snippet copied as {mime}: {'ok' if copied else 'FAILED'}")

    time.sleep(0.15)
    if copied:
        send_ctrl_v(ui)
        lap("ctrl+v sent")

    time.sleep(0.5)
    if saved is not None:
        restored = wl_copy(["--type", saved_mime], saved)
        label = saved_mime
    else:
        restored = wl_copy(["--clear"])
        label = "cleared"
    lap(f"clipboard restored ({label}): {'ok' if restored else 'FAILED'}")
    print(f"  total {(time.monotonic() - t0) * 1000:.0f} ms\n")


def main():
    ap = argparse.ArgumentParser(description="Banana Nana Linux probe")
    ap.add_argument("--plain", action="store_true",
                    help="paste plain text instead of HTML (for comparison)")
    args = ap.parse_args()

    keyboards, denied = find_keyboards()
    if not keyboards:
        if denied:
            permission_help("/dev/input/event*")
        sys.exit("No keyboard devices (EV_KEY with KEY_A and KEY_SPACE) found.")
    print("Listening to these keyboards (not grabbed):")
    for d in keyboards:
        print(f"  {d.path}  {d.name}")

    try:
        ui = UInput(events={ecodes.EV_KEY: [ecodes.KEY_BACKSPACE,
                                            ecodes.KEY_LEFTCTRL, ecodes.KEY_V]},
                    name=UI_NAME)
    except PermissionError:
        permission_help("/dev/uinput")
    own_paths = {ui.device.path}   # never feed our own output back into the buffer

    print(f"\nVirtual keyboard: {UI_NAME} at {ui.device.path}")
    print("Type ';test' then SPACE in LibreOffice Writer, Kate or a browser.")
    print(f"Mode: {'plain text' if args.plain else 'HTML'}.   Ctrl+C to quit.\n")

    sel = selectors.DefaultSelector()
    for d in keyboards:
        sel.register(d.fd, selectors.EVENT_READ, d)

    buf, shift = "", False
    try:
        while True:
            for key, _ in sel.select():
                dev = key.data
                if dev.path in own_paths:
                    continue
                try:
                    events = list(dev.read())
                except OSError:
                    print(f"Device disconnected: {dev.name}")
                    sel.unregister(dev.fd)
                    continue
                for ev in events:
                    if ev.type != ecodes.EV_KEY:
                        continue
                    if ev.code in SHIFTS:
                        shift = ev.value != 0
                        continue
                    if ev.value == 0:              # key release
                        continue
                    code = ev.code
                    if code == ecodes.KEY_BACKSPACE:
                        buf = buf[:-1]
                    elif code in RESET_KEYS:
                        buf = ""
                    elif code == ecodes.KEY_SPACE:
                        if buf.endswith(TRIGGER):
                            buf = ""
                            fire(ui, args.plain)
                        else:
                            buf += " "
                    elif code in LETTERS:
                        buf += LETTERS[code].upper() if shift else LETTERS[code]
                    elif code in DIGITS:
                        buf += DIGITS[code]
                    elif code == ecodes.KEY_SEMICOLON:
                        buf += ":" if shift else ";"
                    buf = buf[-BUF_MAX:]
    except KeyboardInterrupt:
        print("\nBye.")
    finally:
        for d in keyboards:
            d.close()
        ui.close()


if __name__ == "__main__":
    main()
