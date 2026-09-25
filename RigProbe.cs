using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Endnight.Animation;
using HarmonyLib;
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
    private static readonly Dictionary<string, string> PlayerParents = new();
    private static readonly Dictionary<string, string> PlayerPaths = new();

    protected override void OnSdkInitialized()
    {
        RLog.Msg("RigProbe loaded. Commands: rigprobe [filter], rigspawn <TypeName> [variation], rigmap, rigtest [self|off]");
    }

    [DebugCommand("rigprobe")]
    private static void ProbeCommand(string args)
    {
        try
        {
            Probe((args ?? string.Empty).Trim());
        }
        catch (Exception e)
        {
            RLog.Error($"RigProbe failed: {e}");
        }
    }

    [DebugCommand("rigspawn")]
    private static void SpawnCommand(string args)
    {
        var parts = (args ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !Enum.TryParse<VailActorTypeId>(parts[0], true, out var id))
        {
            Say("Usage: rigspawn <TypeName> [variation]  e.g. rigspawn Brandy");
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

    private static readonly string[][] FemaleChains = BuildChains(true);
    private static readonly string[][] PlayerChains = BuildChains(false);

    private static bool _patched;
    private static GameObject _clone;
    private static bool _selfMode;
    private static float _hipScale = 1f;
    private static readonly List<Transform> DriveFemale = new();
    private static readonly List<Transform> DrivePlayer = new();
    private static readonly List<Quaternion> DriveOffset = new();
    private static Transform _femaleHips;
    private static Transform _playerHips;

    private static string[][] BuildChains(bool female)
    {
        var chains = new List<string[]>();
        chains.Add(female
            ? new[] { "c_hip_SC", "C_spine0_SC", "C_spine1_SC", "C_spine2_SC", "C_neck0_SC", "C_neck1_SC", "C_head0_SC" }
            : new[] { "Hips", "Spine", "Spine1", "Spine2", "Neck", "Neck1", "Head" });
        foreach (var side in new[] { ("L", "l", "Left"), ("R", "r", "Right") })
        {
            var (u, l, p) = side;
            chains.Add(female
                ? new[] { $"{u}_armPrnt0_SC", $"{u}_arm0_SC", $"{u}_arm1_SC", $"{u}_hand00_SC" }
                : new[] { $"{p}Shoulder", $"{p}Arm", $"{p}ForeArm", $"{p}Hand" });
            chains.Add(female
                ? new[] { $"{u}_leg0_SC", $"{u}_leg1_SC", $"{u}_foot00_SC", $"{u}_foot01_SC" }
                : new[] { $"{p}UpLeg", $"{p}Leg", $"{p}Foot", $"{p}ToeBase" });
            foreach (var (f, pf) in new[] { ("A", "Index"), ("B", "Middle"), ("C", "Ring"), ("D", "Pinky") })
            {
                chains.Add(female
                    ? new[] { $"{l}_handAFinger{f}0_SC", $"{l}_handAFinger{f}1_SC", $"{l}_handAFinger{f}2_SC" }
                    : new[] { $"{p}Hand{pf}1", $"{p}Hand{pf}2", $"{p}Hand{pf}3" });
            }
            chains.Add(female
                ? new[] { $"{l}_handAThumbA0_SC", $"{l}_handAThumbA1_SC", $"{l}_handAThumbA2_SC" }
                : new[] { $"{p}HandThumb1", $"{p}HandThumb2", $"{p}HandThumb3" });
        }
        return chains.ToArray();
    }

    [DebugCommand("rigmap")]
    private static void MapCommand(string args)
    {
        try
        {
            var player = LocalPlayer.GameObject;
            if (!player)
            {
                Say("Load into a game first");
                return;
            }
            var source = FindCarryFemale(player);
            if (!source)
            {
                Say("CarryBody/FemaleCannibal not found on the player");
                return;
            }
            var sb = new StringBuilder();
            BuildDrive(source, player, sb);
            foreach (var a in player.GetComponentsInChildren<Animator>(true))
                sb.AppendLine($"player animator {HierPath(a.transform)} avatar={(a.avatar ? a.avatar.name : "null")} isHuman={(a.avatar && a.avatar.isHuman)}");
            foreach (var a in source.GetComponentsInChildren<Animator>(true))
                sb.AppendLine($"carry animator {HierPath(a.transform)} avatar={(a.avatar ? a.avatar.name : "null")} isHuman={(a.avatar && a.avatar.isHuman)}");
            var outPath = Path.Combine(LoaderEnvironment.UserDataDirectory, "RigMap.txt");
            File.WriteAllText(outPath, sb.ToString());
            Say($"RigMap: {DriveFemale.Count} bones mapped, written to {outPath}");
        }
        catch (Exception e)
        {
            RLog.Error($"rigmap failed: {e}");
        }
    }

    [DebugCommand("rigtest")]
    private static void TestCommand(string args)
    {
        try
        {
            args = (args ?? string.Empty).Trim().ToLowerInvariant();
            if (_clone)
                UnityEngine.Object.Destroy(_clone);
            _clone = null;
            DriveFemale.Clear();
            DrivePlayer.Clear();
            DriveOffset.Clear();
            if (args == "off")
            {
                Say("rigtest off");
                return;
            }

            var player = LocalPlayer.GameObject;
            if (!player)
            {
                Say("Load into a game first");
                return;
            }
            var source = FindCarryFemale(player);
            if (!source)
            {
                Say("CarryBody/FemaleCannibal not found on the player");
                return;
            }

            if (!_patched)
            {
                new Harmony("RigProbe.RigTest").CreateClassProcessor(typeof(ClothingLateUpdatePatch)).Patch();
                _patched = true;
            }

            _clone = UnityEngine.Object.Instantiate(source.gameObject);
            _clone.name = "RigProbeFemale";
            _clone.transform.SetParent(null, true);
            PrepareClone(_clone);

            var sb = new StringBuilder();
            BuildDrive(_clone.transform, player, sb);
            _selfMode = args == "self";
            _clone.SetActive(true);
            RLog.Msg(sb.ToString());
            Say($"rigtest {(_selfMode ? "self" : "front")}: driving {DriveFemale.Count} bones");
        }
        catch (Exception e)
        {
            RLog.Error($"rigtest failed: {e}");
        }
    }

    private static Transform FindCarryFemale(GameObject player)
    {
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "FemaleCannibal" && t.parent && t.parent.name == "CarryBody")
                return t;
        }
        return null;
    }

    private static void PrepareClone(GameObject clone)
    {
        foreach (var a in clone.GetComponentsInChildren<Animator>(true))
            a.enabled = false;
        foreach (var m in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            try
            {
                UnityEngine.Object.DestroyImmediate(m);
            }
            catch
            {
                m.enabled = false;
            }
        }
        foreach (var c in clone.GetComponentsInChildren<Collider>(true))
            c.enabled = false;
        foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true))
            rb.isKinematic = true;
        foreach (var r in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var keep = r.name == "C1:Female_lp" || r.name.StartsWith("C1:cb_female_");
            r.enabled = keep;
            if (!keep)
                continue;
            r.updateWhenOffscreen = true;
            var t = r.transform;
            while (t)
            {
                t.gameObject.SetActive(true);
                t = t.parent;
            }
        }
    }

    private static void BuildDrive(Transform femaleRoot, GameObject player, StringBuilder sb)
    {
        DriveFemale.Clear();
        DrivePlayer.Clear();
        DriveOffset.Clear();
        _femaleHips = null;
        _playerHips = null;

        var race = LocalPlayer.RaceSystem;
        var root = race ? race._animationRoot : player.transform.Find("PlayerAnimator/Root");
        var hips = root ? root.Find("Hips") : null;
        if (!hips)
        {
            sb.AppendLine("player Hips not found");
            return;
        }

        var fBones = new Dictionary<string, Transform>();
        foreach (var t in femaleRoot.GetComponentsInChildren<Transform>(true))
            if (!fBones.ContainsKey(t.name))
                fBones[t.name] = t;
        var pBones = new Dictionary<string, Transform>();
        foreach (var t in hips.GetComponentsInChildren<Transform>(true))
            if (!pBones.ContainsKey(t.name))
                pBones[t.name] = t;

        var fRest = CollectRest(femaleRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true), fBones.Values);
        var pRest = CollectRest(player.GetComponentsInChildren<SkinnedMeshRenderer>(true), pBones.Values);

        for (int c = 0; c < FemaleChains.Length; c++)
        {
            var fc = FemaleChains[c];
            var pc = PlayerChains[c];
            for (int i = 0; i < fc.Length; i++)
            {
                fBones.TryGetValue("C1:" + fc[i], out var f);
                pBones.TryGetValue(pc[i], out var p);
                if (!f || !p)
                {
                    sb.AppendLine($"MISS {fc[i]} -> {pc[i]} female={(bool)f} player={(bool)p}");
                    continue;
                }
                var hasF = fRest.TryGetValue(f.GetInstanceID(), out var rf);
                var hasP = pRest.TryGetValue(p.GetInstanceID(), out var rp);
                if (!hasF || !hasP)
                {
                    sb.AppendLine($"NOREST {fc[i]} -> {pc[i]} femaleRest={hasF} playerRest={hasP}");
                    continue;
                }

                var align = Quaternion.identity;
                float angle = 0f;
                if (i + 1 < fc.Length && fBones.TryGetValue("C1:" + fc[i + 1], out var fChild) && pBones.TryGetValue(pc[i + 1], out var pChild)
                    && fRest.TryGetValue(fChild.GetInstanceID(), out var rfc) && pRest.TryGetValue(pChild.GetInstanceID(), out var rpc))
                {
                    var df = Pos(rfc) - Pos(rf);
                    var dp = Pos(rpc) - Pos(rp);
                    if (df.sqrMagnitude > 1e-8f && dp.sqrMagnitude > 1e-8f)
                    {
                        align = Quaternion.FromToRotation(df, dp);
                        angle = Vector3.Angle(df, dp);
                    }
                }

                var offset = Quaternion.Inverse(rp.rotation) * (align * rf.rotation);
                DriveFemale.Add(f);
                DrivePlayer.Add(p);
                DriveOffset.Add(offset);
                sb.AppendLine($"MAP {fc[i]} -> {pc[i]} restAngle={angle:F1} fPos={Pos(rf)} pPos={Pos(rp)}");

                if (c == 0 && i == 0)
                {
                    _femaleHips = f;
                    _playerHips = p;
                    var ph = Pos(rp).y;
                    _hipScale = Mathf.Abs(ph) > 0.01f ? Pos(rf).y / ph : 1f;
                    sb.AppendLine($"hips female={Pos(rf)} {rf.rotation.eulerAngles} player={Pos(rp)} {rp.rotation.eulerAngles} hipScale={_hipScale:F3}");
                }
            }
        }
    }

    private static Dictionary<int, Matrix4x4> CollectRest(IEnumerable<SkinnedMeshRenderer> renderers, IEnumerable<Transform> allowed)
    {
        var ids = new HashSet<int>(allowed.Select(t => t.GetInstanceID()));
        var rest = new Dictionary<int, Matrix4x4>();
        foreach (var r in renderers)
        {
            var mesh = r.sharedMesh;
            var bones = r.bones;
            if (!mesh || bones == null)
                continue;
            var binds = mesh.bindposes;
            if (binds == null)
                continue;
            int n = Math.Min(bones.Length, binds.Length);
            for (int i = 0; i < n; i++)
            {
                var b = bones[i];
                if (!b)
                    continue;
                var id = b.GetInstanceID();
                if (!ids.Contains(id) || rest.ContainsKey(id))
                    continue;
                rest[id] = binds[i].inverse;
            }
        }
        return rest;
    }

    private static Vector3 Pos(Matrix4x4 m)
    {
        return new Vector3(m.m03, m.m13, m.m23);
    }

    internal static void Drive()
    {
        if (!_clone || !_femaleHips || !_playerHips)
            return;
        var player = LocalPlayer.GameObject;
        if (!player)
            return;

        var pt = player.transform;
        var yaw = _selfMode ? Quaternion.identity : Quaternion.AngleAxis(180f, pt.up);
        var anchor = _selfMode ? pt.position : pt.position + pt.forward * 2.5f;

        var local = _playerHips.position - pt.position;
        var vertical = Vector3.Project(local, pt.up);
        local = local - vertical + vertical * _hipScale;
        _femaleHips.position = anchor + yaw * local;

        for (int i = 0; i < DriveFemale.Count; i++)
        {
            var f = DriveFemale[i];
            var p = DrivePlayer[i];
            if (!f || !p)
                continue;
            f.rotation = yaw * p.rotation * DriveOffset[i];
        }
    }

    [HarmonyPatch(typeof(PlayerClothingSystem), nameof(PlayerClothingSystem.LateUpdate))]
    private static class ClothingLateUpdatePatch
    {
        private static void Postfix(PlayerClothingSystem __instance)
        {
            try
            {
                if (_clone && __instance.IsLocalPlayer())
                    Drive();
            }
            catch (Exception e)
            {
                RLog.Error($"rigtest drive failed: {e.Message}");
                if (_clone)
                    UnityEngine.Object.Destroy(_clone);
                _clone = null;
            }
        }
    }

    private static void Probe(string filter)
    {
        var player = LocalPlayer.GameObject;
        var race = LocalPlayer.RaceSystem;
        if (!player || !race)
        {
            Say("Load into a game first");
            return;
        }

        var root = race._animationRoot;
        if (!root)
            root = player.transform.Find("PlayerAnimator/Root");
        if (!root)
        {
            Say("Could not find the player animation root");
            return;
        }

        BuildPlayerRig(root);

        var sb = new StringBuilder();
        sb.AppendLine($"RigProbe {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Player animation root: {HierPath(root)}");
        sb.AppendLine($"Player rig transforms: {PlayerPaths.Count}");
        sb.AppendLine($"Current race: {race.CurrentRace}");
        try
        {
            sb.AppendLine($"ExpressionBlendsStartIndex: {race.GetExpressionBlendsStartIndex()}");
        }
        catch (Exception e)
        {
            sb.AppendLine($"ExpressionBlendsStartIndex: error {e.Message}");
        }
        sb.AppendLine($"Head: {Name(race.GetHead())}  LeftArm: {Name(race.GetLeftArm())}  RightArm: {Name(race.GetRightArm())}");
        sb.AppendLine();

        sb.AppendLine("==== PLAYER RENDERERS ====");
        DumpObject(sb, "Player", player, true);
        sb.AppendLine();

        sb.AppendLine("==== ACTORS ====");
        var ids = Enum.GetValues(typeof(VailActorTypeId)).Cast<VailActorTypeId>()
            .Where(i => i != VailActorTypeId.None)
            .Where(i => filter.Length == 0 || i.ToString().IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();

        int probed = 0;
        foreach (var id in ids)
        {
            VailActor prefab = null;
            try
            {
                prefab = ActorTools.GetPrefab(id);
            }
            catch (Exception e)
            {
                sb.AppendLine($"[{id} prefab] error {e.Message}");
            }
            if (prefab)
            {
                DumpObject(sb, $"{id} prefab", prefab.gameObject, false);
                probed++;
            }

            VailActor live = null;
            try
            {
                live = ActorTools.GetActors(id)?.FirstOrDefault(a => a);
            }
            catch (Exception e)
            {
                sb.AppendLine($"[{id} live] error {e.Message}");
            }
            if (live)
            {
                DumpObject(sb, $"{id} live", live.gameObject, false);
                probed++;
            }
        }

        var dir = LoaderEnvironment.UserDataDirectory;
        var outPath = Path.Combine(dir, "RigProbe.txt");
        File.WriteAllText(outPath, sb.ToString());

        var rig = new StringBuilder();
        foreach (var kv in PlayerPaths.OrderBy(k => k.Value))
            rig.AppendLine(kv.Value);
        File.WriteAllText(Path.Combine(dir, "RigProbe_PlayerRig.txt"), rig.ToString());

        Say($"RigProbe: {probed} actor objects written to {outPath}");
    }

    private static void BuildPlayerRig(Transform root)
    {
        PlayerParents.Clear();
        PlayerPaths.Clear();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (PlayerPaths.ContainsKey(t.name))
                continue;
            PlayerPaths[t.name] = RelPath(t, root);
            PlayerParents[t.name] = t.parent ? t.parent.name : string.Empty;
        }
    }

    private static void DumpObject(StringBuilder sb, string label, GameObject go, bool allBlendShapes)
    {
        var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var swaps = go.GetComponentsInChildren<Sons.Ai.SwapSkinnedMeshWithPrefab>(true);

        int totalBones = 0, totalName = 0, totalParent = 0, remapCount = 0;
        var body = new StringBuilder();

        foreach (var r in renderers)
        {
            var a = Analyze(r, allBlendShapes);
            totalBones += a.Bones;
            totalName += a.NameMatch;
            totalParent += a.ParentMatch;
            if (a.HasRemap)
                remapCount++;
            body.Append(a.Text);
        }

        var summary = $"[{label}] go={go.name} renderers={renderers.Length} bones={totalBones} " +
                      $"nameMatch={Pct(totalName, totalBones)} parentMatch={Pct(totalParent, totalBones)} " +
                      $"remapCaches={remapCount} meshSwappers={swaps.Length}";
        sb.AppendLine(summary);
        foreach (var s in swaps)
        {
            string asset;
            try
            {
                asset = s._meshAsset ? s._meshAsset.name : "null";
            }
            catch
            {
                asset = "?";
            }
            sb.AppendLine($"  swapper {HierPath(s.transform)} meshAsset={asset}");
        }
        sb.Append(body);
        sb.AppendLine();
        RLog.Msg(summary);
    }

    private sealed class Result
    {
        public int Bones;
        public int NameMatch;
        public int ParentMatch;
        public bool HasRemap;
        public string Text;
    }

    private static Result Analyze(SkinnedMeshRenderer r, bool allBlendShapes)
    {
        var res = new Result();
        var missing = new List<string>();
        var parentDiff = new List<string>();
        int nulls = 0;

        var bones = r.bones;
        if (bones != null)
        {
            foreach (var b in bones)
            {
                if (!b)
                {
                    nulls++;
                    continue;
                }
                res.Bones++;
                if (PlayerParents.TryGetValue(b.name, out var expectedParent))
                {
                    res.NameMatch++;
                    var actualParent = b.parent ? b.parent.name : string.Empty;
                    if (actualParent == expectedParent)
                        res.ParentMatch++;
                    else
                        parentDiff.Add($"{b.name}({actualParent}!={expectedParent})");
                }
                else
                {
                    missing.Add(b.name);
                }
            }
        }

        var sb = new StringBuilder();
        var mesh = r.sharedMesh;
        int blendCount = mesh ? mesh.blendShapeCount : 0;

        sb.AppendLine($"  SMR {HierPath(r.transform)} active={r.gameObject.activeInHierarchy} mesh={(mesh ? mesh.name : "null")} " +
                      $"verts={(mesh ? mesh.vertexCount : 0)} bones={res.Bones} null={nulls} nameMatch={res.NameMatch} " +
                      $"parentMatch={res.ParentMatch} rootBone={(r.rootBone ? r.rootBone.name : "null")} blendShapes={blendCount} " +
                      $"materials={string.Join(",", r.sharedMaterials.Where(m => m).Select(m => m.name))}");

        var remap = r.GetComponent<SkinnedMeshBoneRemapCache>();
        if (remap)
        {
            res.HasRemap = true;
            sb.AppendLine($"    remap compat={remap._isCompatible} rootPath={remap._rootBonePath} " +
                          $"paths={(remap._bonePaths != null ? remap._bonePaths.Count : 0)} " +
                          $"srcBase={remap._sourceTransformBasePath} base={remap._transformBasePath}");
        }

        if (missing.Count > 0)
            sb.AppendLine($"    missing({missing.Count}): {string.Join(" ", missing.Take(60))}");
        if (parentDiff.Count > 0)
            sb.AppendLine($"    parentDiff({parentDiff.Count}): {string.Join(" ", parentDiff.Take(30))}");

        if (mesh && blendCount > 0)
        {
            var take = allBlendShapes ? blendCount : Math.Min(blendCount, 15);
            var names = new List<string>();
            for (int i = 0; i < take; i++)
                names.Add($"{i}:{mesh.GetBlendShapeName(i)}");
            sb.AppendLine($"    blendShapes: {string.Join(" ", names)}{(take < blendCount ? " ..." : string.Empty)}");
        }

        res.Text = sb.ToString();
        return res;
    }

    private static string Pct(int a, int b)
    {
        return b == 0 ? "n/a" : $"{a}/{b} ({a * 100 / b}%)";
    }

    private static string Name(GameObject go)
    {
        return go ? go.name : "null";
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