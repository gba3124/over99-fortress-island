using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace HtfFortress.Headless
{
    // Unity's Random and Time are native calls that only exist inside the game. The harness rewrites a copy of the
    // built mod so those calls land here instead, then runs the mod's real code against the game's own DLLs.
    public static class Shim
    {
        public static Random Rng = new Random(12345);
        public static float Now = 100f, Dt = 0.02f;
        public static int Frame = 1;

        public static float Value() => (float)Rng.NextDouble();
        public static float RangeF(float a, float b) => a + (float)Rng.NextDouble() * (b - a);
        public static int RangeI(int a, int b) => b <= a ? a : a + Rng.Next(b - a);
        public static UnityEngine.Vector3 InsideUnitSphere()
        {
            while (true)
            {
                var v = new UnityEngine.Vector3(RangeF(-1f, 1f), RangeF(-1f, 1f), RangeF(-1f, 1f));
                if (v.sqrMagnitude <= 1f) return v;
            }
        }
        public static float Time() => Now;
        public static float DeltaTime() => Dt;
        public static int FrameCount() => Frame;

        // HarmonyX's AccessTools can't start on .NET 10; static fields that use it get null instead (never used headless).
        public static MethodInfo NoMethod(Type t, string name, Type[] ps, Type[] gs) => null;
        public static FieldInfo NoField(Type t, string name) => null;
        public static HarmonyLib.AccessTools.FieldRef<T, F> NoFieldRef<T, F>(string name) => null;
    }

    public static class Program
    {
        public static readonly string Game = Path.Combine(Environment.GetEnvironmentVariable("HOME"),
            "Library/Application Support/PixelPort/GameData/prefixes/free-steam/drive_c/Program Files (x86)/Steam/steamapps/common/How to Fish/How to Fish");
        public static readonly string Repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        private static readonly string[] Dirs = { Path.Combine(Game, "How to Fish_Data/Managed"), Path.Combine(Game, "BepInEx/core") };

        public static int Main(string[] args)
        {
            string mod = Path.Combine(Repo, "HtfFortress/bin/Release/netstandard2.1/HtfFortress.dll");
            string patched = Path.Combine(AppContext.BaseDirectory, "HtfFortress.patched.dll");
            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                if (name.Name == "HtfFortress") return ctx.LoadFromAssemblyPath(patched);
                foreach (var d in Dirs)
                {
                    string p = Path.Combine(d, name.Name + ".dll");
                    if (File.Exists(p)) return ctx.LoadFromAssemblyPath(p);
                }
                return null;
            };
            Rewrite(mod, patched);
            return Tests.Run(args);
        }

        // Every call to UnityEngine.Random / UnityEngine.Time in the mod goes to Shim instead.
        private static void Rewrite(string src, string dst)
        {
            var resolver = new DefaultAssemblyResolver();
            foreach (var d in Dirs) resolver.AddSearchDirectory(d);
            resolver.AddSearchDirectory(Path.GetDirectoryName(src));
            var module = ModuleDefinition.ReadModule(src, new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Immediate });
            var map = new Dictionary<string, MethodReference>();
            void Map(string key, string shim, params Type[] ps) => map[key] = module.ImportReference(typeof(Shim).GetMethod(shim, ps));
            Map("UnityEngine.Random::get_value()", nameof(Shim.Value));
            Map("UnityEngine.Random::Range(System.Single,System.Single)", nameof(Shim.RangeF), typeof(float), typeof(float));
            Map("UnityEngine.Random::Range(System.Int32,System.Int32)", nameof(Shim.RangeI), typeof(int), typeof(int));
            Map("UnityEngine.Random::get_insideUnitSphere()", nameof(Shim.InsideUnitSphere));
            Map("UnityEngine.Time::get_time()", nameof(Shim.Time));
            Map("UnityEngine.Time::get_deltaTime()", nameof(Shim.DeltaTime));
            Map("UnityEngine.Time::get_frameCount()", nameof(Shim.FrameCount));
            Map("UnityEngine.Time::get_realtimeSinceStartup()", nameof(Shim.Time));
            int n = 0;
            var missing = new HashSet<string>();
            foreach (var t in module.GetTypes())
                foreach (var m in t.Methods.Where(m => m.HasBody))
                    foreach (var ins in m.Body.Instructions)
                    {
                        if ((ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) || !(ins.Operand is MethodReference mr)) continue;
                        string dt = mr.DeclaringType.FullName;
                        if (dt == "HarmonyLib.AccessTools" && m.IsConstructor && m.IsStatic)
                        {
                            string sig = $"{mr.Name}/{mr.Parameters.Count}";
                            if (mr is GenericInstanceMethod gim && mr.Name == "FieldRefAccess" && mr.Parameters.Count == 1)
                            {
                                var inst = new GenericInstanceMethod(module.ImportReference(typeof(Shim).GetMethod(nameof(Shim.NoFieldRef))));
                                foreach (var a in gim.GenericArguments) inst.GenericArguments.Add(a);
                                ins.Operand = inst;
                            }
                            else if (sig == "Method/4") ins.Operand = module.ImportReference(typeof(Shim).GetMethod(nameof(Shim.NoMethod)));
                            else if (sig == "Field/2") ins.Operand = module.ImportReference(typeof(Shim).GetMethod(nameof(Shim.NoField)));
                            else { missing.Add("HarmonyLib.AccessTools::" + sig + " in a static initializer"); continue; }
                            n++;
                            continue;
                        }
                        if (dt != "UnityEngine.Random" && dt != "UnityEngine.Time") continue;
                        string key = $"{dt}::{mr.Name}({string.Join(",", mr.Parameters.Select(p => p.ParameterType.FullName))})";
                        if (map.TryGetValue(key, out var rep)) { ins.Operand = rep; n++; }
                        else missing.Add(key);
                    }
            module.Write(dst);
            module.Dispose();
            Console.WriteLine($"[harness] rewrote {n} Random/Time calls" + (missing.Count > 0 ? "; NOT rewritten: " + string.Join(", ", missing) : ""));
        }
    }
}
