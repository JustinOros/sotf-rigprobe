using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RedLoader;
using RedLoader.Utils;
using Sons.Ai.Vail;
using Sons.Wearable.Clothing;
using SonsSdk;
using SonsSdk.Attributes;
using TheForest.Utils;
using UnityEngine;

namespace RigProbe;

public class RigProbe : SonsMod
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static HashSet<string> _playerBoneNames = new();

    protected override void OnSdkInitialized()
    {
        RLog.Msg("RigProbe loaded. Commands: rigprobe [filter], rigscene, rigspawn <TypeName> [variation]");
    }

    [DebugCommand("rigprobe")]
    private static void ProbeCommand(string args)
    {
        try
        {
            RunProbe((args ?? string.Empty).Trim(), false);
        }
        catch (Exception e)
        {
            RLog.Error($"rigprobe failed: {e}");
        }
    }

    [DebugCommand("rigscene")]
    private static void SceneCommand(string args)
    {
        try
        {
            RunProbe(string.Empty, true);
        }
        catch (Exception e)
        {
            RLog.Error($"rigscene failed: {e}");
        }
    }

    [DebugCommand("rigspawn")]
    private static void SpawnCommand(string args)
    {
        var parts = (args ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Enum.TryParse<VailActorTypeId>(parts[0], true, out var id))
        {
            Say("Usage: rigspawn <TypeName> [variation]  e.g. rigspawn Virginia");
            return;
        }

        var variation = parts.Length > 1 && int.TryParse(parts[1], out var v) ? v : 0;
        var player = LocalPlayer.GameObject;
        if (!player)
        {
            Say("Load into a game first");
            return;
        }

        try
        {
            var t = player.transform;
            var actor = ActorTools.Spawn(id, t.position + t.forward * 4f, variation);
            Say(actor ? $"Spawned {id} variation {variation}" : $"Spawn returned null for {id}");
        }
        catch (Exception e)
        {
            RLog.Error($"rigspawn {id} failed: {e.Message}");
        }
    }

    private static void RunProbe(string filter, bool sceneMode)
    {
        var player = LocalPlayer.GameObject;
        if (!player)
        {
            Say("Load into a game first");
            return;
        }

        var outDir = Path.Combine(LoaderEnvironment.UserDataDirectory, "RigProbe", sceneMode ? "scene" : "characters");
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        var dumps = new List<CharacterDump>();
        var carryRoot = FindChild(player.transform, "CarryBody");

        var playerDump = DumpObject(player, "player", player.name, carryRoot);
        AddPlayerData(playerDump, player);
        _playerBoneNames = new HashSet<string>(playerDump.SkinnedMeshes.SelectMany(s => s.Bones).Select(LeafName));
        dumps.Add(playerDump);

        if (!sceneMode)
        {
            if (carryRoot)
            {
                for (int i = 0; i < carryRoot.childCount; i++)
                {
                    var child = carryRoot.GetChild(i);
                    dumps.Add(DumpObject(child.gameObject, "carry", child.name, null));
                }
            }

            foreach (var id in Enum.GetValues(typeof(VailActorTypeId)).Cast<VailActorTypeId>())
            {
                if (id == VailActorTypeId.None)
                    continue;
                if (filter.Length > 0 && id.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                VailActor prefab = null;
                try
                {
                    prefab = ActorTools.GetPrefab(id);
                }
                catch (Exception e)
                {
                    RLog.Warning($"GetPrefab {id}: {e.Message}");
                }
                if (prefab)
                {
                    var d = DumpObject(prefab.gameObject, "actorPrefab", id.ToString(), null);
                    d.ActorType = id.ToString();
                    dumps.Add(d);
                }

                List<VailActor> live = null;
                try
                {
                    live = ActorTools.GetActors(id)?.Where(a => a).ToList();
                }
                catch (Exception e)
                {
                    RLog.Warning($"GetActors {id}: {e.Message}");
                }
                if (live == null)
                    continue;
                for (int i = 0; i < live.Count; i++)
                {
                    var d = DumpObject(live[i].gameObject, "actorLive", $"{id}_{i}", null);
                    d.ActorType = id.ToString();
                    dumps.Add(d);
                }
            }
        }
        else
        {
            var skip = new HashSet<int>();
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
                skip.Add(t.GetInstanceID());

            var roots = new Dictionary<int, Transform>();
            foreach (var smr in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
            {
                if (!smr || !smr.gameObject.scene.IsValid() || skip.Contains(smr.transform.GetInstanceID()))
                    continue;
                var root = smr.transform.root;
                if (!roots.ContainsKey(root.GetInstanceID()))
                    roots[root.GetInstanceID()] = root;
            }
            foreach (var root in roots.Values)
                dumps.Add(DumpObject(root.gameObject, "scene", $"{root.name}_{root.GetInstanceID()}", null));
        }

        foreach (var d in dumps)
        {
            d.PlayerNameMatch = PlayerMatch(d);
            d.RigFamily = Family(d);
            var file = Path.Combine(outDir, $"{d.Source}_{Sanitize(d.Name)}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(d, Json));
        }

        WriteIndex(outDir, dumps);
        WriteFamilies(outDir, dumps);
        Say($"RigProbe: {dumps.Count} characters written to {outDir}");
    }

    private static CharacterDump DumpObject(GameObject go, string source, string name, Transform exclude)
    {
        var root = go.transform;
        var dump = new CharacterDump
        {
            Name = name,
            Source = source,
            Path = HierPath(root),
            Active = go.activeInHierarchy
        };

        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r && !(exclude && IsUnder(r.transform, exclude)))
            .ToList();

        var rigIds = new HashSet<int>();
        foreach (var r in smrs)
        {
            var bones = r.bones;
            if (bones != null)
            {
                foreach (var b in bones)
                    AddWithAncestors(b, root, rigIds);
            }
            AddWithAncestors(r.rootBone, root, rigIds);
        }

        foreach (var a in go.GetComponentsInChildren<Animator>(true))
        {
            if (!a || (exclude && IsUnder(a.transform, exclude)))
                continue;
            AddWithAncestors(a.transform, root, rigIds);
            dump.Animators.Add(DumpAnimator(a, root));
        }

        foreach (var t in go.GetComponentsInChildren<Transform>(true))
        {
            if (!rigIds.Contains(t.GetInstanceID()))
                continue;
            dump.Rig.Add(new BoneInfo
            {
                Path = RelPath(t, root),
                Parent = t.parent && t != root ? RelPath(t.parent, root) : string.Empty,
                LocalPosition = V3(t.localPosition),
                LocalRotation = Q(t.localRotation),
                LocalScale = V3(t.localScale)
            });
        }

        foreach (var r in smrs)
            dump.SkinnedMeshes.Add(DumpSmr(r, root));

        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf || !mf.sharedMesh || (exclude && IsUnder(mf.transform, exclude)))
                continue;
            if (!rigIds.Contains(mf.transform.parent ? mf.transform.parent.GetInstanceID() : 0))
                continue;
            dump.AttachedMeshes.Add($"{RelPath(mf.transform, root)} mesh={mf.sharedMesh.name}");
        }

        return dump;
    }

    private static SmrInfo DumpSmr(SkinnedMeshRenderer r, Transform root)
    {
        var info = new SmrInfo
        {
            Path = RelPath(r.transform, root),
            Enabled = r.enabled,
            Active = r.gameObject.activeInHierarchy,
            RootBone = r.rootBone ? RelPath(r.rootBone, root) : string.Empty
        };

        var mesh = r.sharedMesh;
        var bones = r.bones;
        Matrix4x4[] binds = null;
        if (mesh)
        {
            info.Mesh = mesh.name;
            info.Vertices = mesh.vertexCount;
            info.SubMeshes = mesh.subMeshCount;
            info.Readable = mesh.isReadable;
            try
            {
                binds = mesh.bindposes.ToArray();
            }
            catch
            {
                binds = null;
            }
            for (int i = 0; i < mesh.blendShapeCount; i++)
                info.BlendShapes.Add(mesh.GetBlendShapeName(i));
        }

        if (bones != null)
        {
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                var path = b ? RelPath(b, root) : "null";
                info.Bones.Add(path);
                if (binds != null && i < binds.Length)
                {
                    var rest = binds[i].inverse;
                    info.BindRest.Add(new RestInfo
                    {
                        Bone = path,
                        Position = V3(new Vector3(rest.m03, rest.m13, rest.m23)),
                        Rotation = Q(rest.rotation)
                    });
                }
            }
        }

        foreach (var m in r.sharedMaterials)
        {
            if (!m)
                continue;
            var mi = new MaterialInfo { Name = m.name, Shader = m.shader ? m.shader.name : string.Empty };
            try
            {
                foreach (var prop in m.GetTexturePropertyNames())
                {
                    var tex = m.GetTexture(prop);
                    if (tex)
                        mi.Textures[prop] = tex.name;
                }
            }
            catch
            {
            }
            info.Materials.Add(mi);
        }

        return info;
    }

    private static AnimatorInfo DumpAnimator(Animator a, Transform root)
    {
        var info = new AnimatorInfo
        {
            Path = RelPath(a.transform, root),
            Enabled = a.enabled
        };
        try
        {
            info.Controller = a.runtimeAnimatorController ? a.runtimeAnimatorController.name : string.Empty;
        }
        catch
        {
        }

        var avatar = a.avatar;
        if (avatar)
        {
            info.Avatar = avatar.name;
            info.IsHuman = avatar.isHuman;
            info.IsValid = avatar.isValid;
            if (avatar.isHuman)
            {
                try
                {
                    foreach (var hb in avatar.humanDescription.human)
                        info.HumanBones[hb.humanName] = hb.boneName;
                }
                catch (Exception e)
                {
                    info.HumanBones["error"] = e.Message;
                }
            }
        }

        try
        {
            info.Layers = a.layerCount;
            foreach (var p in a.parameters)
                info.Parameters.Add($"{p.name}:{p.type}");
        }
        catch
        {
        }

        return info;
    }

    private static void AddPlayerData(CharacterDump dump, GameObject player)
    {
        var race = LocalPlayer.RaceSystem;
        if (race)
        {
            try
            {
                dump.CurrentRace = race.CurrentRace.ToString();
                dump.ExpressionBlendsStartIndex = race.GetExpressionBlendsStartIndex();
            }
            catch
            {
            }

            var races = race._races;
            if (races != null)
            {
                for (int i = 0; i < races.Count; i++)
                {
                    var r = races[i];
                    if (!r)
                        continue;
                    dump.Races.Add(new RaceInfo
                    {
                        Index = i,
                        Race = r.GetRace.ToString(),
                        Asset = r.name,
                        HeadAssetGuid = r.HeadAsset?.AssetGUID ?? string.Empty,
                        ArmsAssetGuid = r.ArmsAsset?.AssetGUID ?? string.Empty
                    });
                }
            }
        }

        var clothing = player.GetComponentInChildren<PlayerClothingSystem>(true);
        if (!clothing)
            return;

        var defaults = new HashSet<int>();
        if (clothing._defaultClothing != null)
        {
            foreach (var c in clothing._defaultClothing)
                if (c)
                    defaults.Add(c.ItemId);
        }

        var worn = new HashSet<int>();
        try
        {
            foreach (var id in clothing.GetCurrentClothingIds())
                worn.Add(id);
        }
        catch
        {
        }

        if (clothing._allClothing == null)
            return;
        foreach (var c in clothing._allClothing)
        {
            if (!c)
                continue;
            dump.Clothing.Add(new ClothingInfo
            {
                Asset = c.name,
                ItemId = c.ItemId,
                Slot = c.Slot.ToString(),
                RenderableGuid = c.Renderable?.AssetGUID ?? string.Empty,
                Default = defaults.Contains(c.ItemId),
                Worn = worn.Contains(c.ItemId)
            });
        }
    }

    private static void WriteIndex(string outDir, List<CharacterDump> dumps)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RigProbe {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("source\tname\tactorType\tactive\tskinnedMeshes\tuniqueBones\thumanoid\trigFamily\tplayerNameMatch\tpath");
        foreach (var d in dumps)
        {
            var unique = d.SkinnedMeshes.SelectMany(s => s.Bones).Where(b => b != "null").Distinct().Count();
            var human = d.Animators.Any(a => a.IsHuman);
            sb.AppendLine($"{d.Source}\t{d.Name}\t{d.ActorType}\t{d.Active}\t{d.SkinnedMeshes.Count}\t{unique}\t{human}\t{d.RigFamily}\t{d.PlayerNameMatch}\t{d.Path}");
        }
        File.WriteAllText(Path.Combine(outDir, "index.tsv"), sb.ToString());
    }

    private static void WriteFamilies(string outDir, List<CharacterDump> dumps)
    {
        var sb = new StringBuilder();
        foreach (var g in dumps.Where(d => d.RigFamily.Length > 0).GroupBy(d => d.RigFamily).OrderByDescending(g => g.Count()))
        {
            var bones = g.First().SkinnedMeshes.SelectMany(s => s.Bones).Where(b => b != "null").Select(LeafName).Distinct().OrderBy(b => b).ToList();
            sb.AppendLine($"family {g.Key} bones={bones.Count} playerNameMatch={g.First().PlayerNameMatch}");
            sb.AppendLine($"  members: {string.Join(", ", g.Select(d => $"{d.Source}:{d.Name}"))}");
            sb.AppendLine($"  sample: {string.Join(" ", bones.Take(25))}");
            sb.AppendLine();
        }
        File.WriteAllText(Path.Combine(outDir, "rigfamilies.txt"), sb.ToString());
    }

    private static string Family(CharacterDump d)
    {
        var names = d.SkinnedMeshes.SelectMany(s => s.Bones).Where(b => b != "null").Select(LeafName).Distinct().OrderBy(b => b).ToList();
        if (names.Count == 0)
            return string.Empty;
        using var sha = SHA1.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", names)));
        return Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    private static string PlayerMatch(CharacterDump d)
    {
        var names = d.SkinnedMeshes.SelectMany(s => s.Bones).Where(b => b != "null").Select(LeafName).Distinct().ToList();
        if (names.Count == 0)
            return "n/a";
        var hit = names.Count(n => _playerBoneNames.Contains(n));
        return $"{hit}/{names.Count} ({hit * 100 / names.Count}%)";
    }

    private static void AddWithAncestors(Transform t, Transform root, HashSet<int> ids)
    {
        while (t)
        {
            if (!ids.Add(t.GetInstanceID()))
                return;
            if (t == root)
                return;
            t = t.parent;
        }
    }

    private static bool IsUnder(Transform t, Transform ancestor)
    {
        while (t)
        {
            if (t == ancestor)
                return true;
            t = t.parent;
        }
        return false;
    }

    private static Transform FindChild(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name)
                return t;
        return null;
    }

    private static string LeafName(string path)
    {
        var i = path.LastIndexOf('/');
        return i < 0 ? path : path.Substring(i + 1);
    }

    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var ch in s)
            sb.Append(invalid.Contains(ch) || ch == ':' || ch == ' ' ? '_' : ch);
        return sb.ToString();
    }

    private static float[] V3(Vector3 v)
    {
        return new[] { R(v.x), R(v.y), R(v.z) };
    }

    private static float[] Q(Quaternion q)
    {
        return new[] { R(q.x), R(q.y), R(q.z), R(q.w) };
    }

    private static float R(float f)
    {
        return (float)Math.Round(f, 5);
    }

    private static string HierPath(Transform t)
    {
        var parts = new List<string>();
        while (t)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string RelPath(Transform t, Transform root)
    {
        if (t == root)
            return t.name;
        var parts = new List<string>();
        while (t && t != root)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void Say(string text)
    {
        SonsTools.ShowMessage(text, 6f);
        RLog.Msg(text);
    }
}

public class CharacterDump
{
    public string Name { get; set; }
    public string Source { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string Path { get; set; }
    public bool Active { get; set; }
    public string RigFamily { get; set; } = string.Empty;
    public string PlayerNameMatch { get; set; } = string.Empty;
    public string CurrentRace { get; set; }
    public int? ExpressionBlendsStartIndex { get; set; }
    public List<RaceInfo> Races { get; set; } = new();
    public List<ClothingInfo> Clothing { get; set; } = new();
    public List<AnimatorInfo> Animators { get; set; } = new();
    public List<BoneInfo> Rig { get; set; } = new();
    public List<SmrInfo> SkinnedMeshes { get; set; } = new();
    public List<string> AttachedMeshes { get; set; } = new();
}

public class BoneInfo
{
    public string Path { get; set; }
    public string Parent { get; set; }
    public float[] LocalPosition { get; set; }
    public float[] LocalRotation { get; set; }
    public float[] LocalScale { get; set; }
}

public class SmrInfo
{
    public string Path { get; set; }
    public string Mesh { get; set; }
    public bool Enabled { get; set; }
    public bool Active { get; set; }
    public bool Readable { get; set; }
    public int Vertices { get; set; }
    public int SubMeshes { get; set; }
    public string RootBone { get; set; }
    public List<string> Bones { get; set; } = new();
    public List<RestInfo> BindRest { get; set; } = new();
    public List<string> BlendShapes { get; set; } = new();
    public List<MaterialInfo> Materials { get; set; } = new();
}

public class RestInfo
{
    public string Bone { get; set; }
    public float[] Position { get; set; }
    public float[] Rotation { get; set; }
}

public class MaterialInfo
{
    public string Name { get; set; }
    public string Shader { get; set; }
    public Dictionary<string, string> Textures { get; set; } = new();
}

public class AnimatorInfo
{
    public string Path { get; set; }
    public bool Enabled { get; set; }
    public string Controller { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public bool IsHuman { get; set; }
    public bool IsValid { get; set; }
    public int Layers { get; set; }
    public Dictionary<string, string> HumanBones { get; set; } = new();
    public List<string> Parameters { get; set; } = new();
}

public class RaceInfo
{
    public int Index { get; set; }
    public string Race { get; set; }
    public string Asset { get; set; }
    public string HeadAssetGuid { get; set; }
    public string ArmsAssetGuid { get; set; }
}

public class ClothingInfo
{
    public string Asset { get; set; }
    public int ItemId { get; set; }
    public string Slot { get; set; }
    public string RenderableGuid { get; set; }
    public bool Default { get; set; }
    public bool Worn { get; set; }
}
