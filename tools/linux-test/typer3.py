# usage: typer3.py <wait> TOKEN... ; token = KEYNAME or MOD+KEYNAME (e.g. SEMICOLON, LEFTSHIFT+2, RIGHTALT+A); "PAUSE" waits 2s
import sys, time, evdev
from evdev import ecodes as e
ui = evdev.UInput(name="BananaNana-typer"); time.sleep(float(sys.argv[1]))
def k(n): return getattr(e, 'KEY_'+n)
for t in sys.argv[2:]:
    if t=='PAUSE': time.sleep(2); continue
    *mods, key = t.split('+')
    for m in mods: ui.write(e.EV_KEY,k(m),1); ui.syn(); time.sleep(0.03)
    ui.write(e.EV_KEY,k(key),1); ui.syn(); time.sleep(0.03); ui.write(e.EV_KEY,k(key),0); ui.syn(); time.sleep(0.03)
    for m in mods: ui.write(e.EV_KEY,k(m),0); ui.syn(); time.sleep(0.03)
    time.sleep(0.04)
time.sleep(0.5); ui.close()
