import sys, os, UnityPy
D, out = sys.argv[1], sys.argv[2]
names = set(sys.argv[3:])
env = UnityPy.load(D + "/sharedassets0.assets")
done = set()
for obj in env.objects:
    if obj.type.name == "Mesh":
        n = obj.peek_name()
        if n in names and n not in done:
            m = obj.read()
            try:
                with open(os.path.join(out, n + ".obj"), "w") as f: f.write(m.export())
                done.add(n)
                print("mesh", n, "verts", m.m_VertexCount if hasattr(m, "m_VertexCount") else "?")
            except Exception as e:
                print("fail", n, e)
# the material on the DripFish renderer and its textures
for obj in env.objects:
    if obj.type.name == "Material" and obj.peek_name() == "Default":
        mat = obj.read()
        tex = {}
        for k, v in mat.m_SavedProperties.m_TexEnvs:
            try:
                t = v.m_Texture.read()
                tex[k] = t.m_Name
                img = t.image
                img.save(os.path.join(out, f"tex_{k}_{t.m_Name}.png"))
            except Exception as e:
                pass
        cols = {k: (round(v.r,3), round(v.g,3), round(v.b,3), round(v.a,3)) for k, v in mat.m_SavedProperties.m_Colors}
        flts = {k: round(v, 3) for k, v in mat.m_SavedProperties.m_Floats}
        print("Default material textures", tex)
        print("colors", cols)
        print("floats", flts)
        break
