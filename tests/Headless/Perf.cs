using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mono.Cecil;
using UnityEngine;
using Mono.Cecil.Cil;

namespace HtfFortress.Headless
{
    // `Headless.dll perf`: what the mod's per-frame code costs, read from the built DLL's IL (no game needed).
    // Entry points are the Update / LateUpdate / FixedUpdate / OnGUI / OnTriggerStay of every MonoBehaviour; from each,
    // the calls into the mod's own methods are followed (lambdas and iterator bodies too) up to Depth calls deep.
    // A "site" is one IL instruction that allocates on the managed heap or does a by-name / hierarchy search in Unity.
    // Static count, not bytes: a site in a branch that never runs counts the same as one that runs every frame.
    public static class Perf
    {
        public const int Depth = 2;
        private static readonly string[] Entries = { "Update", "LateUpdate", "FixedUpdate", "OnGUI", "OnTriggerStay" };
        private static readonly string[] Lookups = { "GetComponent", "GetComponents", "GetComponentInChildren", "GetComponentsInChildren", "GetComponentInParent",
                                                     "GetComponentsInParent", "FindObjectOfType", "FindObjectsOfType", "Find", "FindWithTag", "FindGameObjectsWithTag", "get_main" };
        private static readonly string[] PhysicsAlloc = { "OverlapSphere", "OverlapBox", "OverlapCapsule", "RaycastAll", "SphereCastAll", "CapsuleCastAll", "BoxCastAll" };

        public static void Run(string dll)
        {
            Audit(dll);
            Bench();
        }

        // ---------- runtime: the mod's own hot methods, called headless ----------

        // Each case is set up so the method takes the path it takes on most frames without reaching the engine (a turret
        // that is reloading, no belts to show, no network). Bytes are what .NET allocates per call (Mono allocates the same
        // closures and lists; its LINQ allocates more); time is .NET's JIT, so only before/after on this machine compares.
        private static void Bench()
        {
            Console.WriteLine("== perf: per-call cost of the hot paths (headless, mod's real code)");
            var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            D Open<D>(Type t, string name) where D : Delegate => (D)Delegate.CreateDelegate(typeof(D), t.GetMethods(flags).First(m => m.Name == name));
            // A component without the engine: its field initializers run (C# runs them before the base constructor), the base
            // constructor then fails (UnityEngine.Object's needs the C++ side) and is ignored. Program.Shim makes `if (obj)` a
            // reference check for these.
            T Make<T>()
            {
                var o = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
                try { typeof(T).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(o, null); } catch (TargetInvocationException) { }
                return o;
            }

            var turrets = Make<Turrets>();
            ((Dictionary<int, float>)typeof(Turrets).GetField("_nextFire", flags).GetValue(turrets))[1] = 1e9f; // reloading
            var gun = new PieceState { Id = 1, Type = PieceType.Gun, Level = 1, Pos = Vector3.zero };
            var fire = Open<Action<Turrets, PieceState, List<Creature>>>(typeof(Turrets), "Fire");
            var splash = Open<Action<Turrets, List<Creature>, Vector3, float, int, Catalog.TurretStats>>(typeof(Turrets), "Splash");
            var none = new List<Creature>();
            var stats = Catalog.Turret(PieceType.Cannon, 1, 0);
            var net = Make<FortressNet>();
            var waist = Open<Action<FortressNet>>(typeof(FortressNet), "RefreshWaist");
            var systems = (Dictionary<string, ParticleSystem>)typeof(WeaponFx).GetField("Systems", flags).GetValue(null);
            var gold = new Color(1f, 0.78f, 0.3f);
            systems["BossSparks" + ColorUtility.ToHtmlStringRGB(gold)] = systems["SparksCyan"] = Make<ParticleSystem>();

            Case("Turrets.Fire, reloading      (host: per turret, every frame)", () => fire(turrets, gun, none));
            Case("Turrets.Splash, no fish      (host: per cannon/mortar shell)", () => splash(turrets, none, Vector3.zero, 2f, 10, stats));
            Case("Perks.TeamLegend             (host: 3-4 per turret hit)", () => Perks.TeamLegend(Gear.Scope));
            Case("FortressNet.RefreshWaist     (every client, every frame)", () => waist(net));
            var vault = new VaultData();
            Case("Actives.Owned, none owned    (every client, 1-2 per frame)", () => Actives.Owned(vault));
            Case("BossVfx.Sparks(colour)       (boss auras, ~10/s per boss)", () => BossVfx.Sparks(gold));
            Case("WeaponFx.Sparks(\"Cyan\")     (weapon hits, every client)", () => WeaponFx.Sparks("Cyan"));
        }

        private static void Case(string name, Action call)
        {
            const int n = 200_000;
            try
            {
                for (int i = 0; i < 1000; i++) call(); // JIT, first-call caches
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < n; i++) call();
                double bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)n;
                double best = double.MaxValue;
                for (int round = 0; round < 25; round++) // best of many: macOS moves the thread between fast and slow cores
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    for (int i = 0; i < n / 4; i++) call();
                    best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / (n / 4));
                }
                Console.WriteLine($"  {name}  {bytes,6:0} B/call  {best,6:0.0} ns/call");
            }
            catch (Exception e) { Console.WriteLine($"  {name}  not measurable headless: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}"); }
        }

        // ---------- static: every allocation the per-frame code can reach ----------

        private static void Audit(string dll)
        {
            var resolver = new DefaultAssemblyResolver();
            foreach (var d in new[] { Path.Combine(Program.Game, "How to Fish_Data/Managed"), Path.Combine(Program.Game, "BepInEx/core"), Path.GetDirectoryName(dll) })
                resolver.AddSearchDirectory(d);
            using var module = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = resolver });
            var depth = new Dictionary<MethodDefinition, int>();
            var queue = new Queue<MethodDefinition>();
            foreach (var t in module.GetTypes().Where(IsBehaviour))
                foreach (var m in t.Methods.Where(m => m.HasBody && Entries.Contains(m.Name)))
                { depth[m] = 0; queue.Enqueue(m); }
            int entries = depth.Count;
            while (queue.Count > 0)
            {
                var m = queue.Dequeue();
                int d = depth[m];
                foreach (var next in Callees(m))
                {
                    // A lambda or an iterator body runs as part of its owner: same depth.
                    int nd = next.DeclaringType.Name.StartsWith("<") || next.Name.StartsWith("<") ? d : d + 1;
                    if (nd > Depth || depth.TryGetValue(next, out int old) && old <= nd) continue;
                    depth[next] = nd;
                    queue.Enqueue(next);
                }
            }
            var perMethod = depth.Keys.Select(m => (m, d: depth[m], sites: Sites(m).ToList())).Where(x => x.sites.Count > 0).ToList();
            Console.WriteLine($"== perf: per-frame IL audit of {Path.GetFileName(dll)} ({entries} entry points, {depth.Count} methods within {Depth} calls)");
            foreach (int maxD in new[] { 0, 1, Depth })
            {
                var all = perMethod.Where(x => x.d <= maxD).SelectMany(x => x.sites).ToList();
                Console.WriteLine($"  depth<={maxD}: {all.Count} sites  " + string.Join("  ", all.GroupBy(s => s).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
            }
            // Allocations before a method's first branch run on every call, even the ones that return straight away
            // (a closure the compiler builds at the top of a method that only uses it further down, say).
            var always = depth.Keys.Select(m => (m, d: depth[m], n: Sites(m, entryOnly: true).Count())).Where(x => x.n > 0).OrderBy(x => x.d).ThenBy(x => Owner(x.m)).ToList();
            Console.WriteLine("  allocations on every call (before the first branch): "
                              + string.Join("  ", new[] { 0, 1, Depth }.Select(maxD => $"depth<={maxD}: {always.Where(x => x.d <= maxD).Sum(x => x.n)}")));
            Console.WriteLine("    depth<=1: " + string.Join(", ", always.Where(x => x.d <= 1).Select(x => $"{Owner(x.m)}[{x.d}]")));
            Console.WriteLine("    depth 2: " + string.Join(", ", always.Where(x => x.d == 2).Select(x => Owner(x.m))));
            Console.WriteLine("  every frame (depth 0) and the busiest one call down (depth: sites):");
            foreach (var x in perMethod.Where(x => x.d == 0).OrderByDescending(x => x.sites.Count).ThenBy(x => x.m.FullName)
                         .Concat(perMethod.Where(x => x.d == 1).OrderByDescending(x => x.sites.Count).ThenBy(x => x.m.FullName).Take(25)))
                Console.WriteLine($"    {x.d}: {x.sites.Count,3}  {Owner(x.m)}  [{string.Join(" ", x.sites.GroupBy(s => s).Select(g => g.Key + " " + g.Count()))}]");
        }

        private static bool IsBehaviour(TypeDefinition t)
        {
            for (var b = t.BaseType; b != null;)
            {
                if (b.FullName == "UnityEngine.MonoBehaviour") return true;
                try { b = b.Resolve()?.BaseType; } catch { return false; }
            }
            return false;
        }

        private static string Owner(MethodDefinition m)
        {
            var t = m.DeclaringType;
            while (t.DeclaringType != null && t.Name.StartsWith("<")) t = t.DeclaringType;
            return t.Name + "." + m.Name;
        }

        private static IEnumerable<MethodDefinition> Callees(MethodDefinition m)
        {
            // an iterator's body lives in its state machine's MoveNext
            var sm = m.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "IteratorStateMachineAttribute");
            if (sm != null && sm.ConstructorArguments[0].Value is TypeReference smt && smt.Resolve() is TypeDefinition smd)
                foreach (var mn in smd.Methods.Where(x => x.Name == "MoveNext" && x.HasBody)) yield return mn;
            foreach (var ins in m.Body.Instructions)
            {
                if (!(ins.Operand is MethodReference mr)) continue;
                if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt && ins.OpCode != OpCodes.Newobj && ins.OpCode != OpCodes.Ldftn) continue;
                MethodDefinition md;
                try { md = mr.Resolve(); } catch { continue; }
                if (md != null && md.HasBody && md.Module == m.Module) yield return md;
            }
        }

        // One tag per allocating / searching instruction.
        public static IEnumerable<string> Sites(MethodDefinition m, bool entryOnly = false)
        {
            foreach (var ins in m.Body.Instructions)
            {
                if (entryOnly && ins.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw) yield break;
                if (ins.OpCode == OpCodes.Box) { yield return "box"; continue; }
                if (ins.OpCode == OpCodes.Newarr) { yield return "newarr"; continue; }
                if (!(ins.Operand is MethodReference mr)) continue;
                string type = mr.DeclaringType.FullName, name = mr.Name;
                if (ins.OpCode == OpCodes.Newobj)
                {
                    TypeDefinition td = null;
                    try { td = mr.DeclaringType.Resolve(); } catch { }
                    if (td == null || td.IsValueType) continue;
                    if (IsException(td)) continue; // a throw path
                    if (IsDelegate(td))
                    {
                        // the compiler caches a lambda that captures nothing in a static field: built once, not per call
                        if (ins.Next?.OpCode == OpCodes.Dup && ins.Next.Next?.OpCode == OpCodes.Stsfld) continue;
                        yield return "delegate";
                    }
                    else if (td.Name.Contains("DisplayClass")) yield return "closure";
                    else if (td.CustomAttributes.Any(a => a.AttributeType.Name == "CompilerGeneratedAttribute")) yield return "iterator";
                    else yield return "new";
                    continue;
                }
                if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
                if (type == "System.Linq.Enumerable") yield return "linq";
                else if (type == "System.String" && (name == "Concat" || name == "Format" || name == "Join" || name == "Substring" || name == "Replace" || name == "Split")
                         || name == "ToStringAndClear" || name == "ToString" && mr.Parameters.Count <= 1 && type != "System.Text.StringBuilder") yield return "string";
                else if (name == "GetEnumerator" && (type.StartsWith("System.Collections.Generic.IEnumerable") || type == "System.Collections.IEnumerable")) yield return "ienum";
                else if (type.StartsWith("UnityEngine.") && Lookups.Contains(name) && (name != "Find" || type == "UnityEngine.GameObject" || type == "UnityEngine.Transform")
                         && (name != "get_main" || type == "UnityEngine.Camera")) yield return "lookup";
                else if (type == "UnityEngine.Physics" && PhysicsAlloc.Contains(name)) yield return "physics";
                else if (type == "UnityEngine.Renderer" && (name == "get_material" || name == "get_materials")) yield return "material";
            }
        }

        private static bool IsException(TypeDefinition t)
        {
            for (var b = t; b != null;)
            {
                if (b.FullName == "System.Exception") return true;
                try { b = b.BaseType?.Resolve(); } catch { return false; }
            }
            return false;
        }

        private static bool IsDelegate(TypeDefinition t) => t.BaseType != null && (t.BaseType.FullName == "System.MulticastDelegate" || t.BaseType.FullName == "System.Delegate");
    }
}
