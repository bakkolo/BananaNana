import sys, time, evdev
from evdev import ecodes as e
ui = evdev.UInput(name="BananaNana-typer"); time.sleep(float(sys.argv[1]))
M = {';':e.KEY_SEMICOLON,' ':e.KEY_SPACE,'\n':e.KEY_ENTER}
def tap(k):
    ui.write(e.EV_KEY,k,1); ui.syn(); time.sleep(0.03); ui.write(e.EV_KEY,k,0); ui.syn(); time.sleep(0.05)
for word in sys.argv[2:]:
    for c in word: tap(M.get(c) or getattr(e, 'KEY_'+c.upper()))
    time.sleep(2)
time.sleep(0.5); ui.close()
