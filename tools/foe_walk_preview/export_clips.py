import sys, json, UnityPy
D, out = sys.argv[1], sys.argv[2]
env = UnityPy.load(D + "/sharedassets0.assets")
go = next(o for o in env.objects if o.type.name == "GameObject" and o.peek_name() == "Dripper")
def comps(g):
    for c in g.read().m_Component:
        yield (c.component if hasattr(c, "component") else c).deref()
def tr_of(g): return next(c for c in comps(g) if c.type.name == "Transform").read()
node = tr_of(go).m_Children[0].read().m_GameObject.deref()
res = {}
for ch in tr_of(node).m_Children:
    cg = ch.read().m_GameObject.deref()
    names = [c.type.name for c in comps(cg)]
    rb = [c.read() for c in comps(cg) if c.type.name == "Rigidbody"]
    print(cg.read().m_Name, names, [("kin", r.m_IsKinematic, "grav", r.m_UseGravity) for r in rb])
anim = next(c for c in comps(node) if c.type.name == "Animation").read()
for clipref in anim.m_Animations:
    clip = clipref.read()
    res[clip.m_Name] = {cu.path: [(k.time, [k.value.x, k.value.y, k.value.z, k.value.w]) for k in cu.curve.m_Curve] for cu in clip.m_RotationCurves}
json.dump(res, open(out, "w"))
print({c: {p: len(v) for p, v in d.items()} for c, d in res.items()})
