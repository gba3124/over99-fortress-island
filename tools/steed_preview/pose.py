# The Silver Steed's poses, the same numbers BossSteed.cs uses (change both together). Blender axes: +X the horse's left,
# -Y forward, Z up; BossSteed.cs turns them into Unity's.
# Angles in degrees. Legs and head turn about X (positive: hoof back / nose down). Wing: raise about Y (negative lifts the
# left wing), then sweep about Z (positive sends the tip back), then tilt about X (negative leans it back).
import math

LEGS = {"HL": 0.0, "HR": 0.1, "FL": 0.45, "FR": 0.55}  # gallop: where each leg is in the stride


def lerp(a, b, k):
    return a + (b - a) * k


def gallop(u):
    legs = {}
    for name, off in LEGS.items():
        ph = math.tau * (u - off)
        lift = max(0.0, -math.cos(ph)) ** 0.7  # the leg coming forward, off the ground
        if name[0] == "F":
            legs[name] = (28 * math.sin(ph), 75 * lift)
        else:
            legs[name] = (26 * math.sin(ph), -45 * lift)
    return dict(legs=legs, pitch=4 * math.sin(math.tau * (u - 0.3)), bob=0.3 * math.sin(2 * math.tau * u),
                head=10 + 6 * math.sin(math.tau * (u - 0.45)), tail=(40 + 6 * math.sin(math.tau * u), 0),
                wing=(-25, 55, -10))


def pose(name, t=0.0):
    still = {k: (0, 0) for k in LEGS}
    if name == "rest":
        return dict(legs=still, pitch=0, bob=0, head=4, tail=(0, 0), wing=(-45, 30, -30))
    if name == "gallop":
        return gallop(t)
    if name == "rear":  # the brain turns the whole horse up about its hind hooves; the forelegs paddle
        legs = dict(still, FL=(-35 + 20 * math.sin(math.tau * t), 80), FR=(-20 - 20 * math.sin(math.tau * t), 100))
        return dict(legs=legs, pitch=0, bob=0, head=22, tail=(10, 0), wing=(-55, 5, 0), rear=38)
    if name == "leap":
        flap = math.sin(math.tau * t)
        legs = dict(FL=(-50, 110), FR=(-45, 105), HL=(40, 25), HR=(35, 20))
        return dict(legs=legs, pitch=-6, bob=0, head=-4, tail=(50, 0), wing=(-20 + 35 * flap, 0, 0))
    raise KeyError(name)
